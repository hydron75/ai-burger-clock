namespace AiBurgerClock;

internal static class Program
{
    internal const string MutexName = @"Local\AiBurgerClock-5C62800F-7E19-471A-9BCA-2A7FB1F49138";

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        bool HasArgument(string value) => args.Contains(value, StringComparer.OrdinalIgnoreCase);

        // COM launch MUST precede normal mutex/UI/store/startup paths, even when a normal
        // tray instance owns the mutex. Acknowledging a notification has no application action.
        if (HasArgument("--quota-toast-activated"))
        {
            if (args.Any(value => !new[] { "--quota-toast-activated", "--quota-toast-test", "-Embedding", "/Embedding" }
                .Contains(value, StringComparer.OrdinalIgnoreCase))) return 2;
            return WindowsQuotaToastActivation.RunOnly(HasArgument("--quota-toast-test") ?
                WindowsQuotaToastIdentity.Test : WindowsQuotaToastIdentity.Production);
        }
        int toastTestIndex = Array.FindIndex(args, value => value.Equals("--quota-toast-test", StringComparison.OrdinalIgnoreCase));
        if (toastTestIndex >= 0)
            return args.Length == 2 && toastTestIndex == 0 ? WindowsQuotaToastIntegration.Run(args[1]) : 2;
        if (HasArgument("--register-quota-notifications") || HasArgument("--unregister-quota-notifications"))
        {
            bool unregister = HasArgument("--unregister-quota-notifications");
            if (args.Count(value => value.Equals("--register-quota-notifications", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("--unregister-quota-notifications", StringComparison.OrdinalIgnoreCase)) != 1 ||
                args.Any(value => !value.Equals(unregister ? "--unregister-quota-notifications" : "--register-quota-notifications", StringComparison.OrdinalIgnoreCase) &&
                    !(unregister && value.Equals("--dry-run", StringComparison.OrdinalIgnoreCase)))) return 2;
            try
            {
                if (HasArgument("--register-quota-notifications")) WindowsQuotaToastRegistration.Register(WindowsQuotaToastIdentity.Production);
                else WindowsQuotaToastRegistration.Unregister(WindowsQuotaToastIdentity.Production, HasArgument("--dry-run"));
                return 0;
            }
            catch (Exception error)
            { Console.Error.WriteLine($"Quota notification registration failed: {error.GetType().Name}, HRESULT=0x{error.HResult:X8}"); return 1; }
        }

        if (HasArgument("--self-test"))
            return SelfTest.Run();
        if (HasArgument("--check-providers"))
            return LiveStatusProbe.Run();
        if (HasArgument("--check-quotas"))
            return LiveQuotaProbe.RunAsync().GetAwaiter().GetResult();

        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            if (HasArgument("--smoke-test"))
            {
                Console.Error.WriteLine("Smoke test cannot run while AI Burger Clock is already running. Exit the tray app first.");
                return 2;
            }
            return 0;
        }

        ApplicationConfiguration.Initialize();
        if (HasArgument("--smoke-test"))
        {
            int reportIndex = Array.FindIndex(args, value => value.Equals("--report-directory", StringComparison.OrdinalIgnoreCase));
            string? reportDirectory = reportIndex >= 0 && reportIndex + 1 < args.Length ? args[reportIndex + 1] : null;
            return SmokeTest.Run(HasArgument("--verify-autostart"), reportDirectory);
        }

        using var activation = WindowsQuotaToastActivation.StartIfRegistered(WindowsQuotaToastIdentity.Production);
        using var context = new TrayApplicationContext(HasArgument("--autostart"));
        Application.Run(context);
        return 0;
    }
}
