using AppKit;

namespace AiBurgerClock;

internal static class Program
{
    private static MacApplication? application;

    private static int Main(string[] args)
    {
        bool smoke = args.Contains("--smoke-test", StringComparer.Ordinal);
        if (args.Any(arg => arg != "--smoke-test"))
        {
            Console.Error.WriteLine("지원 옵션: --smoke-test (임시 DB, 네트워크·CLI·계정·설정 변경 없음)");
            return 2;
        }
        string dataDirectory = smoke
            ? Path.Combine(Path.GetTempPath(), "aiburgerclock-mac-smoke-" + Guid.NewGuid().ToString("N"))
            : AppPaths.DataDirectory;
        Directory.CreateDirectory(dataDirectory);
        // LaunchServices normally reuses an existing .app; also guard direct executable launches.
        FileStream instance;
        try
        {
            instance = new(Path.Combine(dataDirectory, "instance.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            Console.Error.WriteLine("AI Burger Clock이 이미 실행 중입니다.");
            return 2;
        }
        using (instance)
        {
            NSApplication.Init();
            application = new MacApplication(new UsageStore(Path.Combine(dataDirectory, "burgerclock.db")), smoke);
            NSApplication.SharedApplication.Delegate = application;
            NSApplication.Main([]);
            int result = application.ExitCode;
            application.Dispose();
            application = null;
            if (smoke)
            {
                // Only this invocation's newly created GUID-named temporary directory.
                try { Directory.Delete(dataDirectory, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return result;
        }
    }
}
