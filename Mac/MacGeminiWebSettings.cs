namespace AiBurgerClock;

// Mac-only setting for the Gemini app usage reader (0.9.6). The shared UsageStore has no key for it and shared files are
// not changed from the Mac side, so the two flags live in one small file next to the database (a smoke run uses its own
// temporary folder). It holds no account data: only "is the reader on" and "was a login ever completed here".
internal sealed class MacGeminiWebSettings
{
    internal const string FileName = "gemini-web.settings";
    private readonly string path;

    private MacGeminiWebSettings(string path, bool enabled, bool hasSession)
    {
        this.path = path;
        Enabled = enabled;
        HasSession = hasSession;
    }

    // The reader is switched on (login completed and not turned off).
    internal bool Enabled { get; private set; }

    // A login completed since the last logout: the app's own web data store may hold a session.
    internal bool HasSession { get; private set; }

    // A missing or unreadable file means the safe default: off, nothing to log out of.
    internal static MacGeminiWebSettings Load(string directory)
    {
        string path = Path.Combine(directory, FileName);
        bool enabled = false, session = false;
        try
        {
            foreach (string line in File.ReadAllLines(path))
            {
                if (line == "enabled=1") enabled = true;
                else if (line == "session=1") session = true;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return new(path, enabled && session, session);
    }

    // Writes through a temporary file so a crash never leaves half a setting. False when it could not be saved (the in-memory
    // values are still the requested ones, so the menu and the running reader agree).
    internal bool Save(bool enabled, bool hasSession)
    {
        enabled &= hasSession; // On without a session cannot happen.
        // The running state follows the request even when the file cannot be written (the caller reports that).
        Enabled = enabled;
        HasSession = hasSession;
        string temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, $"enabled={(enabled ? 1 : 0)}\nsession={(hasSession ? 1 : 0)}\n");
            File.Move(temporary, path, overwrite: true);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        return true;
    }
}
