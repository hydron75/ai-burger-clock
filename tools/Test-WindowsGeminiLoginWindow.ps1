#requires -Version 7.0
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = Split-Path -Parent $PSScriptRoot
$report = Join-Path $repository ('artifacts/gemini-login-window/' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($report) | Out-Null
$project = Join-Path $report 'LoginWindowFixture.csproj'
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$(LoginWindowSource)" Link="WindowsGeminiLoginWindow.cs" />
  </ItemGroup>
</Project>
'@ | Set-Content -LiteralPath $project -Encoding utf8
@'
using System.Runtime.InteropServices;
using System.Text.Json;
using AiBurgerClock;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        GetStartupInfo(out var startup);
        ApplicationConfiguration.Initialize();
        using var form = new Form
        {
            Text = "Gemini login visibility fixture",
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-10000, -10000)
        };
        if (args[0] == "fixed")
            WindowsGeminiLoginWindow.Show(form);
        else
            form.Show();
        var result = new
        {
            UseShowWindow = (startup.Flags & 1) != 0,
            startup.ShowCommand,
            Visible = WindowsGeminiLoginWindow.IsWindowVisible(form.Handle)
        };
        File.WriteAllText(args[1], JsonSerializer.Serialize(result));
    }

    [DllImport("kernel32.dll", EntryPoint = "GetStartupInfoW")]
    private static extern void GetStartupInfo(out StartupInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        internal uint Size;
        internal IntPtr Reserved, Desktop, Title;
        internal uint X, Y, Width, Height, XChars, YChars, Fill, Flags;
        internal ushort ShowCommand, ReservedSize;
        internal IntPtr ReservedBytes, StandardInput, StandardOutput, StandardError;
    }
}
'@ | Set-Content -LiteralPath (Join-Path $report 'Program.cs') -Encoding utf8

Push-Location -LiteralPath $repository
try {
    dotnet build $project -c Release --nologo -warnaserror "-p:LoginWindowSource=$(Join-Path $repository 'WindowsGeminiLoginWindow.cs')"
    if ($LASTEXITCODE -ne 0) { throw 'Login visibility fixture build failed.' }
} finally { Pop-Location }

$exe = Join-Path $report 'bin/Release/net10.0-windows/LoginWindowFixture.exe'
function Invoke-WindowFixture([string]$Style, [string]$Mode, [string]$Output) {
    $process = Start-Process -FilePath $exe -WorkingDirectory $report -WindowStyle $Style -ArgumentList $Mode, $Output -PassThru
    try {
        if (-not $process.WaitForExit(15000)) {
            $process.Kill()
            throw 'Login visibility fixture timed out.'
        }
        if ($process.ExitCode -ne 0) { throw "Login visibility fixture exited with $($process.ExitCode)." }
        Get-Content -LiteralPath (Join-Path $report $Output) -Raw | ConvertFrom-Json
    } finally { $process.Dispose() }
}

$normal = Invoke-WindowFixture Normal plain normal.json
$hidden = Invoke-WindowFixture Hidden plain hidden.json
$fixed = Invoke-WindowFixture Hidden fixed fixed.json
$passed = $normal.Visible -and $hidden.UseShowWindow -and $hidden.ShowCommand -eq 0 -and -not $hidden.Visible -and
    $fixed.UseShowWindow -and $fixed.ShowCommand -eq 0 -and $fixed.Visible
[pscustomobject]@{ Passed = $passed; Normal = $normal; Hidden = $hidden; Fixed = $fixed; Report = $report } |
    ConvertTo-Json -Depth 3
if (-not $passed) { throw 'Login visibility regression did not match the expected Win32 states.' }
