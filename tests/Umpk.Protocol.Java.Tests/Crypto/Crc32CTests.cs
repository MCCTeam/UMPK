using System.Text;
using Umpk.Protocol.Java.Crypto;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Crypto;

public class Crc32CTests
{
    [Fact]
    public void StandardVector_123456789()
    {
        // The canonical CRC-32C check value.
        byte[] data = Encoding.ASCII.GetBytes("123456789");
        Assert.Equal(0xE3069283u, Crc32C.Compute(data));
        Assert.Equal(0xE3069283u, Crc32C.Compute(data, forceSoftware: true));
    }

    [Fact]
    public void EmptyInput_IsZero()
    {
        Assert.Equal(0u, Crc32C.Compute([]));
        Assert.Equal(0u, Crc32C.Compute([], forceSoftware: true));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(255)]
    public void AllZeros_HardwareMatchesSoftware(int length)
    {
        var data = new byte[length];
        Assert.Equal(Crc32C.Compute(data, forceSoftware: true), Crc32C.Compute(data));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    [InlineData(1000)]
    public void AllOnes_HardwareMatchesSoftware(int length)
    {
        var data = new byte[length];
        Array.Fill(data, (byte)0xFF);
        Assert.Equal(Crc32C.Compute(data, forceSoftware: true), Crc32C.Compute(data));
    }

    [Fact]
    public void RandomInputs_HardwareMatchesSoftware()
    {
        for (int i = 0; i < 200; i++)
        {
            var data = new byte[Random.Shared.Next(0, 300)];
            Random.Shared.NextBytes(data);
            Assert.Equal(Crc32C.Compute(data, forceSoftware: true), Crc32C.Compute(data));
        }
    }

    [Fact]
    public void Append_EqualsSingleShot()
    {
        var data = new byte[500];
        Random.Shared.NextBytes(data);

        uint single = Crc32C.Compute(data);

        uint running = 0;
        running = Crc32C.Append(running, data.AsSpan(0, 100), forceSoftware: false);
        running = Crc32C.Append(running, data.AsSpan(100, 250), forceSoftware: false);
        running = Crc32C.Append(running, data.AsSpan(350), forceSoftware: false);

        Assert.Equal(single, running);
    }

    [Fact]
    public void AllZeros_KnownValues()
    {
        // Independently known CRC-32C of N zero bytes (verified against a reference implementation).
        Assert.Equal(0x48674BC7u, Crc32C.Compute(new byte[4]));
        Assert.Equal(0x8A9136AAu, Crc32C.Compute(new byte[32]));
    }
}
