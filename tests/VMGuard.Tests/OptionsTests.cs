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
            var o = Options.Parse(new[] { "--instalar", "--umbral", "15", "--intervalo=5", "--vmrun", @"C:\VMware\vmrun.exe", "--dryrun", "--sin-icono" });
            Assert.Equal(Command.Install, o.Command);
            Assert.Equal(15, o.BatteryThreshold);
            Assert.Equal(5, o.IntervalSeconds);
            Assert.Equal(@"C:\VMware\vmrun.exe", o.VmrunPath);
            Assert.True(o.DryRun);
            Assert.True(o.NoTrayIcon);
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
        [InlineData("--umbral", "0")]
        [InlineData("--umbral", "101")]
        [InlineData("--umbral", "abc")]
        [InlineData("--intervalo", "0")]
        public void RejectsInvalidNumbers(string name, string value)
        {
            Assert.Throws<ArgumentException>(() => Options.Parse(new[] { name, value }));
        }

        [Fact]
        public void RejectsUnknownAndMissing()
        {
            Assert.Throws<ArgumentException>(() => Options.Parse(new[] { "--nada" }));
            Assert.Throws<ArgumentException>(() => Options.Parse(new[] { "--umbral" }));
            Assert.Throws<ArgumentException>(() => Options.Parse(new[] { "suelto" }));
        }

        [Fact]
        public void RunArgumentsRoundTrip()
        {
            var o = Options.Parse(new[] { "--instalar", "--umbral", "12", "--vmrun", @"C:\Program Files\VMware\vmrun.exe", "--sin-icono" });
            string args = o.ToRunArguments();
            Assert.Equal(@"--umbral 12 --intervalo 30 --vmrun ""C:\Program Files\VMware\vmrun.exe"" --sin-icono", args);
            Assert.DoesNotContain("instalar", args);
        }

        [Theory]
        [InlineData(@"C:\a\b.vmx", @"C:\a\b.vmx")]
        [InlineData(@"C:\Mis VMs\x.vmx", @"""C:\Mis VMs\x.vmx""")]
        [InlineData(@"C:\dir with space\", @"""C:\dir with space\\""")]
        [InlineData(@"a""b", @"""a\""b""")]
        [InlineData("", @"""""")]
        public void QuotesForWindowsCommandLine(string input, string expected)
        {
            Assert.Equal(expected, Options.Quote(input));
        }
    }
}
