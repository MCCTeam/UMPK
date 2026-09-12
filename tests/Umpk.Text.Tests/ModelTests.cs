using Umpk.Text;
using Xunit;

namespace Umpk.Text.Tests;

public sealed class ModelTests
{
    [Fact]
    public void TextColor_NamedRoundTrips()
    {
        TextColor red = TextColor.Red;
        Assert.True(red.IsNamed);
        Assert.Equal("red", red.Name);
        Assert.Equal(0xFF5555, red.Rgb);
        Assert.Equal("red", red.Serialize());
        Assert.Equal(red, TextColor.Parse("red"));
    }

    [Fact]
    public void TextColor_HexRoundTrips()
    {
        TextColor color = TextColor.FromRgb(0x1A2B3C);
        Assert.False(color.IsNamed);
        Assert.Equal("#1A2B3C", color.Serialize());
        Assert.Equal(color, TextColor.Parse("#1a2b3c"));
    }

    [Theory]
    [InlineData("notacolor")]
    [InlineData("#12345")]
    [InlineData("#gggggg")]
    [InlineData("#1234567")]
    public void TextColor_ParseRejectsInvalid(string value)
    {
        Assert.Null(TextColor.Parse(value));
    }

    [Fact]
    public void TextColor_EqualityIgnoresName()
    {
        // Vanilla TextColor equality is on RGB value only.
        Assert.Equal(TextColor.FromRgb(0xFF5555), TextColor.Red);
    }

    [Fact]
    public void Style_ApplyToInheritsUnsetFields()
    {
        var parent = new Style { Color = TextColor.Red, Bold = true };
        var child = new Style { Italic = true };
        Style merged = child.ApplyTo(parent);
        Assert.Equal(TextColor.Red, merged.Color);
        Assert.True(merged.Bold);
        Assert.True(merged.Italic);
    }

    [Fact]
    public void Style_ApplyToChildWins()
    {
        var parent = new Style { Color = TextColor.Red };
        var child = new Style { Color = TextColor.Blue };
        Assert.Equal(TextColor.Blue, child.ApplyTo(parent).Color);
    }

    [Fact]
    public void Style_EmptyIsEmpty()
    {
        Assert.True(Style.Empty.IsEmpty);
        Assert.False(new Style { Bold = false }.IsEmpty);
    }

    [Fact]
    public void Component_FactoriesProduceExpectedContent()
    {
        Component text = Component.Text("hi");
        Assert.IsType<TextContent>(text.Content);
        Assert.Equal("hi", ((TextContent)text.Content).Text);

        Component tr = Component.Translatable("key", Component.Text("a"));
        var content = Assert.IsType<TranslatableContent>(tr.Content);
        Assert.Equal("key", content.Key);
        Assert.Single(content.Args);
    }

    [Fact]
    public void Component_RecordEqualityIsStructural()
    {
        Component a = Component.Text("x");
        Component b = Component.Text("x");
        Assert.Equal(a, b);

        var styledA = new Component(new TextContent("x"), new Style { Bold = true });
        var styledB = new Component(new TextContent("x"), new Style { Bold = true });
        Assert.Equal(styledA, styledB);
        Assert.NotEqual(a, styledA);
    }

    [Fact]
    public void ClickEvent_CustomCarriesPayload()
    {
        var payload = new Umpk.Nbt.NbtString("data");
        var click = new ClickEvent("my:id", payload);
        Assert.Equal(ClickEventAction.Custom, click.Action);
        Assert.Equal("my:id", click.Value);
        Assert.Equal(payload, click.Payload);
    }

    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => Component.Text(null!));
        Assert.Throws<ArgumentNullException>(() => new TextContent(null!));
        Assert.Throws<ArgumentNullException>(() => Style.Empty.ApplyTo(null!));
    }
}
