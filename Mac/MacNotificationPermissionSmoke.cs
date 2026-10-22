namespace AiBurgerClock;

// Synthetic check of MacNotificationPermission: no notification center, no AppKit, no windows. The "system"
// is a field the test changes (the user switching the app on or off in System Settings) and an asynchronous
// read that is answered later, the way GetNotificationSettings answers.
internal static class MacNotificationPermissionSmoke
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Notification permission: " + message);
    }

    private sealed class World
    {
        internal bool Allowed, Determined = true;
        internal int Reads, Notices;
        internal readonly Queue<Action> Replies = new(), MainThread = new();
        internal Action<Action<bool, bool>> Read => answer =>
        {
            Reads++;
            (bool allowed, bool determined) = (Allowed, Determined); // what the system says at the moment of the read
            Replies.Enqueue(() => answer(allowed, determined));
        };
        internal Action<Action> OnMain => action => MainThread.Enqueue(action);
        // Replies come from another thread and are handed to the main thread.
        internal void Pump()
        {
            while (Replies.Count > 0 || MainThread.Count > 0)
            {
                if (Replies.Count > 0) Replies.Dequeue()();
                if (MainThread.Count > 0) MainThread.Dequeue()();
            }
        }
    }

    internal static string Verify()
    {
        int scenarios = 0;

        // Allowed from the start: sent at once, no read.
        {
            var world = new World { Allowed = true };
            MacNotificationPermission permission = Create(world);
            permission.Answered(true);
            int sent = 0;
            permission.Send(() => sent++);
            Check(sent == 1 && world.Reads == 0 && !permission.Denied, "an allowed app must send at once without reading again.");
            scenarios++;
        }

        // Denied at the first look, then allowed in System Settings: the next notification is sent without a restart.
        {
            var world = new World { Allowed = false };
            MacNotificationPermission permission = Create(world);
            permission.Answered(false);
            int sent = 0;
            permission.Send(() => sent++);
            world.Pump();
            Check(sent == 0 && permission.Denied, "a still-denied app must not send.");
            world.Allowed = true;
            permission.Send(() => sent++);
            world.Pump();
            Check(sent == 1, "after allowing in System Settings the notification was still dropped until restart.");
            Check(!permission.Denied, "the permission still reads as denied after it was allowed.");
            permission.Send(() => sent++);
            Check(sent == 2, "once allowed, later notifications must send at once.");
            scenarios++;
        }

        // The permission was never decided: nothing is sent and it does not count as denied.
        {
            var world = new World { Allowed = false, Determined = false };
            MacNotificationPermission permission = Create(world);
            int sent = 0;
            permission.Send(() => sent++);
            world.Pump();
            Check(sent == 0 && !permission.Denied, "an undecided permission must neither send nor read as denied.");
            scenarios++;
        }

        // Switched off after it was on: the app finds out when it is activated again, and says so once.
        {
            var world = new World { Allowed = true };
            MacNotificationPermission permission = Create(world);
            permission.Answered(true);
            world.Allowed = false;
            permission.Refresh();
            world.Pump();
            Check(permission.Denied && world.Notices == 1, "a permission switched off must read as denied and say so once.");
            permission.Refresh();
            world.Pump();
            Check(world.Notices == 1, "the 'switched off' notice must not repeat on every activation.");
            int sent = 0;
            permission.Send(() => sent++);
            world.Pump();
            Check(sent == 0, "a notification must not be sent after the permission was switched off.");
            world.Allowed = true;
            permission.Refresh();
            world.Pump();
            Check(!permission.Denied, "switching it on again must clear the denied state on the next activation.");
            scenarios++;
        }

        // Several notifications while denied, then the read answers late: each is judged by the answer to its own read.
        {
            var world = new World { Allowed = false };
            MacNotificationPermission permission = Create(world);
            permission.Answered(false);
            var order = new List<string>();
            permission.Send(() => order.Add("first"));      // read while still denied
            world.Allowed = true;
            permission.Send(() => order.Add("second"));     // read after it was allowed
            world.Pump();
            Check(order.SequenceEqual(["second"]), "notifications must follow the permission at the time of their own read: " + string.Join(",", order));
            scenarios++;
        }

        // Stopped: a late answer sends nothing and says nothing.
        {
            var world = new World { Allowed = false };
            MacNotificationPermission permission = Create(world);
            permission.Answered(false);
            int sent = 0;
            world.Allowed = true;
            permission.Send(() => sent++);
            permission.Stop();
            world.Pump();
            Check(sent == 0 && world.Notices == 0, "a notification must not go out after the app stopped.");
            scenarios++;
        }

        return $"notification permission ({scenarios} scenarios: allowed at start, allowed later in System Settings, undecided, switched off later with one notice, per-read judging, stop)";
    }

    private static MacNotificationPermission Create(World world) => new(world.Read, world.OnMain, () => world.Notices++);
}
