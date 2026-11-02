using System;
using System.Globalization;
using System.IO;
using Microsoft.Win32;

namespace VMGuard
{
    /// <summary>
    /// Per-user registry entries (HKEY_CURRENT_USER, no admin needed):
    /// the Run value that starts VMGuard at sign-in and the Installed apps entry.
    /// The key paths can be overridden for tests.
    /// </summary>
    internal static class Registration
    {
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\VMGuard";
        public const string ValueName = "VMGuard";
        public const string Publisher = "VMGuard";
        public const string HelpUrl = "https://github.com/jcostasuarez/VMGuard";

        /// <summary>Command line stored in the Run value.</summary>
        internal static string BuildCommand(string exe, string arguments) =>
            Options.Quote(exe) + (string.IsNullOrEmpty(arguments) ? "" : " " + arguments);

        public static void SetStartup(string exe, string arguments, string runKey = RunKey)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(runKey))
                k.SetValue(ValueName, BuildCommand(exe, arguments), RegistryValueKind.String);
        }

        public static string GetStartup(string runKey = RunKey)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(runKey))
                return k?.GetValue(ValueName) as string;
        }

        public static void RemoveStartup(string runKey = RunKey)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(runKey, writable: true))
                k?.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        public static void WriteUninstallEntry(string exe, string installDir, string version, string uninstallKey = UninstallKey)
        {
            long sizeKb = 0;
            try { sizeKb = Math.Max(1, new FileInfo(exe).Length / 1024); } catch { }

            using (var k = Registry.CurrentUser.CreateSubKey(uninstallKey))
            {
                k.SetValue("DisplayName", "VMGuard", RegistryValueKind.String);
                k.SetValue("DisplayVersion", version, RegistryValueKind.String);
                k.SetValue("Publisher", Publisher, RegistryValueKind.String);
                k.SetValue("DisplayIcon", exe + ",0", RegistryValueKind.String);
                k.SetValue("InstallLocation", installDir, RegistryValueKind.String);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture), RegistryValueKind.String);
                k.SetValue("UninstallString", BuildCommand(exe, "--uninstall"), RegistryValueKind.String);
                k.SetValue("QuietUninstallString", BuildCommand(exe, "--uninstall --quiet"), RegistryValueKind.String);
                k.SetValue("URLInfoAbout", HelpUrl, RegistryValueKind.String);
                k.SetValue("HelpLink", HelpUrl, RegistryValueKind.String);
                k.SetValue("Comments", "Suspends VMware Workstation VMs on shutdown or low battery", RegistryValueKind.String);
                k.SetValue("EstimatedSize", (int)sizeKb, RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        public static bool HasUninstallEntry(string uninstallKey = UninstallKey)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(uninstallKey))
                return k != null;
        }

        public static void RemoveUninstallEntry(string uninstallKey = UninstallKey)
        {
            Registry.CurrentUser.DeleteSubKeyTree(uninstallKey, throwOnMissingSubKey: false);
        }
    }
}
