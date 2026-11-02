using System;
using System.IO;
using Microsoft.Win32;
using Xunit;

namespace VMGuard.Tests
{
    /// <summary>
    /// Tests that need real Windows APIs. They only run on Windows (CI uses windows-latest)
    /// and pass trivially elsewhere.
    /// </summary>
    public class WindowsIntegrationTests
    {
        private static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

        [Fact]
        public void MissingLegacyTaskOperationsAreNoOps()
        {
            if (!IsWindows) return;
            string name = "VMGuard-Test-" + Guid.NewGuid().ToString("N");
            Assert.False(ScheduledTask.Exists(name));
            ScheduledTask.Stop(name);
            ScheduledTask.Delete(name);
        }

        [Fact]
        public void WritesAndRemovesRegistryEntries()
        {
            if (!IsWindows) return;

            string root = @"Software\VMGuard-Test-" + Guid.NewGuid().ToString("N");
            string runKey = root + @"\Run";
            string uninstallKey = root + @"\Uninstall\VMGuard";
            string exe = typeof(Program).Assembly.Location;
            try
            {
                Registration.SetStartup(@"C:\Apps\VM Guard\VMGuard.exe", "--interval 30", runKey);
                Assert.Equal(@"""C:\Apps\VM Guard\VMGuard.exe"" --interval 30", Registration.GetStartup(runKey));

                Registration.WriteUninstallEntry(exe, Path.GetDirectoryName(exe), "1.2.3", uninstallKey);
                Assert.True(Registration.HasUninstallEntry(uninstallKey));
                using (var k = Registry.CurrentUser.OpenSubKey(uninstallKey))
                {
                    Assert.Equal("VMGuard", k.GetValue("DisplayName"));
                    Assert.Equal("1.2.3", k.GetValue("DisplayVersion"));
                    Assert.Equal(Options.Quote(exe) + " --uninstall", k.GetValue("UninstallString"));
                }

                Registration.RemoveStartup(runKey);
                Registration.RemoveUninstallEntry(uninstallKey);
                Assert.Null(Registration.GetStartup(runKey));
                Assert.False(Registration.HasUninstallEntry(uninstallKey));

                // Removing again is a no-op
                Registration.RemoveStartup(runKey);
                Registration.RemoveUninstallEntry(uninstallKey);
            }
            finally
            {
                Registry.CurrentUser.DeleteSubKeyTree(root, throwOnMissingSubKey: false);
            }
        }

        [Fact]
        public void RequestExitWithNoInstanceReturnsImmediately()
        {
            if (!IsWindows) return;
            Assert.True(SingleInstance.RequestExitAndWait(TimeSpan.FromSeconds(1)));
        }
    }
}
