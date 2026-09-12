using Umpk.Nbt;
using Xunit;

namespace Umpk.Nbt.Tests;

public sealed class WireRoundTripTests
{
    private static NbtCompound SampleCompound()
    {
        var c = new NbtCompound();
        c.PutByte("byte", -12);
        c.PutShort("short", 12345);
        c.PutInt("int", -70000);
        c.PutLong("long", 9_000_000_000L);
        c.PutFloat("float", 3.5f);
        c.PutDouble("double", 2.718281828);
        c.PutString("string", "hello é world");
        c.Put("bytes", new NbtByteArray([1, -2, 3, -4]));
        c.Put("ints", new NbtIntArray([1, 2, 300000]));
        c.Put("longs", new NbtLongArray([1L, -2L, 9_000_000_000L]));

        var list = new NbtList();
        list.Add(new NbtInt(10));
        list.Add(new NbtInt(20));
        c.Put("list", list);

        var nested = new NbtCompound();
        nested.PutString("k", "v");
        c.Put("nested", nested);

        var listOfCompounds = new NbtList();
        var e0 = new NbtCompound();
        e0.PutInt("id", 1);
        listOfCompounds.Add(e0);
        c.Put("entries", listOfCompounds);

        return c;
    }

    [Theory]
    [InlineData(NbtWireFormat.JavaNamedRoot)]
    [InlineData(NbtWireFormat.JavaUnnamedRoot)]
    [InlineData(NbtWireFormat.JavaRootTagOrString)]
    public void Compound_RoundTripsForEveryFlavor(NbtWireFormat format)
    {
        NbtCompound original = SampleCompound();
        byte[] encoded = NbtWriter.ToArray(original, format);
        var decoded = (NbtCompound)NbtReader.Read(encoded, format);
        Assert.Equal(original, decoded);

        byte[] reencoded = NbtWriter.ToArray(decoded, format);
        Assert.Equal(encoded, reencoded);
    }

    [Fact]
    public void EveryTagType_RoundTripsInsideCompound()
    {
        var c = new NbtCompound();
        c.Put("end_via_list", new NbtList(NbtTagType.End)); // empty list -> End element type on wire
        c.Put("b", new NbtByte((sbyte)7));
        c.Put("s", new NbtShort(-3));
        c.Put("i", new NbtInt(int.MinValue));
        c.Put("l", new NbtLong(long.MaxValue));
        c.Put("f", new NbtFloat(float.Epsilon));
        c.Put("d", new NbtDouble(double.MaxValue));
        c.Put("ba", new NbtByteArray([sbyte.MinValue, 0, sbyte.MaxValue]));
        c.Put("str", new NbtString("x"));
        c.Put("ia", new NbtIntArray([int.MinValue, 0, int.MaxValue]));
        c.Put("la", new NbtLongArray([long.MinValue, 0, long.MaxValue]));

        byte[] encoded = NbtWriter.ToArray(c, NbtWireFormat.JavaNamedRoot);
        var decoded = (NbtCompound)NbtReader.Read(encoded, NbtWireFormat.JavaNamedRoot);
        Assert.Equal(c, decoded);
    }

    [Fact]
    public void NamedRoot_WritesEmptyRootName()
    {
        var c = new NbtCompound();
        byte[] encoded = NbtWriter.ToArray(c, NbtWireFormat.JavaNamedRoot);
        // type(0x0A) + name-length(0x00 0x00) + end(0x00)
        Assert.Equal(new byte[] { 0x0A, 0x00, 0x00, 0x00 }, encoded);
    }

    [Fact]
    public void UnnamedRoot_HasNoRootName()
    {
        var c = new NbtCompound();
        byte[] encoded = NbtWriter.ToArray(c, NbtWireFormat.JavaUnnamedRoot);
        // type(0x0A) + end(0x00), no name bytes
        Assert.Equal(new byte[] { 0x0A, 0x00 }, encoded);
    }

    [Fact]
    public void RootTagOrString_AllowsBareString()
    {
        var s = new NbtString("just a string");
        byte[] encoded = NbtWriter.ToArray(s, NbtWireFormat.JavaRootTagOrString);
        var decoded = NbtReader.Read(encoded, NbtWireFormat.JavaRootTagOrString);
        Assert.Equal(s, decoded);
        Assert.Equal((byte)NbtTagType.String, encoded[0]);
    }

    [Fact]
    public void UnnamedRoot_BareEndByteDecodesToEnd()
    {
        var decoded = NbtReader.Read(new byte[] { 0x00 }, NbtWireFormat.JavaUnnamedRoot);
        Assert.Same(NbtEnd.Instance, decoded);
    }

    [Fact]
    public void NamedRoot_RejectsNonCompoundRoot()
    {
        Assert.Throws<NbtFormatException>(() => NbtWriter.ToArray(new NbtInt(1), NbtWireFormat.JavaNamedRoot));
        // 0x03 (int) + name "" + payload, read as named root -> must reject
        byte[] bytes = [0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01];
        Assert.Throws<NbtFormatException>(() => NbtReader.Read(bytes, NbtWireFormat.JavaNamedRoot));
    }

    [Fact]
    public void NestedListsAndCompounds_RoundTrip()
    {
        var outer = new NbtList();
        for (int i = 0; i < 3; i++)
        {
            var inner = new NbtList();
            inner.Add(new NbtString($"s{i}"));
            outer.Add(inner);
        }

        var c = new NbtCompound();
        c.Put("m", outer);
        byte[] encoded = NbtWriter.ToArray(c, NbtWireFormat.JavaUnnamedRoot);
        var decoded = (NbtCompound)NbtReader.Read(encoded, NbtWireFormat.JavaUnnamedRoot);
        Assert.Equal(c, decoded);
    }

    [Fact]
    public void ByteArrayConvenience_MatchesSpanOverload()
    {
        var c = new NbtCompound();
        c.PutInt("x", 1);
        byte[] encoded = NbtWriter.ToArray(c, NbtWireFormat.JavaUnnamedRoot);
        NbtTag viaSpan = NbtReader.Read(encoded.AsSpan(), NbtWireFormat.JavaUnnamedRoot);
        NbtTag viaArray = NbtReader.Read(encoded, NbtWireFormat.JavaUnnamedRoot);
        Assert.Equal(viaSpan, viaArray);
    }

    [Fact]
    public void Read_ReportsBytesConsumed_WithTrailingData()
    {
        var c = new NbtCompound();
        c.PutInt("x", 1);
        byte[] encoded = NbtWriter.ToArray(c, NbtWireFormat.JavaUnnamedRoot);
        byte[] withTrailer = [.. encoded, 0xAA, 0xBB];
        NbtTag decoded = NbtReader.Read(withTrailer, NbtWireFormat.JavaUnnamedRoot, NbtAccounter.CreateDefault(), out int read);
        Assert.Equal(encoded.Length, read);
        Assert.Equal(c, decoded);
    }

    [Fact]
    public void Read_ThrowsOnTrailingDataInStrictOverload()
    {
        var c = new NbtCompound();
        c.PutInt("x", 1);
        byte[] encoded = NbtWriter.ToArray(c, NbtWireFormat.JavaUnnamedRoot);
        byte[] withTrailer = [.. encoded, 0xAA];
        Assert.Throws<NbtFormatException>(() => NbtReader.Read(withTrailer, NbtWireFormat.JavaUnnamedRoot));
    }
}
