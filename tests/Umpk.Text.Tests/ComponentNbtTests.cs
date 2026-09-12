using Umpk.Nbt;
using Umpk.Text;
using Umpk.Text.Serialization;
using Xunit;

namespace Umpk.Text.Tests;

public sealed class ComponentNbtTests
{
    private static Component RoundTrip(Component input, ComponentWireEra era)
    {
        NbtTag tag = ComponentNbt.To(input, era);
        return ComponentNbt.From(tag, era);
    }

    [Fact]
    public void PlainTextCollapsesToStringTag()
    {
        NbtTag tag = ComponentNbt.To(Component.Text("hi"));
        NbtString str = Assert.IsType<NbtString>(tag);
        Assert.Equal("hi", str.Value);
    }

    [Fact]
    public void BareStringTagDecodesToText()
    {
        Component c = ComponentNbt.From(new NbtString("hello"));
        Assert.Equal("hello", c.ToPlainText());
    }

    [Fact]
    public void ListTagDecodesWithFirstAsBase()
    {
        var list = new NbtList();
        list.Add(new NbtString("a"));
        list.Add(new NbtString("b"));
        Component c = ComponentNbt.From(list);
        Assert.Equal("ab", c.ToPlainText());
    }

    [Fact]
    public void RoundTripsNestedStyles()
    {
        var input = new Component(
            new TextContent("root"),
            new Style { Color = TextColor.Green, Bold = true, Italic = false },
            [new Component(new TextContent("kid"), new Style { Underlined = true })]);

        foreach (ComponentWireEra era in new[] { ComponentWireEra.Legacy, ComponentWireEra.Modern })
            Assert.Equal(input, RoundTrip(input, era));

    }

    [Fact]
    public void RoundTripsTranslatable()
    {
        var input = new Component(new TranslatableContent(
            "chat.type.text",
            "<%s> %s",
            [Component.Text("Steve"), Component.Text("hi")]));
        Assert.Equal(input, RoundTrip(input, ComponentWireEra.Modern));
    }

    [Fact]
    public void RoundTripsScoreSelectorKeybindNbt()
    {
        Component[] inputs =
        [
            new Component(new ScoreContent("Steve", "kills")),
            new Component(new SelectorContent("@e", Component.Text("; "))),
            new Component(new KeybindContent("key.drop")),
            new Component(new NbtContent("Pos", false, null, NbtDataSource.Storage, "my:store")),
        ];

        foreach (Component input in inputs)
            Assert.Equal(input, RoundTrip(input, ComponentWireEra.Modern));

    }

    [Fact]
    public void RoundTripsClickEventModern()
    {
        var input = new Component(new TextContent("c"), new Style
        {
            ClickEvent = new ClickEvent(ClickEventAction.RunCommand, "/say hi"),
        });
        Assert.Equal(input, RoundTrip(input, ComponentWireEra.Modern));
    }

    [Fact]
    public void RoundTripsClickEventChangePageAsInt()
    {
        var input = new Component(new TextContent("c"), new Style
        {
            ClickEvent = new ClickEvent(ClickEventAction.ChangePage, "7"),
        });
        NbtTag tag = ComponentNbt.To(input, ComponentWireEra.Modern);
        var compound = Assert.IsType<NbtCompound>(tag);
        NbtCompound click = compound.GetCompound("click_event")!;
        Assert.IsType<NbtInt>(click["page"]);
        Assert.Equal(input, RoundTrip(input, ComponentWireEra.Modern));
    }

    [Fact]
    public void RoundTripsShowEntityHoverBothWireLayouts()
    {
        var input = new Component(new TextContent("e"), new Style
        {
            HoverEvent = new HoverShowEntity("minecraft:pig", Guid.NewGuid(), Component.Text("Babe")),
        });
        Assert.Equal(input, RoundTrip(input, ComponentWireEra.Modern));
        Assert.Equal(input, RoundTrip(input, ComponentWireEra.Legacy));
    }

    [Fact]
    public void ShowEntityUuidIsIntArrayInNbt()
    {
        var guid = Guid.NewGuid();
        var input = new Component(new TextContent("e"), new Style
        {
            HoverEvent = new HoverShowEntity("minecraft:pig", guid, null),
        });
        NbtTag tag = ComponentNbt.To(input, ComponentWireEra.Modern);
        var compound = Assert.IsType<NbtCompound>(tag);
        NbtCompound hover = compound.GetCompound("hover_event")!;
        var arr = Assert.IsType<NbtIntArray>(hover["uuid"]);
        Assert.Equal(4, arr.Value.Length);

        // Round-trip preserves the exact UUID.
        Component back = ComponentNbt.From(tag, ComponentWireEra.Modern);
        var hoverBack = Assert.IsType<HoverShowEntity>(back.Style.HoverEvent);
        Assert.Equal(guid, hoverBack.Id);
    }

    [Fact]
    public void RoundTripsShowItemHover()
    {
        var data = new NbtCompound();
        data.PutInt("Damage", 3);
        var input = new Component(new TextContent("i"), new Style
        {
            HoverEvent = new HoverShowItem("minecraft:stone", 4, data),
        });
        Component result = RoundTrip(input, ComponentWireEra.Modern);
        var hover = Assert.IsType<HoverShowItem>(result.Style.HoverEvent);
        Assert.Equal("minecraft:stone", hover.ItemId);
        Assert.Equal(4, hover.Count);
        Assert.Equal(data, hover.Data);
    }

    [Fact]
    public void StyleBooleansAreByteTags()
    {
        var input = new Component(new TextContent("x"), new Style { Bold = true, Italic = false });
        var compound = Assert.IsType<NbtCompound>(ComponentNbt.To(input));
        Assert.IsType<NbtByte>(compound["bold"]);
        Assert.Equal((sbyte)1, ((NbtByte)compound["bold"]).Value);
        Assert.Equal((sbyte)0, ((NbtByte)compound["italic"]).Value);
    }

    /// <summary>A bare string tag is literal content in every era, even when its text resembles JSON. Brackets, braces, and complete JSON documents must remain unchanged.</summary>
    [Theory]
    [InlineData("[Admin] ")]
    [InlineData("[")]
    [InlineData("{")]
    [InlineData("{\"not\":\"json\"")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("[Owner] \u00a7cSteve")]
    [InlineData("  [leading space] ")]
    [InlineData("")]
    [InlineData("just text")]
    public void BareStringTagIsAlwaysLiteral(string value)
    {
        foreach (ComponentWireEra era in new[] { ComponentWireEra.Legacy, ComponentWireEra.Modern })
        {
            Component c = ComponentNbt.From(new NbtString(value), era);
            Assert.Equal(value, c.ToPlainText());
            Assert.Equal(new TextContent(value), c.Content);
            Assert.True(c.Style.IsEmpty);
            Assert.Empty(c.Children);
        }
    }

    /// <summary>The hostile case that is valid JSON and meant literally. A server sending the characters <c>{"text":"x","bold":true}</c> as a player's display name means those characters, and the vanilla client shows them; it does not render a bold <c>x</c>.</summary>
    [Fact]
    public void JsonShapedLiteralIsNotReinterpreted()
    {
        Component c = ComponentNbt.From(new NbtString("{\"text\":\"x\",\"bold\":true}"), ComponentWireEra.Modern);
        Assert.Equal("{\"text\":\"x\",\"bold\":true}", c.ToPlainText());
        Assert.Null(c.Style.Bold);
    }

    /// <summary>The same literal values survive an encode/decode round trip, so a bracket-prefixed team prefix that arrives on the wire can also be re-emitted (the byte-exact conformance gate depends on it).</summary>
    [Theory]
    [InlineData("[Admin] ")]
    [InlineData("{\"text\":\"x\"}")]
    [InlineData("[")]
    [InlineData("")]
    public void JsonShapedLiteralRoundTrips(string value)
    {
        Component input = Component.Text(value);
        NbtTag tag = ComponentNbt.To(input, ComponentWireEra.Modern);
        Assert.Equal(value, Assert.IsType<NbtString>(tag).Value);
        Assert.Equal(input, ComponentNbt.From(tag, ComponentWireEra.Modern));
    }

    /// <summary>Bracket-shaped literals remain literal at every recursive position, not only at the root.</summary>
    [Fact]
    public void NestedJsonShapedLiteralsAreLiteral()
    {
        var root = new NbtCompound();
        root.PutString("text", "[Admin] ");
        var extra = new NbtList();
        extra.Add(new NbtString("[Mod] "));
        root.Put("extra", extra);

        var hover = new NbtCompound();
        hover.PutString("action", "show_text");
        hover.Put("value", new NbtString("{not json"));
        root.Put("hover_event", hover);

        Component c = ComponentNbt.From(root, ComponentWireEra.Modern);
        Assert.Equal("[Admin] [Mod] ", c.ToPlainText());
        var shown = Assert.IsType<HoverShowText>(c.Style.HoverEvent);
        Assert.Equal("{not json", shown.Text.ToPlainText());
    }

    [Fact]
    public void ThrowsOnNullAndBadTags()
    {
        Assert.Throws<ArgumentNullException>(() => ComponentNbt.From(null!));
        Assert.Throws<ArgumentNullException>(() => ComponentNbt.To(null!));
        Assert.Throws<ComponentFormatException>(() => ComponentNbt.From(new NbtList()));
    }
}
