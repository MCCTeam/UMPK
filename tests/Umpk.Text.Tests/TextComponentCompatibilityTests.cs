using Umpk.Text;
using Umpk.Text.Serialization;
using Xunit;

namespace Umpk.Text.Tests;

public sealed class TextComponentCompatibilityTests
{
    [Fact]
    public void ParsesPlainText()
    {
        Component c = LegacyText.Parse("just text");
        Assert.Equal("just text", c.ToPlainText());
    }

    [Fact]
    public void ParsesColorCode()
    {
        Component c = LegacyText.Parse("§cred text");
        Assert.Equal("red text", c.ToPlainText());
        Assert.Equal(TextColor.Red, c.Style.Color);
    }

    [Fact]
    public void ParsesDecorationCodes()
    {
        Component c = LegacyText.Parse("§lbold §oand italic");
        // Two runs: "bold " (bold), then "and italic" is bold+italic because §o accumulates.
        Assert.Equal("bold and italic", c.ToPlainText());
    }

    [Fact]
    public void ColorCodeResetsDecorations()
    {
        // Vanilla: a color code clears prior decorations.
        Component c = LegacyText.Parse("§lbold§cred");
        Assert.Equal(2, c.Children.Count);
        Assert.True(c.Children[0].Style.Bold);
        Assert.Equal(TextColor.Red, c.Children[1].Style.Color);
        Assert.Null(c.Children[1].Style.Bold);
    }

    [Fact]
    public void ParsesHexColorExtension()
    {
        Component c = LegacyText.Parse("§#ff8800orange");
        Assert.Equal("orange", c.ToPlainText());
        Assert.Equal(TextColor.FromRgb(0xFF8800), c.Style.Color);
    }

    [Fact]
    public void ParsesResetCode()
    {
        Component c = LegacyText.Parse("§c§lstyled§rplain");
        Assert.Equal("styledplain", c.ToPlainText());
        Component plainRun = c.Children[^1];
        Assert.Null(plainRun.Style.Color);
        Assert.Null(plainRun.Style.Bold);
    }

    [Fact]
    public void KeepsUnknownCodesVerbatim()
    {
        Component c = LegacyText.Parse("§znot a code");
        Assert.Equal("§znot a code", c.ToPlainText());
    }

    [Fact]
    public void EncodesNamedColorAndDecorations()
    {
        var component = new Component(new TextContent("hi"), new Style { Color = TextColor.Gold, Bold = true });
        string encoded = LegacyText.Encode(component);
        Assert.Equal("§6§lhi", encoded);
    }

    [Fact]
    public void EncodesHexColor()
    {
        var component = new Component(new TextContent("x"), new Style { Color = TextColor.FromRgb(0x1A2B3C) });
        Assert.Equal("§#1a2b3cx", LegacyText.Encode(component));
    }

    [Fact]
    public void EncodeRoundTripsColorThroughParse()
    {
        var component = new Component(new TextContent("hello"), new Style { Color = TextColor.Aqua });
        string encoded = LegacyText.Encode(component);
        Component reparsed = LegacyText.Parse(encoded);
        Assert.Equal("hello", reparsed.ToPlainText());
        Assert.Equal(TextColor.Aqua, reparsed.Style.Color);
    }

    [Fact]
    public void EncodeHexRoundTripsThroughParse()
    {
        var component = new Component(new TextContent("x"), new Style { Color = TextColor.FromRgb(0xABCDEF) });
        Component reparsed = LegacyText.Parse(LegacyText.Encode(component));
        Assert.Equal(TextColor.FromRgb(0xABCDEF), reparsed.Style.Color);
    }

    [Fact]
    public void EncodeIsLossyForNonTextContent()
    {
        // Translatable falls back to its fallback string; events are dropped.
        var component = new Component(
            new TranslatableContent("k", "fallback text", []),
            new Style { ClickEvent = new ClickEvent(ClickEventAction.RunCommand, "/x") });
        Assert.Equal("fallback text", LegacyText.Encode(component));
    }

    [Fact]
    public void EmptyStringYieldsEmptyComponent()
    {
        Component c = LegacyText.Parse("");
        Assert.Equal(string.Empty, c.ToPlainText());
    }

    [Fact]
    public void ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => LegacyText.Parse(null!));
        Assert.Throws<ArgumentNullException>(() => LegacyText.Encode(null!));
    }
}
