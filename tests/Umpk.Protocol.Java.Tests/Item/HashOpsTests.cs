using System.Buffers.Binary;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Crypto;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary>Locks the 1.21.5+ <see cref="HashOps"/> structural hashing model against hand-computed byte layouts. Each expected value is computed here by building the exact tag/little-endian byte stream used by the wire contract, then running it through the known-good <see cref="Crc32C"/> primitive. That keeps the assertion independent of the production HashOps code path while pinning the wire format.</summary>
public class HashOpsTests
{
    private static readonly HashOps Ops = new();

    private static int Crc(params byte[] data) => unchecked((int)Crc32C.Compute(data));

    [Fact]
    public void Empty_EmptyMap_EmptyList_MatchTagBytes()
    {
        Assert.Equal(Crc(1), Ops.Empty);
        Assert.Equal(Crc(2, 3), Ops.EmptyMap);
        Assert.Equal(Crc(4, 5), Ops.EmptyList);
    }

    [Fact]
    public void Boolean_UsesTag13()
    {
        Assert.Equal(Crc(13, 1), Ops.Boolean(true));
        Assert.Equal(Crc(13, 0), Ops.Boolean(false));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void Int_IsTag8ThenLittleEndian(int value)
    {
        var b = new byte[5];
        b[0] = 8;
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(1), value);
        Assert.Equal(Crc(b), Ops.Int(value));
    }

    [Fact]
    public void Byte_Short_Long_UseTheirTags()
    {
        var s = new byte[3];
        s[0] = 7;
        BinaryPrimitives.WriteInt16LittleEndian(s.AsSpan(1), 258);
        Assert.Equal(Crc(s), Ops.Short(258));

        var l = new byte[9];
        l[0] = 9;
        BinaryPrimitives.WriteInt64LittleEndian(l.AsSpan(1), 0x0102030405060708L);
        Assert.Equal(Crc(l), Ops.Long(0x0102030405060708L));

        Assert.Equal(Crc(6, unchecked((byte)-5)), Ops.Byte(-5));
    }

    [Fact]
    public void Float_Double_HashRawBitsLittleEndian()
    {
        var f = new byte[5];
        f[0] = 10;
        BinaryPrimitives.WriteInt32LittleEndian(f.AsSpan(1), BitConverter.SingleToInt32Bits(1.5f));
        Assert.Equal(Crc(f), Ops.Float(1.5f));

        var d = new byte[9];
        d[0] = 11;
        BinaryPrimitives.WriteInt64LittleEndian(d.AsSpan(1), BitConverter.DoubleToInt64Bits(-2.25));
        Assert.Equal(Crc(d), Ops.Double(-2.25));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("minecraft:custom_name")]
    public void String_IsTag12_LengthLE_ThenUtf16CharsLE(string value)
    {
        var b = new byte[1 + 4 + (value.Length * 2)];
        b[0] = 12;
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(1, 4), value.Length);
        int pos = 5;
        foreach (char c in value)
        {
            b[pos++] = (byte)c;
            b[pos++] = (byte)(c >> 8);
        }

        Assert.Equal(Crc(b), Ops.String(value));
    }

    [Fact]
    public void List_WrapsElementAsBytesInOrderWithTags4And5()
    {
        int e0 = Ops.Int(7);
        int e1 = Ops.String("x");
        var b = new byte[2 + (2 * 4)];
        b[0] = 4;
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(1), e0);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(5), e1);
        b[9] = 5;
        Assert.Equal(Crc(b), Ops.List([e0, e1]));
    }

    [Fact]
    public void Map_SortsEntriesByPadToLongOfKeyThenValue()
    {
        // Two entries whose key hashes force a specific ordering under unsigned (padToLong) comparison.
        (int Key, int Value) a = (Ops.String("aaa"), Ops.Int(1));
        (int Key, int Value) b = (Ops.String("zzz"), Ops.Int(2));

        long ka = a.Key & 0xFFFFFFFFL;
        long kb = b.Key & 0xFFFFFFFFL;
        (int Key, int Value) first = ka <= kb ? a : b;
        (int Key, int Value) second = ka <= kb ? b : a;

        var buf = new byte[2 + (2 * 8)];
        buf[0] = 2;
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(1), first.Key);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(5), first.Value);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(9), second.Key);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(13), second.Value);
        buf[17] = 3;

        // Order of the input list must not matter: both orderings hash to the sorted layout.
        Assert.Equal(Crc(buf), Ops.Map([a, b]));
        Assert.Equal(Crc(buf), Ops.Map([b, a]));
    }

    [Fact]
    public void IntList_LongList_ByteList_UseTheirTags()
    {
        var ints = new byte[2 + (2 * 4)];
        ints[0] = 16;
        BinaryPrimitives.WriteInt32LittleEndian(ints.AsSpan(1), 10);
        BinaryPrimitives.WriteInt32LittleEndian(ints.AsSpan(5), -20);
        ints[9] = 17;
        Assert.Equal(Crc(ints), Ops.IntList([10, -20]));

        var longs = new byte[2 + 8];
        longs[0] = 18;
        BinaryPrimitives.WriteInt64LittleEndian(longs.AsSpan(1), 5L);
        longs[9] = 19;
        Assert.Equal(Crc(longs), Ops.LongList([5L]));

        Assert.Equal(Crc(14, 0xAA, 0xBB, 15), Ops.ByteList([0xAA, 0xBB]));
    }
}
