using System;
using System.Threading;

namespace VMGuard
{
    /// <summary>
    /// Single-instance support: a named mutex marks the running copy, and a named
    /// event lets the installer ask it to exit cleanly instead of killing it.
    /// </summary>
    internal static class SingleInstance
    {
        public const string MutexName = @"Local\VMGuard";
        public const string DryRunMutexName = @"Local\VMGuard-DryRun";
        public const string ExitEventName = @"Local\VMGuard-Exit";

        /// <summary>
        /// Asks the running VMGuard (if any) to exit and waits until it has.
        /// Returns true if no VMGuard is running anymore.
        /// </summary>
        public static bool RequestExitAndWait(TimeSpan timeout)
        {
            if (EventWaitHandle.TryOpenExisting(ExitEventName, out EventWaitHandle ev))
            {
                using (ev) ev.Set();
            }

            using (var m = new Mutex(false, MutexName))
            {
                bool owned;
                try { owned = m.WaitOne(timeout); }
                catch (AbandonedMutexException) { owned = true; }
                if (owned) m.ReleaseMutex();
                return owned;
            }
        }

        /// <summary>Creates the event that the installer signals to ask this instance to exit.</summary>
        public static EventWaitHandle CreateExitEvent() =>
            new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
    }
}
