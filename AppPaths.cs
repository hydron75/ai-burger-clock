namespace AiBurgerClock;

internal static class AppPaths
{
    internal static string DataDirectory => DataDirectoryFor(OperatingSystem.IsMacOS(),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    internal static string DataDirectoryFor(bool macOS, string homeDirectory, string localApplicationData) =>
        macOS ? Path.Combine(homeDirectory, "Library", "Application Support", "AIBurgerClock") :
            Path.Combine(localApplicationData, "AIBurgerClock");
}
