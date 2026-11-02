using System;
using System.Diagnostics;
using System.IO;

namespace VMGuard
{
    /// <summary>
    /// Per-user install, no administrator rights needed:
    ///  - copies VMGuard.exe to %USERPROFILE%\VMGuard
    ///  - starts it at sign-in through the user's Run key (listed in Task Manager > Startup apps)
    ///  - registers it in Settings > Apps > Installed apps, with an uninstall command
    /// </summary>
    internal static class Installer
    {
        /// <summary>Scheduled task used by earlier versions; removed on install.</summary>
        public const string LegacyTaskName = "VMGuard";

        public static string InstallDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "VMGuard");

        public static string InstalledExe => Path.Combine(InstallDir, "VMGuard.exe");

        /// <summary>Installs or reinstalls. Returns a message to show the user.</summary>
        public static string Install(Options opt)
        {
            string self = Process.GetCurrentProcess().MainModule.FileName;
            Directory.CreateDirectory(InstallDir);

            // Stop the previous version before replacing it: the scheduled task used by
            // earlier versions (also covers the old PowerShell script), then any running copy.
            ScheduledTask.Stop(LegacyTaskName);
            ScheduledTask.Delete(LegacyTaskName);
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

            string args = opt.ToRunArguments();
            Registration.SetStartup(InstalledExe, args);
            Registration.WriteUninstallEntry(InstalledExe, InstallDir, Program.Version);

            string msg = "VMGuard is installed.\n\n" +
                         "Battery threshold: " + threshold + "% (you can change it from the tray icon)\n" +
                         "Program: " + InstalledExe + "\n" +
                         "Log: " + Path.Combine(InstallDir, "VMGuard.log") + "\n\n" +
                         "It starts every time you sign in (Task Manager > Startup apps) and can be " +
                         "uninstalled from Settings > Apps > Installed apps.";
            if (stopped)
            {
                Process.Start(new ProcessStartInfo(InstalledExe, args) { UseShellExecute = false, WorkingDirectory = InstallDir });
                return msg;
            }
            return msg + "\n\nAnother VMGuard is still running, so the new version will start the next time you sign in.";
        }

        /// <summary>Uninstalls: removes the startup and Installed apps entries and stops VMGuard. The folder and the log are kept.</summary>
        public static string Uninstall()
        {
            Registration.RemoveStartup();
            Registration.RemoveUninstallEntry();
            ScheduledTask.Stop(LegacyTaskName);
            ScheduledTask.Delete(LegacyTaskName);
            SingleInstance.RequestExitAndWait(TimeSpan.FromSeconds(15));
            return "VMGuard is uninstalled.\n\nThe folder " + InstallDir + " (program, settings and log) is not deleted; you can remove it by hand.";
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
