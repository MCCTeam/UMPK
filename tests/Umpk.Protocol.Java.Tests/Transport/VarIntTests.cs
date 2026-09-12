using System.Buffers;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Transport;

public class VarIntTests
{
    [Theory]
    [InlineData(0, new byte[] { 0x00 })]
    [InlineData(1, new byte[] { 0x01 })]
    [InlineData(127, new byte[] { 0x7F })]
    [InlineData(128, new byte[] { 0x80, 0x01 })]
    [InlineData(255, new byte[] { 0xFF, 0x01 })]
    [InlineData(25565, new byte[] { 0xDD, 0xC7, 0x01 })]
    [InlineData(2097151, new byte[] { 0xFF, 0xFF, 0x7F })]
    [InlineData(int.MaxValue, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x07 })]
    [InlineData(-1, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F })]
    public void KnownVectors(int value, byte[] expected)
    {
        Span<byte> buf = stackalloc byte[5];
        int n = VarInt.Write(value, buf);
        Assert.Equal(expected, buf[..n].ToArray());
        Assert.Equal(expected.Length, VarInt.SizeOf(value));

        var seq = new ReadOnlySequence<byte>(expected);
        Assert.True(VarInt.TryRead(seq, out int decoded, out int read));
        Assert.Equal(value, decoded);
        Assert.Equal(expected.Length, read);
    }

    [Fact]
    public void TryRead_IncompleteSequence_ReturnsFalse()
    {
        // A continuation byte with nothing following.
        var seq = new ReadOnlySequence<byte>(new byte[] { 0x80 });
        Assert.False(VarInt.TryRead(seq, out _, out _));
    }

    [Fact]
    public void TryRead_TooLong_Throws()
    {
        var seq = new ReadOnlySequence<byte>(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x01 });
        Assert.Throws<InvalidDataException>(() => VarInt.TryRead(seq, out _, out _));
    }
}
