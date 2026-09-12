using Umpk.Protocol.Java.Crypto;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Protocol.Java.Tests.Crypto;

public class HardwarePathTests(ITestOutputHelper o)
{
    [Fact]
    public void ReportHardwareAvailability()
    {
        byte[] key = new byte[16];
        var cfb = AesCfb8.Create(key);
        o.WriteLine($"AesCfb8.IsHardwareAccelerated = {cfb.IsHardwareAccelerated}");
        o.WriteLine($"Crc32C.IsHardwareAccelerated = {Crc32C.IsHardwareAccelerated}");
    }
}
