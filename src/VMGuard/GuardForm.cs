using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace VMGuard
{
    /// <summary>
    /// Invisible window that receives the Windows shutdown notification (WM_QUERYENDSESSION)
    /// and checks the battery on a timer. Optionally shows a system tray icon.
    ///
    /// The window is "shown" with opacity 0 instead of being hidden: Windows only
    /// honors ShutdownBlockReasonCreate for visible top-level windows.
    /// </summary>
    internal sealed class GuardForm : Form
    {
        /// <summary>How long to wait, at shutdown, for a battery suspend already in progress.</summary>
        private static readonly TimeSpan ShutdownWaitForBackground = TimeSpan.FromSeconds(330);

        private readonly Options _opt;
        private readonly Logger _log;
        private readonly VmSuspender _suspender;
        private readonly Timer _timer;
        private readonly NotifyIcon _tray;

        public GuardForm(Options opt, Logger log, VmSuspender suspender)
        {
            _opt = opt;
            _log = log;
            _suspender = suspender;

            Text = "VMGuard";
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            Size = new Size(1, 1);
            Opacity = 0;

            _timer = new Timer { Interval = opt.IntervalSeconds * 1000 };
            _timer.Tick += (s, e) => CheckBattery(false);

            if (!opt.NoTrayIcon) _tray = CreateTrayIcon();
            _suspender.SuspendStarting += OnSuspendStarting;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _timer.Start();
            if (_opt.DryRun) CheckBattery(true);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_QUERYENDSESSION)
            {
                // Hold the shutdown until the VMs are suspended
                NativeMethods.ShutdownBlockReasonCreate(Handle, "Suspending VMware virtual machines...");
                try { SuspendForShutdown(); }
                catch (Exception ex) { _log.Write("ERROR: " + ex.Message); }
                NativeMethods.ShutdownBlockReasonDestroy(Handle);
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }

        private void SuspendForShutdown()
        {
            _timer.Stop();
            // If a battery suspend is running, wait for it to finish instead
            // of starting another one on the same VMs.
            if (_suspender.IsBusy)
            {
                _log.Write("Shutdown/sign-out during a suspend in progress -> waiting");
                _suspender.WaitBackground(ShutdownWaitForBackground);
            }
            // Final pass in case a VM is still running (or nothing was in progress)
            _suspender.SuspendAll("Shutdown/sign-out");
            // If the shutdown does not happen after all (someone cancelled it), keep watching
            _timer.Start();
        }

        private void CheckBattery(bool report)
        {
            try
            {
                if (_suspender.IsBusy) return;

                PowerStatus ps = SystemInformation.PowerStatus;
                int pct = (int)Math.Round(ps.BatteryLifePercent * 100);
                bool hasBattery = (ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) == 0
                                  && ps.BatteryChargeStatus != BatteryChargeStatus.Unknown;
                UpdateTrayText(ps, pct, hasBattery);

                if (ps.PowerLineStatus == PowerLineStatus.Offline && hasBattery && pct <= _opt.BatteryThreshold)
                {
                    string reason = "Low battery (" + pct + "%, unplugged, threshold " + _opt.BatteryThreshold + "%)";
                    // In dry-run mode VMs are only listed (fast): run it right here
                    if (_opt.DryRun) _suspender.SuspendAll(reason);
                    else _suspender.StartBackground(reason);
                }
                else if (report)
                {
                    _log.Write("Battery " + (hasBattery ? pct + "%" : "not detected") + ", power " + PowerLineText(ps.PowerLineStatus) +
                               ", threshold " + _opt.BatteryThreshold + "% -> would not suspend for battery");
                    var vms = _suspender.GetRunningVMs();
                    _log.Write("Running VMs now: " + vms.Count);
                    foreach (string vmx in vms) _log.Write("  " + vmx);
                }
            }
            catch (Exception ex)
            {
                _log.Write("ERROR: " + ex.Message);
            }
        }

        private static string PowerLineText(PowerLineStatus s)
        {
            switch (s)
            {
                case PowerLineStatus.Online: return "plugged in";
                case PowerLineStatus.Offline: return "unplugged";
                default: return "unknown";
            }
        }

        // --- System tray icon ---------------------------------------------

        private NotifyIcon CreateTrayIcon()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Suspend VMs now", null, (s, e) =>
            {
                if (_opt.DryRun) _suspender.SuspendAll("Manual (tray menu)");
                else if (!_suspender.StartBackground("Manual (tray menu)"))
                    _tray.ShowBalloonTip(3000, "VMGuard", "A suspend is already in progress.", ToolTipIcon.Info);
            });
            menu.Items.Add("Open log", null, (s, e) => OpenLog());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit VMGuard", null, (s, e) =>
            {
                var r = MessageBox.Show(
                    "If you exit, VMs will not be suspended on shutdown or low battery until you sign in again.\n\nExit VMGuard?",
                    "VMGuard", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (r == DialogResult.Yes)
                {
                    _log.Write("VMGuard closed from the tray icon");
                    Close();
                }
            });

            var icon = new NotifyIcon
            {
                Icon = SystemIcons.Shield,
                Text = "VMGuard",
                ContextMenuStrip = menu,
                Visible = true,
            };
            icon.DoubleClick += (s, e) => OpenLog();
            return icon;
        }

        private void UpdateTrayText(PowerStatus ps, int pct, bool hasBattery)
        {
            if (_tray == null) return;
            string text = "VMGuard" + (_opt.DryRun ? " [DRY RUN]" : "") + "\n" +
                          (hasBattery ? "Battery " + pct + "% " + PowerLineText(ps.PowerLineStatus) : "No battery") +
                          "\nThreshold " + _opt.BatteryThreshold + "%";
            // NotifyIcon.Text allows at most 63 characters
            _tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
        }

        private void OnSuspendStarting(string reason, int count)
        {
            if (_tray == null) return;
            try
            {
                BeginInvoke((Action)(() =>
                    _tray.ShowBalloonTip(5000, "VMGuard", reason + ": suspending " + count + " VM(s)", ToolTipIcon.Warning)));
            }
            catch
            {
                // The window may be closing
            }
        }

        private void OpenLog()
        {
            try
            {
                if (!File.Exists(_log.Path)) File.AppendAllText(_log.Path, "");
                Process.Start(new ProcessStartInfo("notepad.exe", Options.Quote(_log.Path)) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _log.Write("ERROR opening the log: " + ex.Message);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                if (_tray != null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
