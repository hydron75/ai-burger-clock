namespace AiBurgerClock;

// The notification permission as the app last knew it, and the rule for sending against it. No AppKit here, so
// a synthetic check can drive it (MacNotificationPermissionSmoke).
//
// The permission is not read once: the user can switch it on or off in System Settings while the app runs. A
// notification that finds it off looks again before it is dropped, and the app looks again when it is activated.
internal sealed class MacNotificationPermission
{
    private readonly Action<Action<bool, bool>> read;
    private readonly Action<Action> onMain;
    private readonly Action switchedOff;
    private bool authorized;
    private bool answered;
    private bool stopped;

    // read: asks the system now and answers later, on any thread, with (allowed, decided at all).
    // onMain: runs an action on the main thread, where all state here is touched.
    // switchedOff: said once when a permission that was on is found off.
    internal MacNotificationPermission(Action<Action<bool, bool>> read, Action<Action> onMain, Action switchedOff)
    {
        this.read = read;
        this.onMain = onMain;
        this.switchedOff = switchedOff;
    }

    // True once macOS answered and the answer was "not allowed".
    internal bool Denied => answered && !authorized;

    // The answer to the permission request (main thread).
    internal void Answered(bool granted)
    {
        authorized = granted;
        answered = true;
    }

    // Sends now if the app is allowed. If not, looks at the system again first: allowed after all (switched on
    // in System Settings) sends this notification; otherwise it is dropped as before. Main thread.
    internal void Send(Action send)
    {
        if (stopped) return;
        if (authorized)
        {
            send();
            return;
        }
        read((allowed, decided) => onMain(() =>
        {
            if (stopped) return;
            Apply(allowed, decided);
            if (authorized) send();
        }));
    }

    // Looks at the system again (the app was activated). Main thread.
    internal void Refresh()
    {
        if (stopped) return;
        read((allowed, decided) => onMain(() =>
        {
            if (!stopped) Apply(allowed, decided);
        }));
    }

    internal void Stop() => stopped = true;

    private void Apply(bool allowed, bool decided)
    {
        // Not decided yet is not "denied": nothing changes, and no notice is due.
        if (!decided) return;
        bool wasOn = authorized;
        authorized = allowed;
        answered = true;
        if (wasOn && !allowed) switchedOff();
    }
}
