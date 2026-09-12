using Umpk.Nbt;
using Xunit;

namespace Umpk.Nbt.Tests;

public sealed class MalformedInputTests
{
    private static byte[] Encode(NbtCompound c) => NbtWriter.ToArray(c, NbtWireFormat.JavaUnnamedRoot);

    private static NbtCompound Rich()
    {
        var c = new NbtCompound();
        c.PutInt("int", 123456);
        c.PutString("str", "value");
        c.Put("arr", new NbtLongArray([1, 2, 3]));
        var list = new NbtList();
        list.Add(new NbtInt(1));
        list.Add(new NbtInt(2));
        c.Put("list", list);
        return c;
    }

    [Fact]
    public void TruncationAtEveryBoundary_ThrowsTypedException()
    {
        byte[] full = Encode(Rich());
        // Every prefix shorter than the whole payload must fail cleanly, never with an IndexOutOfRange / OutOfMemory / OverflowException.
        for (int len = 0; len < full.Length; len++)
        {
            byte[] prefix = full[..len];
            Assert.Throws<NbtFormatException>(() => NbtReader.Read(prefix, NbtWireFormat.JavaUnnamedRoot));
        }
    }

    [Fact]
    public void EmptyInput_Throws()
    {
        Assert.Throws<NbtFormatException>(() => NbtReader.Read(Array.Empty<byte>(), NbtWireFormat.JavaUnnamedRoot));
    }

    [Fact]
    public void UnknownTagType_Throws()
    {
        byte[] bytes = [0x63]; // 99 is not a valid tag id
        Assert.Throws<NbtFormatException>(() => NbtReader.Read(bytes, NbtWireFormat.JavaUnnamedRoot));
    }

    [Fact]
    public void HugeDeclaredByteArrayLength_ThrowsSizeLimit()
    {
        // compound root, member "x": byte-array with declared length 0x7FFFFFFF, no data.
        var bytes = new List<byte> { 0x0A }; // compound
        bytes.Add(0x07); // byte array
        bytes.Add(0x00);
        bytes.Add(0x01);
        bytes.Add((byte)'x');
        bytes.AddRange(new byte[] { 0x7F, 0xFF, 0xFF, 0xFF }); // length ~2.1 billion
        Assert.Throws<NbtSizeLimitException>(() => NbtReader.Read(bytes.ToArray(), NbtWireFormat.JavaUnnamedRoot));
    }

    [Fact]
    public void HugeDeclaredArrayLength_WithinQuota_ThrowsTruncation()
    {
        // Declared length that passes the byte quota (via a generous accounter) but exceeds the buffer must still throw a format exception rather than allocating or overflowing.
        var bytes = new List<byte> { 0x0B }; // root int-array (unnamed)
        bytes.AddRange(new byte[] { 0x10, 0x00, 0x00, 0x00 }); // ~268M ints declared, no data
        Assert.Throws<NbtFormatException>(() =>
            NbtReader.Read(bytes.ToArray(), NbtWireFormat.JavaUnnamedRoot, NbtAccounter.Unlimited()));
    }

    [Fact]
    public void NegativeArrayLength_Throws()
    {
        var bytes = new List<byte> { 0x0B }; // int array root
        bytes.AddRange(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }); // -1
        Assert.Throws<NbtFormatException>(() => NbtReader.Read(bytes.ToArray(), NbtWireFormat.JavaUnnamedRoot, NbtAccounter.Unlimited()));
    }

    [Fact]
    public void ListWithMissingType_Throws()
    {
        // list root (0x09), element type End(0x00), count 1 -> illegal
        byte[] bytes = [0x09, 0x00, 0x00, 0x00, 0x00, 0x01];
        Assert.Throws<NbtFormatException>(() => NbtReader.Read(bytes, NbtWireFormat.JavaUnnamedRoot));
    }

    [Fact]
    public void DepthBomb_ThrowsDepthLimit()
    {
        // Deeply nested lists exceeding the default depth of 512. Unnamed root: first byte is the root list type. Each level is a list body: element-type byte (List = 0x09) + count. The innermost level closes with an empty list (element-type End, count 0).
        const int depth = 600;
        var payload = new List<byte> { 0x09 }; // root list type marker (framing)
        for (int i = 0; i < depth; i++)
        {
            payload.Add(0x09); // this list's element type == List
            payload.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x01 }); // count 1
        }

        payload.Add(0x00); // innermost list element type == End
        payload.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // count 0

        Assert.Throws<NbtDepthLimitException>(() =>
            NbtReader.Read(payload.ToArray(), NbtWireFormat.JavaUnnamedRoot, NbtAccounter.Unlimited()));
    }

    [Fact]
    public void DepthBomb_ViaCompounds_ThrowsDepthLimit()
    {
        // Deeply nested compounds: each level is member "a" of type compound, never closed.
        const int depth = 600;
        var bytes = new List<byte> { 0x0A }; // root compound (unnamed)
        for (int i = 0; i < depth; i++)
        {
            bytes.Add(0x0A); // member type compound
            bytes.Add(0x00);
            bytes.Add(0x01);
            bytes.Add((byte)'a');
        }

        Assert.Throws<NbtDepthLimitException>(() =>
            NbtReader.Read(bytes.ToArray(), NbtWireFormat.JavaUnnamedRoot, NbtAccounter.Unlimited()));
    }

    [Fact]
    public void ByteQuota_Trips()
    {
        byte[] full = Encode(Rich());
        var tiny = NbtAccounter.Create(8);
        Assert.Throws<NbtSizeLimitException>(() => NbtReader.Read(full, NbtWireFormat.JavaUnnamedRoot, tiny));
    }
}
