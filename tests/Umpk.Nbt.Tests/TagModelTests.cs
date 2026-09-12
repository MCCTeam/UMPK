using Umpk.Nbt;
using Xunit;

namespace Umpk.Nbt.Tests;

public sealed class TagModelTests
{
    [Fact]
    public void EndTag_IsSingletonAndCopiesToItself()
    {
        Assert.Same(NbtEnd.Instance, NbtEnd.Instance.Copy());
        Assert.Equal(NbtTagType.End, NbtEnd.Instance.Type);
    }

    [Fact]
    public void ByteTag_BoolConstructorStoresOneOrZero()
    {
        Assert.Equal((sbyte)1, new NbtByte(true).Value);
        Assert.Equal((sbyte)0, new NbtByte(false).Value);
        Assert.True(new NbtByte(true).AsBool);
        Assert.False(new NbtByte(false).AsBool);
    }

    [Fact]
    public void NumericAccessors_MatchVanillaConversions()
    {
        // Narrow integer conversions keep the low 16 or 8 bits.
        var i = new NbtInt(0x1_2345);
        Assert.Equal((short)0x2345, i.AsShort);
        Assert.Equal(unchecked((sbyte)0x45), i.AsSByte);

        // Floating-point to integer conversion floors toward negative infinity.
        Assert.Equal(3, new NbtDouble(3.9).AsInt);
        Assert.Equal(-4, new NbtDouble(-3.1).AsInt);
        Assert.Equal(3, new NbtFloat(3.9f).AsInt);
    }

    [Fact]
    public void ScalarEquality_UsesValue()
    {
        Assert.Equal(new NbtInt(7), new NbtInt(7));
        Assert.NotEqual<NbtTag>(new NbtInt(7), new NbtLong(7));
        Assert.Equal(new NbtString("x"), new NbtString("x"));
        Assert.Equal(new NbtDouble(double.NaN), new NbtDouble(double.NaN));
    }

    [Fact]
    public void StringTag_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new NbtString(null!));
    }

    [Fact]
    public void ByteArray_CopyIsIndependent()
    {
        var original = new NbtByteArray([1, 2, 3]);
        var copy = (NbtByteArray)original.Copy();
        copy.Value[0] = 42;
        Assert.Equal((sbyte)1, original.Value[0]);
        Assert.NotEqual(original, copy);
    }

    // A mixed list reports Compound as its element type and wraps non-compound values on write.
    [Fact]
    public void List_ReportsCompound_ForMixedContents()
    {
        var list = new NbtList();
        list.Add(new NbtInt(1));
        Assert.Equal(NbtTagType.Int, list.ElementType);

        list.Add(new NbtString("no"));
        Assert.Equal(NbtTagType.Compound, list.ElementType);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void List_EmptyHasEndElementType()
    {
        var list = new NbtList();
        Assert.Equal(NbtTagType.End, list.ElementType);
        list.Add(new NbtByte((sbyte)1));
        list.Clear();
        Assert.Equal(NbtTagType.End, list.ElementType);
    }

    [Fact]
    public void List_CopyIsDeep()
    {
        var list = new NbtList { new NbtIntArray([1, 2]) };
        var copy = (NbtList)list.Copy();
        ((NbtIntArray)copy[0]).Value[0] = 9;
        Assert.Equal(1, ((NbtIntArray)list[0]).Value[0]);
    }

    [Fact]
    public void Compound_TypedAccessorsAndTryGet()
    {
        var c = new NbtCompound();
        c.PutInt("i", 5);
        c.PutString("s", "hi");
        c.PutBool("b", true);

        Assert.Equal(5, c.GetInt("i"));
        Assert.Equal("hi", c.GetString("s"));
        Assert.True(c.GetBool("b"));
        Assert.Equal(0, c.GetInt("missing"));
        Assert.Equal(string.Empty, c.GetString("missing"));

        Assert.True(c.TryGet("i", out NbtTag? raw));
        Assert.IsType<NbtInt>(raw);
        Assert.True(c.TryGet("i", out NbtInt? typed));
        Assert.Equal(5, typed!.Value);
        Assert.False(c.TryGet("i", out NbtString? _));
        Assert.False(c.TryGet("missing", out NbtInt? _));
    }

    [Fact]
    public void Compound_IndexerThrowsOnMissing()
    {
        var c = new NbtCompound();
        Assert.Throws<KeyNotFoundException>(() => c["nope"]);
    }

    [Fact]
    public void Compound_RemoveAndContains()
    {
        var c = new NbtCompound();
        c.PutInt("x", 1);
        Assert.True(c.ContainsKey("x"));
        Assert.True(c.Remove("x"));
        Assert.False(c.ContainsKey("x"));
        Assert.False(c.Remove("x"));
        Assert.True(c.IsEmpty);
    }

    [Fact]
    public void Compound_ReassignKeepsPosition()
    {
        var c = new NbtCompound();
        c.PutInt("a", 1);
        c.PutInt("b", 2);
        c.PutInt("a", 99);
        Assert.Equal(new[] { "a", "b" }, c.Keys.ToArray());
        Assert.Equal(99, c.GetInt("a"));
    }

    [Fact]
    public void Compound_EqualityIsOrderIndependent()
    {
        var a = new NbtCompound();
        a.PutInt("x", 1);
        a.PutInt("y", 2);
        var b = new NbtCompound();
        b.PutInt("y", 2);
        b.PutInt("x", 1);
        // Use Equals directly; xUnit's Assert.Equal would fall back to IEnumerable order comparison.
        Assert.True(a.Equals(b));
        Assert.True(b.Equals(a));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}
