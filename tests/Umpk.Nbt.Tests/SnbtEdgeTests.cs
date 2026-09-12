using Umpk.Nbt;
using Umpk.Nbt.Snbt;
using Xunit;

namespace Umpk.Nbt.Tests;

public sealed class SnbtEdgeTests
{
    [Fact]
    public void Printer_EndTag()
    {
        Assert.Equal("END", SnbtPrinter.Print(NbtEnd.Instance));
    }

    [Fact]
    public void Printer_EmptyArraysAndList()
    {
        Assert.Equal("[B;]", SnbtPrinter.Print(new NbtByteArray([])));
        Assert.Equal("[I;]", SnbtPrinter.Print(new NbtIntArray([])));
        Assert.Equal("[L;]", SnbtPrinter.Print(new NbtLongArray([])));
        Assert.Equal("[]", SnbtPrinter.Print(new NbtList()));
        Assert.Equal("{}", SnbtPrinter.Print(new NbtCompound()));
    }

    [Fact]
    public void Printer_NonFiniteFloatsAndDoubles()
    {
        Assert.Equal("NaNf", SnbtPrinter.Print(new NbtFloat(float.NaN)));
        Assert.Equal("Infinityf", SnbtPrinter.Print(new NbtFloat(float.PositiveInfinity)));
        Assert.Equal("-Infinityf", SnbtPrinter.Print(new NbtFloat(float.NegativeInfinity)));
        Assert.Equal("NaNd", SnbtPrinter.Print(new NbtDouble(double.NaN)));
        Assert.Equal("Infinityd", SnbtPrinter.Print(new NbtDouble(double.PositiveInfinity)));
        Assert.Equal("-Infinityd", SnbtPrinter.Print(new NbtDouble(double.NegativeInfinity)));
    }

    [Fact]
    public void Printer_ScientificNotationKeepsNoExtraPoint()
    {
        // A value whose shortest round-trip form uses 'E' must not get a spurious ".0".
        string printed = SnbtPrinter.Print(new NbtDouble(1e30));
        Assert.EndsWith("d", printed, StringComparison.Ordinal);
        Assert.DoesNotContain(".0d", printed, StringComparison.Ordinal);
    }

    [Fact]
    public void Printer_NullRejected()
    {
        Assert.Throws<ArgumentNullException>(() => SnbtPrinter.Print(null!));
    }

    [Fact]
    public void Printer_SwitchesQuoteToAvoidEscaping()
    {
        // Contains a double quote but no single quote: printer should wrap in single quotes.
        var s = new NbtString("say \"hi\"");
        Assert.Equal("'say \"hi\"'", SnbtPrinter.Print(s));
    }

    [Fact]
    public void Parser_ValueRootList()
    {
        var list = (NbtList)SnbtParser.ParseValue("[1,2,3]");
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void Parser_ValueRootArray()
    {
        var arr = (NbtIntArray)SnbtParser.ParseValue("[I;7,8]");
        Assert.Equal(new[] { 7, 8 }, arr.Value);
    }

    [Fact]
    public void Parser_NullRejected()
    {
        Assert.Throws<ArgumentNullException>(() => SnbtParser.ParseCompound(null!));
        Assert.Throws<ArgumentNullException>(() => SnbtParser.ParseValue(null!));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{a}")]
    [InlineData("{a:}")]
    [InlineData("{:1}")]
    [InlineData("[1,2")]
    [InlineData("[X;1]")]
    [InlineData("{a:[B;1]}")]  // 1 is int, not byte -> mixed array
    public void Parser_MalformedSnbtRejected(string snbt)
    {
        Assert.Throws<NbtFormatException>(() => SnbtParser.ParseCompound(snbt));
    }

    [Fact]
    public void Parser_WhitespaceTolerated()
    {
        var c = SnbtParser.ParseCompound("  {  a : 1 , b : 2  }  ");
        Assert.Equal(1, c.GetInt("a"));
        Assert.Equal(2, c.GetInt("b"));
    }

    [Fact]
    public void Parser_LargeNumbersFallThroughToString()
    {
        // A digit run too large for a long, no suffix, no decimal: vanilla treats it as a string.
        var c = SnbtParser.ParseCompound("{x:99999999999999999999999999}");
        Assert.IsType<NbtString>(c["x"]);
    }

    [Fact]
    public void GetStringAndGetBool_WrongTypeReturnsDefault()
    {
        var c = new NbtCompound();
        c.PutInt("i", 1);
        Assert.Equal(string.Empty, c.GetString("i"));  // not a string
        c.PutByte("flag", 1);
        Assert.True(c.GetBool("flag"));
        c.PutByte("zero", 0);
        Assert.False(c.GetBool("zero"));
    }
}
