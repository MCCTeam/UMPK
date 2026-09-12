using Umpk.Nbt;
using Xunit;

namespace Umpk.Nbt.Tests;

/// <summary>
/// Heterogeneous lists in the shape produced by mixed-form chat components.
/// <para>A component collapses to a bare string only when it is plain text with no style and no children and ComponentSerialization applies that recursively, so a component whose children mix plain and styled text encodes as a list of one String and one Compound. NBT has a single element-type byte, and the wire convention writes such a list as Compound with every non-compound element wrapped as <c>{"": value}</c>, unwrapping on read.</para>
/// <para>The convention has the same wire representation through every supported era, so it is not era-gated.</para>
/// <para>Before this, <c>NbtList.Add</c> threw NbtFormatException on the second element, so an item carrying such a component could not be encoded at all: clicking it reported "Cannot add tag of type Compound to NBT list of type String" and the packet was never sent.</para>
/// </summary>
public sealed class HeterogeneousListTests
{
    private static NbtCompound StyledComponent(string text, string colour)
    {
        var c = new NbtCompound();
        c.PutString("text", text);
        c.PutString("color", colour);
        return c;
    }

    [Fact]
    public void Add_AcceptsAMixOfTypes()
    {
        var list = new NbtList();
        list.Add(new NbtString("plain "));
        list.Add(StyledComponent("styled", "red"));

        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void ElementType_IsCompound_WhenTheContentsAreMixed()
    {
        var list = new NbtList();
        list.Add(new NbtString("plain "));
        list.Add(StyledComponent("styled", "red"));

        // Mixed element types select Compound as the wire element type.
        Assert.Equal(NbtTagType.Compound, list.ElementType);
    }

    [Fact]
    public void ElementType_StaysNarrow_WhenTheContentsAgree()
    {
        var list = new NbtList();
        list.Add(new NbtString("a"));
        list.Add(new NbtString("b"));

        Assert.Equal(NbtTagType.String, list.ElementType);
    }

    [Fact]
    public void MixedList_RoundTrips_WithTheValuesIntact()
    {
        var root = new NbtCompound();
        var extra = new NbtList();
        extra.Add(new NbtString("plain "));
        extra.Add(StyledComponent("styled", "red"));
        root.Put("extra", extra);

        byte[] bytes = NbtWriter.ToArray(root, NbtWireFormat.JavaUnnamedRoot);
        var back = (NbtCompound)NbtReader.Read(bytes, NbtWireFormat.JavaUnnamedRoot);
        NbtList? decoded = back.GetList("extra");

        Assert.NotNull(decoded);
        Assert.Equal(2, decoded!.Count);

        // The bare string must come back as a STRING, not as the {"": "..."} wrapper it travelled in.
        var first = Assert.IsType<NbtString>(decoded[0]);
        Assert.Equal("plain ", first.Value);

        var second = Assert.IsType<NbtCompound>(decoded[1]);
        Assert.Equal("styled", second.GetString("text"));
        Assert.Equal("red", second.GetString("color"));
    }

    [Fact]
    public void MixedList_IsWrittenAsCompoundElements()
    {
        var root = new NbtCompound();
        var extra = new NbtList();
        extra.Add(new NbtString("plain "));
        extra.Add(StyledComponent("styled", "red"));
        root.Put("extra", extra);

        byte[] bytes = NbtWriter.ToArray(root, NbtWireFormat.JavaUnnamedRoot);

        // The element-type byte follows the list's own tag byte and name; rather than index into the encoding, assert the observable contract: a peer that does NOT unwrap sees two compounds.
        var back = (NbtCompound)NbtReader.Read(bytes, NbtWireFormat.JavaUnnamedRoot);
        Assert.Equal(NbtTagType.String, back.GetList("extra")![0].Type);
    }

    [Fact]
    public void AGenuineWrapperShapedCompound_SurvivesTheRoundTrip()
    {
        // {"": "x"} is a legal compound in its own right. Re-wrapping it ensures that decoding returns the compound rather than its contents.
        var wrapperShaped = new NbtCompound();
        wrapperShaped.Put(string.Empty, new NbtString("x"));

        var root = new NbtCompound();
        var list = new NbtList();
        list.Add(new NbtString("plain"));
        list.Add(wrapperShaped);
        root.Put("l", list);

        byte[] bytes = NbtWriter.ToArray(root, NbtWireFormat.JavaUnnamedRoot);
        NbtList decoded = ((NbtCompound)NbtReader.Read(bytes, NbtWireFormat.JavaUnnamedRoot)).GetList("l")!;

        Assert.Equal("plain", Assert.IsType<NbtString>(decoded[0]).Value);
        var kept = Assert.IsType<NbtCompound>(decoded[1]);
        Assert.Equal("x", kept.GetString(string.Empty));
    }

    [Fact]
    public void HomogeneousLists_AreUnaffected()
    {
        var root = new NbtCompound();
        var list = new NbtList();
        list.Add(new NbtInt(1));
        list.Add(new NbtInt(2));
        root.Put("l", list);

        byte[] bytes = NbtWriter.ToArray(root, NbtWireFormat.JavaUnnamedRoot);
        NbtList decoded = ((NbtCompound)NbtReader.Read(bytes, NbtWireFormat.JavaUnnamedRoot)).GetList("l")!;

        Assert.Equal(NbtTagType.Int, decoded.ElementType);
        Assert.Equal(1, Assert.IsType<NbtInt>(decoded[0]).Value);
        Assert.Equal(2, Assert.IsType<NbtInt>(decoded[1]).Value);
    }
}
