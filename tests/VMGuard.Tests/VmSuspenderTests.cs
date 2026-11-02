using System.Linq;
using Xunit;

namespace VMGuard.Tests
{
    public class VmSuspenderTests
    {
        [Fact]
        public void ParsesVmrunList()
        {
            string output = "Total running VMs: 2\r\nC:\\VMs\\Ubuntu\\Ubuntu.vmx\r\n  D:\\Otra VM\\Win.VMX  \r\n\r\n";
            var vms = VmSuspender.ParseList(output);
            Assert.Equal(new[] { @"C:\VMs\Ubuntu\Ubuntu.vmx", @"D:\Otra VM\Win.VMX" }, vms.ToArray());
        }

        [Fact]
        public void ParsesEmptyList()
        {
            Assert.Empty(VmSuspender.ParseList("Total running VMs: 0\r\n"));
            Assert.Empty(VmSuspender.ParseList(null));
        }
    }
}
