using Umpk.Nbt;
using Xunit;

namespace Umpk.Nbt.Tests;

public sealed class AccessorCoverageTests
{
    [Fact]
    public void AllNumericTags_ExposeEveryConversion()
    {
        NbtNumeric[] tags =
        [
            new NbtByte((sbyte)5),
            new NbtShort(5),
            new NbtInt(5),
            new NbtLong(5),
            new NbtFloat(5f),
            new NbtDouble(5d),
        ];

        foreach (NbtNumeric t in tags)
        {
            Assert.Equal(5L, t.AsLong);
            Assert.Equal(5, t.AsInt);
            Assert.Equal((short)5, t.AsShort);
            Assert.Equal((sbyte)5, t.AsSByte);
            Assert.Equal(5d, t.AsDouble);
            Assert.Equal(5f, t.AsFloat);
        }
    }

    [Fact]
    public void NumericMasking_LongAndShortAndFloat()
    {
        var l = new NbtLong(0x1_0000_2345L);
        Assert.Equal((short)0x2345, l.AsShort);
        Assert.Equal(unchecked((sbyte)0x45), l.AsSByte);
        Assert.Equal(0x2345, l.AsInt);

        var s = new NbtShort(-1);
        Assert.Equal(-1, s.AsInt);
        Assert.Equal(unchecked((sbyte)0xFF), s.AsSByte);

        var f = new NbtFloat(300.7f);
        Assert.Equal((short)(300 & 0xFFFF), f.AsShort);
        Assert.Equal(unchecked((sbyte)(300 & 0xFF)), f.AsSByte);
        Assert.Equal(300L, f.AsLong);

        var d = new NbtDouble(-2.5);
        Assert.Equal(-3L, d.AsLong);
        Assert.Equal((float)-2.5, d.AsFloat);
        Assert.Equal(unchecked((short)(-3 & 0xFFFF)), d.AsShort);
        Assert.Equal(unchecked((sbyte)(-3 & 0xFF)), d.AsSByte);
    }

    [Fact]
    public void ScalarEqualityAndHash_AllTypes()
    {
        Assert.True(new NbtByte((sbyte)1).Equals(new NbtByte((sbyte)1)));
        Assert.False(new NbtByte((sbyte)1).Equals("x"));
        Assert.Equal(new NbtShort(2).GetHashCode(), new NbtShort(2).GetHashCode());
        Assert.Equal(new NbtLong(3).GetHashCode(), new NbtLong(3).GetHashCode());
        Assert.Equal(new NbtFloat(4f).GetHashCode(), new NbtFloat(4f).GetHashCode());
        Assert.Equal(new NbtDouble(5d).GetHashCode(), new NbtDouble(5d).GetHashCode());
        Assert.Equal(new NbtString("s").GetHashCode(), new NbtString("s").GetHashCode());
        Assert.False(new NbtInt(1).Equals(new NbtInt(2)));
        Assert.False(new NbtLong(1).Equals(new NbtByte((sbyte)1)));
        Assert.False(new NbtFloat(1f).Equals(new NbtFloat(2f)));
        Assert.False(new NbtDouble(1d).Equals(new NbtDouble(2d)));
        Assert.False(new NbtShort(1).Equals(new NbtShort(2)));
    }

    [Fact]
    public void Arrays_EqualityAndHashCode()
    {
        Assert.True(new NbtByteArray([1, 2]).Equals(new NbtByteArray([1, 2])));
        Assert.False(new NbtByteArray([1, 2]).Equals(new NbtByteArray([1, 3])));
        Assert.False(new NbtByteArray([1]).Equals("x"));
        Assert.Equal(new NbtByteArray([1, 2]).GetHashCode(), new NbtByteArray([1, 2]).GetHashCode());

        Assert.True(new NbtIntArray([1, 2]).Equals(new NbtIntArray([1, 2])));
        Assert.False(new NbtIntArray([1]).Equals(new NbtIntArray([2])));
        Assert.Equal(new NbtIntArray([1, 2]).GetHashCode(), new NbtIntArray([1, 2]).GetHashCode());

        Assert.True(new NbtLongArray([1, 2]).Equals(new NbtLongArray([1, 2])));
        Assert.False(new NbtLongArray([1]).Equals(new NbtLongArray([2])));
        Assert.Equal(new NbtLongArray([1, 2]).GetHashCode(), new NbtLongArray([1, 2]).GetHashCode());
    }

    [Fact]
    public void Arrays_RejectNull()
    {
        Assert.Throws<ArgumentNullException>(() => new NbtByteArray(null!));
        Assert.Throws<ArgumentNullException>(() => new NbtIntArray(null!));
        Assert.Throws<ArgumentNullException>(() => new NbtLongArray(null!));
    }

    [Fact]
    public void List_FullIListSurface()
    {
        var list = new NbtList { new NbtInt(1), new NbtInt(2), new NbtInt(3) };
        Assert.Equal(3, list.Count);
        Assert.False(list.IsReadOnly);
        Assert.Equal(0, list.IndexOf(list[0]));
        Assert.Contains(list[1], list);

        list[0] = new NbtInt(10);
        Assert.Equal(10, ((NbtInt)list[0]).Value);

        list.Insert(1, new NbtInt(20));
        Assert.Equal(20, ((NbtInt)list[1]).Value);

        // The indexer and Insert no longer refuse a different type; vanilla's ListTag does not either, and the list simply reports Compound and wraps on write. See HeterogeneousListTests.
        list.Insert(0, new NbtString("mixed"));
        Assert.Equal(NbtTagType.Compound, list.ElementType);
        list.RemoveAt(0);

        var target = new NbtTag[5];
        list.CopyTo(target, 0);
        Assert.Equal(4, list.Count);

        NbtTag removed = list[1];
        Assert.True(list.Remove(removed));
        list.RemoveAt(0);
        Assert.Equal(2, list.Count);

        int seen = 0;
        foreach (NbtTag _ in list)
            seen++;

        Assert.Equal(2, seen);

        System.Collections.IEnumerable nonGeneric = list;
        Assert.NotNull(nonGeneric.GetEnumerator());
    }

    [Fact]
    public void List_InsertIntoEmptySetsElementType()
    {
        var list = new NbtList();
        list.Insert(0, new NbtLong(1));
        Assert.Equal(NbtTagType.Long, list.ElementType);
    }

    [Fact]
    public void List_NullRejected()
    {
        var list = new NbtList();
        Assert.Throws<ArgumentNullException>(() => list.Add(null!));
        Assert.Throws<ArgumentNullException>(() => list.Insert(0, null!));
    }

    [Fact]
    public void List_EqualityAndHash()
    {
        var a = new NbtList { new NbtInt(1), new NbtInt(2) };
        var b = new NbtList { new NbtInt(1), new NbtInt(2) };
        var c = new NbtList { new NbtInt(1), new NbtInt(3) };
        Assert.True(a.Equals(b));
        Assert.False(a.Equals(c));
        Assert.False(a.Equals("x"));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());

        var shorter = new NbtList { new NbtInt(1) };
        Assert.False(a.Equals(shorter));
    }

    [Fact]
    public void Compound_AllPutHelpersAndTypedGetters()
    {
        var c = new NbtCompound();
        c.PutByte("by", -1);
        c.PutShort("sh", -2);
        c.PutLong("lo", -3);
        c.PutFloat("fl", 1.25f);
        c.PutDouble("do", 2.5);
        c.Put("li", new NbtList { new NbtInt(1) });
        c.Put("co", new NbtCompound());

        Assert.Equal((sbyte)-1, c.GetByte("by"));
        Assert.Equal((short)-2, c.GetShort("sh"));
        Assert.Equal(-3L, c.GetLong("lo"));
        Assert.Equal(1.25f, c.GetFloat("fl"));
        Assert.Equal(2.5, c.GetDouble("do"));
        Assert.NotNull(c.GetList("li"));
        Assert.NotNull(c.GetCompound("co"));
        Assert.Null(c.GetList("co"));
        Assert.Null(c.GetCompound("li"));

        Assert.Equal(NbtTagType.Byte, c.GetTagType("by"));
        Assert.Equal(NbtTagType.End, c.GetTagType("missing"));

        // Default-on-missing numeric getters.
        Assert.Equal((short)0, c.GetShort("missing"));
        Assert.Equal(0L, c.GetLong("missing"));
        Assert.Equal((sbyte)0, c.GetByte("missing"));
        Assert.Equal(0f, c.GetFloat("missing"));
        Assert.Equal(0d, c.GetDouble("missing"));
        Assert.False(c.GetBool("missing"));

        c.Clear();
        Assert.True(c.IsEmpty);
    }

    [Fact]
    public void Compound_NullArgumentsRejected()
    {
        var c = new NbtCompound();
        Assert.Throws<ArgumentNullException>(() => c.Put(null!, new NbtInt(1)));
        Assert.Throws<ArgumentNullException>(() => c.Put("k", null!));
        Assert.Throws<ArgumentNullException>(() => c.ContainsKey(null!));
        Assert.Throws<ArgumentNullException>(() => c.Remove(null!));
        Assert.Throws<ArgumentNullException>(() => c.TryGet(null!, out NbtTag? _));
        Assert.Throws<ArgumentNullException>(() => c.GetTagType(null!));
        Assert.Throws<ArgumentNullException>(() => _ = c[null!]);
    }

    [Fact]
    public void Compound_IndexerSetter()
    {
        var c = new NbtCompound();
        c["k"] = new NbtInt(9);
        Assert.Equal(9, c.GetInt("k"));
        Assert.False(c.Equals("x"));
    }

    [Fact]
    public void Compound_NonGenericEnumerator()
    {
        var c = new NbtCompound();
        c.PutInt("a", 1);
        System.Collections.IEnumerable e = c;
        Assert.NotNull(e.GetEnumerator());
    }

    [Fact]
    public void FormatExceptions_MessageAndInner()
    {
        var inner = new InvalidOperationException("boom");
        var ex = new NbtFormatException("bad", inner);
        Assert.Equal("bad", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }
}
