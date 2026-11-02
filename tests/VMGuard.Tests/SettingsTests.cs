using System;
using System.IO;
using Xunit;

namespace VMGuard.Tests
{
    public class SettingsTests
    {
        [Fact]
        public void SavesAndLoadsThreshold()
        {
            string dir = Path.Combine(Path.GetTempPath(), "VMGuardTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                Assert.Null(Settings.Load(dir).BatteryThreshold);

                var s = Settings.Load(dir);
                s.BatteryThreshold = 25;
                s.Save();

                Assert.Equal(25, Settings.Load(dir).BatteryThreshold);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Theory]
        [InlineData("threshold=15", 15)]
        [InlineData("  Threshold = 7  ", 7)]
        [InlineData("# comment\nthreshold=30", 30)]
        [InlineData("threshold=0", null)]
        [InlineData("threshold=101", null)]
        [InlineData("threshold=abc", null)]
        [InlineData("other=5", null)]
        public void ParsesThreshold(string text, int? expected)
        {
            var s = Settings.Load(Path.GetTempPath() + "does-not-exist-" + Guid.NewGuid().ToString("N"));
            s.Parse(text.Split('\n'));
            Assert.Equal(expected, s.BatteryThreshold);
        }
    }
}
