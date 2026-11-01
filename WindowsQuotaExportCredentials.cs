using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace AiBurgerClock;

internal interface IWindowsQuotaExportCredentials
{
    bool Exists();
    string Read();
    void Replace(string token);
}

internal sealed class WindowsQuotaExportCredentials : IWindowsQuotaExportCredentials
{
    internal const string Target = "Hydron75.AIBurgerClock.SitesQuotaExport.v1";
    private const uint Generic = 1, LocalMachine = 2;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string TargetName, Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias, UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr value);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential value, uint flags);
    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr value);

    public bool Exists()
    {
        if (!CredRead(Target, Generic, 0, out var pointer))
        {
            if (Marshal.GetLastWin32Error() == 1168) return false;
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        CredFree(pointer);
        return true;
    }

    internal static void Validate(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Any(c => c < 33 || c > 126) || Utf8.GetByteCount(token) > 2560)
            throw new ArgumentException("Invalid service token.");
    }

    public string Read()
    {
        if (!CredRead(Target, Generic, 0, out var pointer)) throw new Win32Exception(Marshal.GetLastWin32Error());
        byte[]? bytes = null;
        try
        {
            var value = Marshal.PtrToStructure<Credential>(pointer);
            if (value.Type != Generic || value.Persist != LocalMachine || value.CredentialBlobSize is 0 or > 2560)
                throw new InvalidDataException("Invalid credential metadata.");
            bytes = new byte[value.CredentialBlobSize];
            Marshal.Copy(value.CredentialBlob, bytes, 0, bytes.Length);
            string token = Utf8.GetString(bytes);
            Validate(token);
            return token;
        }
        finally
        {
            if (bytes is not null) Array.Clear(bytes);
            CredFree(pointer);
        }
    }

    public void Replace(string token)
    {
        Validate(token);
        byte[] bytes = Utf8.GetBytes(token);
        IntPtr blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var value = new Credential
            {
                Type = Generic, TargetName = Target, Comment = "AI Burger Clock Sites quota export v1; UTF-8",
                CredentialBlobSize = (uint)bytes.Length, CredentialBlob = blob, Persist = LocalMachine,
                UserName = "AI Burger Clock", TargetAlias = ""
            };
            if (!CredWrite(ref value, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            for (int i = 0; i < bytes.Length; i++) Marshal.WriteByte(blob, i, 0);
            Marshal.FreeHGlobal(blob);
            Array.Clear(bytes);
        }
    }
}
