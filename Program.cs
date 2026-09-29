namespace AiBurgerClock;

internal static class Program
{
    private const string MutexName = @"Local\AiBurgerClock-5C62800F-7E19-471A-9BCA-2A7FB1F49138";

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        bool HasArgument(string value) => args.Contains(value, StringComparer.OrdinalIgnoreCase);

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

        using var context = new TrayApplicationContext(HasArgument("--autostart"));
        Application.Run(context);
        return 0;
    }
}
