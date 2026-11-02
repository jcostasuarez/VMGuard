using System;
using System.IO;
using System.Security.Principal;
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
        public void RegistersFindsAndDeletesScheduledTask()
        {
            if (!IsWindows) return;

            string name = "VMGuard-Test-" + Guid.NewGuid().ToString("N");
            string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            string xml = Installer.BuildTaskXml(WindowsIdentity.GetCurrent().Name, cmd, "/c exit 0", Path.GetTempPath()).ToString();
            try
            {
                ScheduledTask.Register(name, xml);
                Assert.True(ScheduledTask.Exists(name));

                // Registering again replaces it
                ScheduledTask.Register(name, xml);
                ScheduledTask.Stop(name);
            }
            finally
            {
                ScheduledTask.Delete(name);
            }
            Assert.False(ScheduledTask.Exists(name));

            // Operations on a missing task are no-ops
            ScheduledTask.Stop(name);
            ScheduledTask.Run(name);
            ScheduledTask.Delete(name);
        }

        [Fact]
        public void RequestExitWithNoInstanceReturnsImmediately()
        {
            if (!IsWindows) return;
            Assert.True(SingleInstance.RequestExitAndWait(TimeSpan.FromSeconds(1)));
        }
    }
}
