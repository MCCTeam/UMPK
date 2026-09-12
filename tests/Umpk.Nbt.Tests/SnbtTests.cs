using Umpk.Nbt;
using Umpk.Nbt.Snbt;
using Xunit;

namespace Umpk.Nbt.Tests;

public sealed class SnbtTests
{
    [Fact]
    public void Printer_MatchesVanillaSuffixes()
    {
        Assert.Equal("5b", SnbtPrinter.Print(new NbtByte((sbyte)5)));
        Assert.Equal("5s", SnbtPrinter.Print(new NbtShort(5)));
        Assert.Equal("5", SnbtPrinter.Print(new NbtInt(5)));
        Assert.Equal("5L", SnbtPrinter.Print(new NbtLong(5)));
        Assert.Equal("1.5f", SnbtPrinter.Print(new NbtFloat(1.5f)));
        Assert.Equal("1.5d", SnbtPrinter.Print(new NbtDouble(1.5)));
    }

    [Fact]
    public void Printer_FloatAndDoubleAlwaysHaveDecimalPoint()
    {
        Assert.Equal("1.0f", SnbtPrinter.Print(new NbtFloat(1f)));
        Assert.Equal("2.0d", SnbtPrinter.Print(new NbtDouble(2d)));
    }

    [Fact]
    public void Printer_TypedArrays()
    {
        Assert.Equal("[B;1B,2B,3B]", SnbtPrinter.Print(new NbtByteArray([1, 2, 3])));
        Assert.Equal("[I;1,2,3]", SnbtPrinter.Print(new NbtIntArray([1, 2, 3])));
        Assert.Equal("[L;1L,2L]", SnbtPrinter.Print(new NbtLongArray([1, 2])));
    }

    [Fact]
    public void Printer_QuotesKeysAndStringsWhenNeeded()
    {
        var c = new NbtCompound();
        c.PutString("simple_key", "value with space");
        c.PutInt("needs quote", 1);
        string s = SnbtPrinter.Print(c);
        Assert.Contains("simple_key:\"value with space\"", s, StringComparison.Ordinal);
        Assert.Contains("\"needs quote\":1", s, StringComparison.Ordinal);
    }

    [Fact]
    public void Parser_ParsesEveryScalarType()
    {
        var c = SnbtParser.ParseCompound("{b:5b,s:6s,i:7,l:8L,f:1.5f,d:2.5d,str:hello,q:\"a b\"}");
        Assert.Equal((sbyte)5, ((NbtByte)c["b"]).Value);
        Assert.Equal((short)6, ((NbtShort)c["s"]).Value);
        Assert.Equal(7, ((NbtInt)c["i"]).Value);
        Assert.Equal(8L, ((NbtLong)c["l"]).Value);
        Assert.Equal(1.5f, ((NbtFloat)c["f"]).Value);
        Assert.Equal(2.5d, ((NbtDouble)c["d"]).Value);
        Assert.Equal("hello", ((NbtString)c["str"]).Value);
        Assert.Equal("a b", ((NbtString)c["q"]).Value);
    }

    [Fact]
    public void Parser_BooleanKeywordsBecomeBytes()
    {
        var c = SnbtParser.ParseCompound("{t:true,f:false}");
        Assert.Equal((sbyte)1, ((NbtByte)c["t"]).Value);
        Assert.Equal((sbyte)0, ((NbtByte)c["f"]).Value);
    }

    [Fact]
    public void Parser_TypedArrays()
    {
        var c = SnbtParser.ParseCompound("{ba:[B;1b,2b],ia:[I;1,2,3],la:[L;1L,2L]}");
        Assert.Equal(new sbyte[] { 1, 2 }, ((NbtByteArray)c["ba"]).Value);
        Assert.Equal(new[] { 1, 2, 3 }, ((NbtIntArray)c["ia"]).Value);
        Assert.Equal(new long[] { 1, 2 }, ((NbtLongArray)c["la"]).Value);
    }

    [Fact]
    public void Parser_Lists()
    {
        var c = SnbtParser.ParseCompound("{l:[1,2,3]}");
        var list = (NbtList)c["l"];
        Assert.Equal(3, list.Count);
        Assert.Equal(NbtTagType.Int, list.ElementType);
    }

    [Fact]
    public void Parser_MixedListRejected()
    {
        Assert.Throws<NbtFormatException>(() => SnbtParser.ParseCompound("{l:[1,\"two\"]}"));
    }

    [Fact]
    public void Parser_TrailingDataRejected()
    {
        Assert.Throws<NbtFormatException>(() => SnbtParser.ParseCompound("{a:1} extra"));
    }

    [Fact]
    public void Parser_EscapedQuotesInString()
    {
        var c = SnbtParser.ParseCompound("{s:\"he said \\\"hi\\\"\"}");
        Assert.Equal("he said \"hi\"", ((NbtString)c["s"]).Value);
    }

    [Fact]
    public void Parser_NestedStructure()
    {
        var c = SnbtParser.ParseCompound("{outer:{inner:[{id:1},{id:2}]}}");
        var inner = (NbtList)c.GetCompound("outer")!["inner"];
        Assert.Equal(2, inner.Count);
        Assert.Equal(2, ((NbtCompound)inner[1]).GetInt("id"));
    }

    [Theory]
    [InlineData("{a:1,b:2b,c:3.5d,d:\"text\",e:[1,2,3],f:[I;4,5,6]}")]
    [InlineData("{}")]
    [InlineData("{nested:{deep:{deeper:1}}}")]
    [InlineData("{arr:[B;-1b,127b],neg:-5,flt:-2.5f}")]
    public void Snbt_PrintThenParseRoundTrips(string snbt)
    {
        NbtCompound parsed = SnbtParser.ParseCompound(snbt);
        string printed = SnbtPrinter.Print(parsed);
        NbtCompound reparsed = SnbtParser.ParseCompound(printed);
        Assert.Equal(parsed, reparsed);
    }

    [Fact]
    public void ParseValue_AcceptsBareScalar()
    {
        Assert.Equal("bare", ((NbtString)SnbtParser.ParseValue("bare")).Value);
        Assert.Equal(42, ((NbtInt)SnbtParser.ParseValue("42")).Value);
    }

    [Fact]
    public void ToString_UsesSnbtPrinter()
    {
        Assert.Equal("5b", new NbtByte((sbyte)5).ToString());
    }
}
