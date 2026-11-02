using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VMGuard
{
    /// <summary>Lists and suspends running VMs using vmrun.</summary>
    internal sealed class VmSuspender
    {
        /// <summary>Maximum time to wait for the suspends to finish.</summary>
        public static readonly TimeSpan SuspendTimeout = TimeSpan.FromMinutes(5);

        private readonly string _vmrun;
        private readonly Logger _log;
        private readonly bool _dryRun;
        private readonly object _bgLock = new object();
        private Task _background;

        public VmSuspender(string vmrun, Logger log, bool dryRun)
        {
            _vmrun = vmrun;
            _log = log;
            _dryRun = dryRun;
        }

        /// <summary>Raised (on another thread) when a real suspend starts: reason and number of VMs.</summary>
        public event Action<string, int> SuspendStarting;

        /// <summary>A background suspend is in progress.</summary>
        public bool IsBusy
        {
            get { lock (_bgLock) return _background != null && !_background.IsCompleted; }
        }

        /// <summary>Running VMs according to "vmrun -T ws list".</summary>
        public List<string> GetRunningVMs()
        {
            var psi = NewStartInfo("-T ws list");
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (var p = Process.Start(psi))
            {
                // Read stderr in parallel so the process does not block if it fills up
                var err = p.StandardError.ReadToEndAsync();
                string output = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(60000))
                {
                    try { p.Kill(); } catch { }
                    throw new TimeoutException("vmrun list did not respond within 60 s");
                }
                if (p.ExitCode != 0)
                {
                    string msg = (output + " " + err.Result).Trim();
                    throw new InvalidOperationException("vmrun list returned " + p.ExitCode + ": " + msg);
                }
                return ParseList(output);
            }
        }

        /// <summary>Extracts the .vmx paths from the output of "vmrun list".</summary>
        public static List<string> ParseList(string output)
        {
            return (output ?? "")
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.EndsWith(".vmx", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>Suspends all VMs in parallel and waits (blocking).</summary>
        public void SuspendAll(string reason)
        {
            List<string> vms = GetRunningVMs();
            if (vms.Count == 0)
            {
                if (_dryRun) _log.Write(reason + " -> no running VMs, nothing would be suspended");
                return;
            }

            if (_dryRun)
            {
                _log.Write(reason + " -> would suspend " + vms.Count + " VM(s)");
                foreach (string vmx in vms) _log.Write("  " + vmx + " -> (not suspended, dry run)");
                return;
            }

            _log.Write(reason + " -> suspending " + vms.Count + " VM(s)");
            try { SuspendStarting?.Invoke(reason, vms.Count); } catch { }

            // All in parallel so it takes as little time as possible
            var procs = new List<Process>();
            foreach (string vmx in vms)
            {
                try
                {
                    procs.Add(Process.Start(NewStartInfo("-T ws suspend " + Options.Quote(vmx))));
                }
                catch (Exception ex)
                {
                    procs.Add(null);
                    _log.Write("  " + vmx + " -> ERROR starting vmrun: " + ex.Message);
                }
            }

            DateTime deadline = DateTime.UtcNow + SuspendTimeout;
            for (int i = 0; i < vms.Count; i++)
            {
                Process p = procs[i];
                if (p == null) continue;
                using (p)
                {
                    int remaining = (int)Math.Max(0, (deadline - DateTime.UtcNow).TotalMilliseconds);
                    if (p.WaitForExit(remaining))
                        _log.Write("  " + vms[i] + " -> exit " + p.ExitCode);
                    else
                        _log.Write("  " + vms[i] + " -> no response after " + SuspendTimeout.TotalMinutes + " min");
                }
            }
        }

        /// <summary>
        /// Runs SuspendAll on another thread so the window keeps handling Windows
        /// messages (for example, the shutdown notification).
        /// Returns false if one was already in progress.
        /// </summary>
        public bool StartBackground(string reason)
        {
            lock (_bgLock)
            {
                if (_background != null && !_background.IsCompleted) return false;
                _background = Task.Factory.StartNew(() =>
                {
                    try { SuspendAll(reason); }
                    catch (Exception ex) { _log.Write("ERROR (background): " + ex.Message); }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                return true;
            }
        }

        /// <summary>Waits for the background suspend to finish, if there is one.</summary>
        public void WaitBackground(TimeSpan timeout)
        {
            Task t;
            lock (_bgLock) t = _background;
            if (t == null) return;
            try { t.Wait(timeout); } catch { }
        }

        private ProcessStartInfo NewStartInfo(string arguments)
        {
            return new ProcessStartInfo(_vmrun, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
        }
    }
}
