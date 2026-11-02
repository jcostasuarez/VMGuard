using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace VMGuard
{
    /// <summary>
    /// VMGuard: suspends every VMware Workstation VM when
    ///  - Windows shuts down / restarts / signs out (handles WM_QUERYENDSESSION)
    ///  - the battery drops to the threshold while unplugged
    /// Runs hidden in the user's session (scheduled task at sign-in).
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Options opt;
            try
            {
                opt = Options.Parse(args);
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(ex.Message + "\n\n" + Options.Usage, "VMGuard", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }

            switch (opt.Command)
            {
                case Command.Help:
                    MessageBox.Show(Options.Usage, "VMGuard", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                case Command.Install:
                    return RunInstaller(() => Installer.Install(opt), "install", opt.Quiet);
                case Command.Uninstall:
                    return RunInstaller(Installer.Uninstall, "uninstall", opt.Quiet);
                default:
                    return RunGuard(opt);
            }
        }

        private static int RunInstaller(Func<string> action, string verb, bool quiet)
        {
            try
            {
                string msg = action();
                if (!quiet) MessageBox.Show(msg, "VMGuard", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not " + verb + " VMGuard:\n\n" + ex.Message, "VMGuard", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static int RunGuard(Options opt)
        {
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            var log = new Logger(Path.Combine(exeDir, "VMGuard.log"), opt.DryRun);

            // Threshold: command line, else the saved setting (changed from the tray), else the default
            var settings = Settings.Load(exeDir);
            if (!opt.ThresholdGiven && settings.BatteryThreshold.HasValue)
                opt.BatteryThreshold = settings.BatteryThreshold.Value;

            if (opt.DryRun)
            {
                // Dry run: own console to show the messages. Closing it ends VMGuard.
                if (NativeMethods.AllocConsole())
                {
                    Console.Title = "VMGuard - dry run";
                    log.Echo = Console.WriteLine;
                }
            }

            Application.ThreadException += (s, e) => log.Write("ERROR: " + e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => log.Write("ERROR fatal: " + e.ExceptionObject);

            // Single instance (dry run uses another name so it can run
            // next to the installed copy without clashing with it)
            using (var mutex = new Mutex(false, opt.DryRun ? SingleInstance.DryRunMutexName : SingleInstance.MutexName))
            {
                bool owned;
                try { owned = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { owned = true; }
                if (!owned)
                {
                    if (opt.DryRun) log.Write("Another dry-run instance is already running. Exiting.");
                    return 0;
                }

                string vmrun = VmrunLocator.Find(opt.VmrunPath);
                if (vmrun == null)
                {
                    log.Write(string.IsNullOrEmpty(opt.VmrunPath)
                        ? "ERROR: vmrun.exe not found. Install VMware Workstation or pass its path with --vmrun."
                        : "ERROR: vmrun.exe does not exist at the given path: " + opt.VmrunPath);
                    if (opt.DryRun) WaitForKey();
                    return 1;
                }

                var suspender = new VmSuspender(vmrun, log, opt.DryRun);
                if (opt.DryRun)
                    log.Write("VMGuard " + Version + " started in DRY-RUN MODE (battery threshold " + opt.BatteryThreshold +
                              "%, interval " + opt.IntervalSeconds + " s, vmrun: " + vmrun +
                              "). No VM will be suspended. Close this window to exit.");
                else
                    log.Write("VMGuard " + Version + " started (battery threshold " + opt.BatteryThreshold +
                              "%, interval " + opt.IntervalSeconds + " s, vmrun: " + vmrun + ")");

                using (var form = new GuardForm(opt, log, suspender, settings))
                using (var exitEvent = opt.DryRun ? null : SingleInstance.CreateExitEvent())
                {
                    // The installer signals this event to replace or remove this copy cleanly
                    RegisteredWaitHandle wait = null;
                    if (exitEvent != null)
                    {
                        wait = ThreadPool.RegisterWaitForSingleObject(exitEvent, (state, timedOut) =>
                        {
                            log.Write("Exit requested by the installer");
                            try { form.BeginInvoke((Action)form.Close); } catch { }
                        }, null, Timeout.Infinite, executeOnlyOnce: true);
                    }
                    Application.Run(form);
                    wait?.Unregister(null);
                }
                mutex.ReleaseMutex();
            }
            return 0;
        }

        internal static string Version =>
            typeof(Program).Assembly.GetName().Version.ToString(3);

        private static void WaitForKey()
        {
            try
            {
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey(true);
            }
            catch { }
        }
    }
}
