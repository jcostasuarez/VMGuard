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
                    return RunInstaller(() => Installer.Install(opt), "install");
                case Command.Uninstall:
                    return RunInstaller(Installer.Uninstall, "uninstall");
                default:
                    return RunGuard(opt);
            }
        }

        private static int RunInstaller(Func<string> action, string verb)
        {
            try
            {
                string msg = action();
                MessageBox.Show(msg, "VMGuard", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            using (var mutex = new Mutex(false, opt.DryRun ? @"Local\VMGuard-DryRun" : @"Local\VMGuard"))
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

                // Get the shutdown notification before most applications
                NativeMethods.SetProcessShutdownParameters(0x3FF, 0);

                var suspender = new VmSuspender(vmrun, log, opt.DryRun);
                if (opt.DryRun)
                    log.Write("VMGuard " + Version + " started in DRY-RUN MODE (battery threshold " + opt.BatteryThreshold +
                              "%, interval " + opt.IntervalSeconds + " s, vmrun: " + vmrun +
                              "). No VM will be suspended. Close this window to exit.");
                else
                    log.Write("VMGuard " + Version + " started (battery threshold " + opt.BatteryThreshold +
                              "%, interval " + opt.IntervalSeconds + " s, vmrun: " + vmrun + ")");

                using (var form = new GuardForm(opt, log, suspender))
                {
                    Application.Run(form);
                }
                mutex.ReleaseMutex();
            }
            return 0;
        }

        private static string Version =>
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
