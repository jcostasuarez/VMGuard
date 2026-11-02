using System;
using Xunit;

namespace VMGuard.Tests
{
    public class OptionsTests
    {
        [Fact]
        public void Defaults()
        {
            var o = Options.Parse(new string[0]);
            Assert.Equal(Command.Run, o.Command);
            Assert.Equal(10, o.BatteryThreshold);
            Assert.Equal(30, o.IntervalSeconds);
            Assert.Equal("", o.VmrunPath);
            Assert.False(o.DryRun);
            Assert.False(o.NoTrayIcon);
        }

        [Fact]
        public void ParsesAllOptions()
        {
            var o = Options.Parse(new[] { "--install", "--threshold", "15", "--interval=5", "--vmrun", @"C:\VMware\vmrun.exe", "--dry-run", "--no-tray" });
            Assert.Equal(Command.Install, o.Command);
            Assert.Equal(15, o.BatteryThreshold);
            Assert.Equal(5, o.IntervalSeconds);
            Assert.Equal(@"C:\VMware\vmrun.exe", o.VmrunPath);
            Assert.True(o.DryRun);
            Assert.True(o.NoTrayIcon);
        }

        [Fact]
        public void AcceptsLegacySpanishNames()
        {
            var o = Options.Parse(new[] { "--instalar", "--umbral", "15", "--intervalo", "5", "--dryrun", "--sin-icono" });
            Assert.Equal(Command.Install, o.Command);
            Assert.Equal(15, o.BatteryThreshold);
            Assert.Equal(5, o.IntervalSeconds);
            Assert.True(o.DryRun);
            Assert.True(o.NoTrayIcon);
            Assert.Equal(Command.Uninstall, Options.Parse(new[] { "--desinstalar" }).Command);
        }

        [Fact]
        public void AcceptsPowerShellStyleNames()
        {
            var o = Options.Parse(new[] { "-UmbralBateria", "20", "-IntervaloSeg", "10", "-DryRun" });
            Assert.Equal(20, o.BatteryThreshold);
            Assert.Equal(10, o.IntervalSeconds);
            Assert.True(o.DryRun);
        }

        [Theory]
        [InlineData("--threshold", "0")]
        [InlineData("--threshold", "101")]
        [InlineData("--threshold", "abc")]
        [InlineData("--interval", "0")]
        public void RejectsInvalidNumbers(string name, string value)
        {
            Assert.Throws<ArgumentException>(() => Options.Parse(new[] { name, value }));
        }

        [Fact]
        public void RejectsUnknownAndMissing()
        {
            Assert.Throws<ArgumentException>(() => Options.Parse(new[] { "--nothing" }));
            Assert.Throws<ArgumentException>(() => Options.Parse(new[] { "--threshold" }));
            Assert.Throws<ArgumentException>(() => Options.Parse(new[] { "stray" }));
        }

        [Fact]
        public void RunArgumentsRoundTrip()
        {
            var o = Options.Parse(new[] { "--install", "--threshold", "12", "--vmrun", @"C:\Program Files\VMware\vmrun.exe", "--no-tray" });
            string args = o.ToRunArguments();
            Assert.Equal(@"--threshold 12 --interval 30 --vmrun ""C:\Program Files\VMware\vmrun.exe"" --no-tray", args);
            Assert.DoesNotContain("install", args);
        }

        [Theory]
        [InlineData(@"C:\a\b.vmx", @"C:\a\b.vmx")]
        [InlineData(@"C:\My VMs\x.vmx", @"""C:\My VMs\x.vmx""")]
        [InlineData(@"C:\dir with space\", @"""C:\dir with space\\""")]
        [InlineData(@"a""b", @"""a\""b""")]
        [InlineData("", @"""""")]
        public void QuotesForWindowsCommandLine(string input, string expected)
        {
            Assert.Equal(expected, Options.Quote(input));
        }
    }
}
