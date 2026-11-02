using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Xml.Linq;

namespace VMGuard
{
    /// <summary>
    /// Installs VMGuard to %USERPROFILE%\VMGuard and creates the "VMGuard" scheduled task
    /// that starts it at sign-in. Does not require administrator rights.
    /// </summary>
    internal static class Installer
    {
        public const string TaskName = "VMGuard";
        private const string TaskNs = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        public static string InstallDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "VMGuard");

        public static string InstalledExe => Path.Combine(InstallDir, "VMGuard.exe");

        /// <summary>Installs or reinstalls. Returns a message to show the user.</summary>
        public static string Install(Options opt)
        {
            string self = Process.GetCurrentProcess().MainModule.FileName;
            Directory.CreateDirectory(InstallDir);

            // Stop the previous version before replacing it: first the scheduled task
            // (this also covers the old PowerShell version), then any copy started by hand.
            ScheduledTask.Stop(TaskName);
            bool stopped = SingleInstance.RequestExitAndWait(TimeSpan.FromSeconds(15));

            if (!SamePath(self, InstalledExe))
                CopyWithRetry(self, InstalledExe);

            // The threshold is kept in the settings file; reinstalling without --threshold keeps it
            var settings = Settings.Load(InstallDir);
            if (opt.ThresholdGiven || settings.BatteryThreshold == null)
            {
                settings.BatteryThreshold = opt.BatteryThreshold;
                settings.Save();
            }
            int threshold = settings.BatteryThreshold.Value;

            string user = WindowsIdentity.GetCurrent().Name;
            string xml = BuildTaskXml(user, InstalledExe, opt.ToRunArguments(), InstallDir).ToString();
            ScheduledTask.Register(TaskName, xml);

            string msg = "VMGuard is installed.\n\n" +
                         "Battery threshold: " + threshold + "% (you can change it from the tray icon)\n" +
                         "Program: " + InstalledExe + "\n" +
                         "Log: " + Path.Combine(InstallDir, "VMGuard.log");
            if (stopped)
            {
                ScheduledTask.Run(TaskName);
                return msg + "\n\nIt is running now and will start every time you sign in.";
            }
            return msg + "\n\nAnother VMGuard is still running, so the new version will start the next time you sign in.";
        }

        /// <summary>Uninstalls: deletes the task and stops VMGuard. The folder and the log are kept.</summary>
        public static string Uninstall()
        {
            ScheduledTask.Stop(TaskName);
            ScheduledTask.Delete(TaskName);
            SingleInstance.RequestExitAndWait(TimeSpan.FromSeconds(15));
            return "VMGuard is uninstalled.\n\nThe folder " + InstallDir + " (program and log) is not deleted; you can remove it by hand.";
        }

        /// <summary>Scheduled task definition (Task Scheduler XML format).</summary>
        internal static XDocument BuildTaskXml(string user, string exe, string arguments, string workingDir)
        {
            XNamespace ns = TaskNs;
            return new XDocument(
                new XElement(ns + "Task", new XAttribute("version", "1.2"),
                    new XElement(ns + "RegistrationInfo",
                        new XElement(ns + "Description", "Suspends VMware VMs on shutdown or low battery")),
                    new XElement(ns + "Triggers",
                        new XElement(ns + "LogonTrigger",
                            new XElement(ns + "Enabled", "true"),
                            new XElement(ns + "UserId", user))),
                    new XElement(ns + "Principals",
                        new XElement(ns + "Principal", new XAttribute("id", "Author"),
                            new XElement(ns + "UserId", user),
                            new XElement(ns + "LogonType", "InteractiveToken"),
                            new XElement(ns + "RunLevel", "LeastPrivilege"))),
                    new XElement(ns + "Settings",
                        new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                        new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                        new XElement(ns + "StopIfGoingOnBatteries", "false"),
                        new XElement(ns + "AllowHardTerminate", "true"),
                        new XElement(ns + "StartWhenAvailable", "false"),
                        new XElement(ns + "AllowStartOnDemand", "true"),
                        new XElement(ns + "Enabled", "true"),
                        new XElement(ns + "Hidden", "false"),
                        new XElement(ns + "ExecutionTimeLimit", "PT0S"),
                        new XElement(ns + "Priority", "7")),
                    new XElement(ns + "Actions", new XAttribute("Context", "Author"),
                        new XElement(ns + "Exec",
                            new XElement(ns + "Command", exe),
                            new XElement(ns + "Arguments", arguments),
                            new XElement(ns + "WorkingDirectory", workingDir)))));
        }

        private static void CopyWithRetry(string from, string to)
        {
            for (int i = 0; ; i++)
            {
                try
                {
                    File.Copy(from, to, overwrite: true);
                    return;
                }
                catch (IOException) when (i < 10)
                {
                    // The previous process may take a moment to release the file
                    System.Threading.Thread.Sleep(500);
                }
            }
        }

        private static bool SamePath(string a, string b) =>
            string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }
}
