namespace AiBurgerClock;

// Mac-only setting for the web usage readers: the Gemini app (0.9.6) and Antigravity. The shared UsageStore has no key
// for it and shared files are not changed from the Mac side, so the flags live in one small file next to the database (a
// smoke run uses its own temporary folder). It holds no account data: only "is the Gemini reader on", "was a login ever
// completed here" and "did the user switch the Antigravity reader on".
internal sealed class MacGeminiWebSettings
{
    internal const string FileName = "gemini-web.settings";
    private readonly string path;

    private MacGeminiWebSettings(string path, bool enabled, bool hasSession, bool antigravity)
    {
        this.path = path;
        Enabled = enabled;
        HasSession = hasSession;
        AntigravityEnabled = antigravity;
    }

    // The reader is switched on (login completed and not turned off).
    internal bool Enabled { get; private set; }

    // A login completed since the last logout: the app's own web data store may hold a session.
    internal bool HasSession { get; private set; }

    // The Antigravity menu item is checked (default off). The reader runs only while the Gemini reader is on as well;
    // turning the Gemini reader off keeps this choice, logout clears it.
    internal bool AntigravityEnabled { get; private set; }

    internal bool AntigravityActive => Enabled && AntigravityEnabled;

    // A missing or unreadable file means the safe default: off, nothing to log out of.
    internal static MacGeminiWebSettings Load(string directory)
    {
        string path = Path.Combine(directory, FileName);
        bool enabled = false, session = false, antigravity = false;
        try
        {
            foreach (string line in File.ReadAllLines(path))
            {
                if (line == "enabled=1") enabled = true;
                else if (line == "session=1") session = true;
                else if (line == "antigravity=1") antigravity = true;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return new(path, enabled && session, session, antigravity);
    }

    // Writes through a temporary file so a crash never leaves half a setting. False when it could not be saved (the in-memory
    // values are still the requested ones, so the menu and the running reader agree).
    internal bool Save(bool enabled, bool hasSession)
    {
        enabled &= hasSession; // On without a session cannot happen.
        // The running state follows the request even when the file cannot be written (the caller reports that).
        Enabled = enabled;
        HasSession = hasSession;
        return Write();
    }

    internal bool SaveAntigravity(bool enabled)
    {
        AntigravityEnabled = enabled;
        return Write();
    }

    private bool Write()
    {
        string temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary,
                $"enabled={(Enabled ? 1 : 0)}\nsession={(HasSession ? 1 : 0)}\nantigravity={(AntigravityEnabled ? 1 : 0)}\n");
            File.Move(temporary, path, overwrite: true);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        return true;
    }
}
