namespace AiBurgerClock;

// Finder-launched GUI apps may not inherit the user's interactive shell PATH.
// Only absolute installation directories are considered; never execute a shell
// command or search the current working directory for a CLI.
internal static class MacCliPaths
{
    internal static IReadOnlyList<string> Candidates(QuotaProvider provider, IEnumerable<string> pathEntries,
        string homeDirectory, string currentDirectory)
    {
        string name = provider switch
        {
            QuotaProvider.Codex => "codex",
            QuotaProvider.Claude => "claude",
            QuotaProvider.Gemini => "agy",
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
        var directories = pathEntries.Concat([homeDirectory.TrimEnd('/') + "/.local/bin",
            "/opt/homebrew/bin", "/usr/local/bin"]);
        return directories.Select(entry => entry.Trim().Trim('"').TrimEnd('/'))
            .Where(path => path.StartsWith('/') && path.Length > 1 &&
                !path.Split('/').Any(part => part is "." or "..") &&
                !string.Equals(path, currentDirectory.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal).Select(path => path + "/" + name).ToArray();
    }
}
