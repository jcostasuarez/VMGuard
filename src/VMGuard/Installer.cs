using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Principal;
using System.Text;
using System.Xml;
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

            // Stop the previous version (exe or PowerShell script) before replacing it
            RunSchtasks("/End /TN " + TaskName, ignoreErrors: true);
            StopRunningInstances();

            if (!SamePath(self, InstalledExe))
                CopyWithRetry(self, InstalledExe);

            string user = WindowsIdentity.GetCurrent().Name;
            string xmlPath = Path.Combine(Path.GetTempPath(), "VMGuard-task-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                using (var w = XmlWriter.Create(xmlPath, new XmlWriterSettings { Encoding = Encoding.Unicode, Indent = true }))
                    BuildTaskXml(user, InstalledExe, opt.ToRunArguments(), InstallDir).Save(w);
                RunSchtasks("/Create /F /TN " + TaskName + " /XML " + Options.Quote(xmlPath), ignoreErrors: false);
            }
            finally
            {
                try { File.Delete(xmlPath); } catch { }
            }
            RunSchtasks("/Run /TN " + TaskName, ignoreErrors: false);

            return "VMGuard is installed and running.\n\n" +
                   "Battery threshold: " + opt.BatteryThreshold + "%\n" +
                   "Program: " + InstalledExe + "\n" +
                   "Log: " + Path.Combine(InstallDir, "VMGuard.log");
        }

        /// <summary>Uninstalls: deletes the task and stops VMGuard. The folder and the log are kept.</summary>
        public static string Uninstall()
        {
            RunSchtasks("/End /TN " + TaskName, ignoreErrors: true);
            RunSchtasks("/Delete /F /TN " + TaskName, ignoreErrors: true);
            StopRunningInstances();
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

        /// <summary>
        /// Stops other VMGuard.exe instances in this session, and the VMGuard.ps1 from the
        /// previous PowerShell version if it is still running.
        /// </summary>
        private static void StopRunningInstances()
        {
            int self = Process.GetCurrentProcess().Id;
            int session = Process.GetCurrentProcess().SessionId;
            var toWait = new List<Process>();

            foreach (Process p in Process.GetProcessesByName("VMGuard"))
            {
                if (p.Id == self || p.SessionId != session) { p.Dispose(); continue; }
                try { p.Kill(); toWait.Add(p); } catch { p.Dispose(); }
            }

            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'powershell.exe' OR Name = 'pwsh.exe'"))
                {
                    foreach (ManagementObject mo in searcher.Get().Cast<ManagementObject>())
                    {
                        using (mo)
                        {
                            string cmd = mo["CommandLine"] as string;
                            if (cmd == null || cmd.IndexOf(@"\VMGuard\VMGuard.ps1", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            try
                            {
                                var p = Process.GetProcessById(Convert.ToInt32(mo["ProcessId"]));
                                if (p.SessionId != session) { p.Dispose(); continue; }
                                p.Kill();
                                toWait.Add(p);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch
            {
                // WMI unavailable: not critical
            }

            foreach (Process p in toWait)
            {
                try { p.WaitForExit(10000); } catch { }
                p.Dispose();
            }
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

        private static void RunSchtasks(string arguments, bool ignoreErrors)
        {
            var psi = new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using (var p = Process.Start(psi))
            {
                var err = p.StandardError.ReadToEndAsync();
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0 && !ignoreErrors)
                    throw new InvalidOperationException("schtasks " + arguments.Split(' ')[0] + " failed (" + p.ExitCode + "): " +
                                                        (err.Result + " " + output).Trim());
            }
        }
    }
}
