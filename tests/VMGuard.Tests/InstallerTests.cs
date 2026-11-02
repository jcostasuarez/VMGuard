using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace VMGuard.Tests
{
    public class InstallerTests
    {
        private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        [Fact]
        public void TaskXmlHasExpectedSettings()
        {
            XDocument doc = Installer.BuildTaskXml(@"PC\usuario", @"C:\Users\usuario\VMGuard\VMGuard.exe", "--umbral 10 --intervalo 30", @"C:\Users\usuario\VMGuard");
            XElement task = doc.Root;

            Assert.Equal(@"PC\usuario", task.Descendants(Ns + "LogonTrigger").Single().Element(Ns + "UserId").Value);
            Assert.Equal("LeastPrivilege", task.Descendants(Ns + "RunLevel").Single().Value);
            Assert.Equal("false", task.Descendants(Ns + "DisallowStartIfOnBatteries").Single().Value);
            Assert.Equal("false", task.Descendants(Ns + "StopIfGoingOnBatteries").Single().Value);
            Assert.Equal("PT0S", task.Descendants(Ns + "ExecutionTimeLimit").Single().Value);
            Assert.Equal("IgnoreNew", task.Descendants(Ns + "MultipleInstancesPolicy").Single().Value);

            XElement exec = task.Descendants(Ns + "Exec").Single();
            Assert.Equal(@"C:\Users\usuario\VMGuard\VMGuard.exe", exec.Element(Ns + "Command").Value);
            Assert.Equal("--umbral 10 --intervalo 30", exec.Element(Ns + "Arguments").Value);
        }
    }
}
