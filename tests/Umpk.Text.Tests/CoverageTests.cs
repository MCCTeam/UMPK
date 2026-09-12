using Umpk.Nbt;
using Umpk.Text;
using Umpk.Text.Serialization;
using Xunit;

namespace Umpk.Text.Tests;

// Additional targeted tests exercising edge paths across the serializers and model.
public sealed class CoverageTests
{
    [Fact]
    public void AllNamedColorStaticsRoundTrip()
    {
        TextColor[] colors =
        [
            TextColor.Black, TextColor.DarkBlue, TextColor.DarkGreen, TextColor.DarkAqua,
            TextColor.DarkRed, TextColor.DarkPurple, TextColor.Gold, TextColor.Gray,
            TextColor.DarkGray, TextColor.Blue, TextColor.Green, TextColor.Aqua,
            TextColor.Red, TextColor.LightPurple, TextColor.Yellow, TextColor.White,
        ];
        foreach (TextColor color in colors)
        {
            Assert.True(color.IsNamed);
            Assert.Equal(color, TextColor.Parse(color.Serialize()));
        }
    }

    [Fact]
    public void TextColorFromNameAndToStringAndHash()
    {
        Assert.Equal(TextColor.Gold, TextColor.FromName("gold"));
        Assert.Null(TextColor.FromName("nope"));
        Assert.Equal("gold", TextColor.Gold.ToString());
        Assert.Equal(TextColor.Gold.GetHashCode(), TextColor.FromRgb(0xFFAA00).GetHashCode());
        Assert.True(TextColor.Gold == TextColor.FromRgb(0xFFAA00));
        Assert.True(TextColor.Gold != TextColor.Red);
    }

    [Fact]
    public void LegacyParsesEveryColorCode()
    {
        (char Code, TextColor Color)[] table =
        [
            ('0', TextColor.Black), ('1', TextColor.DarkBlue), ('2', TextColor.DarkGreen),
            ('3', TextColor.DarkAqua), ('4', TextColor.DarkRed), ('5', TextColor.DarkPurple),
            ('6', TextColor.Gold), ('7', TextColor.Gray), ('8', TextColor.DarkGray),
            ('9', TextColor.Blue), ('a', TextColor.Green), ('b', TextColor.Aqua),
            ('c', TextColor.Red), ('d', TextColor.LightPurple), ('e', TextColor.Yellow),
            ('f', TextColor.White),
        ];
        foreach ((char code, TextColor color) in table)
        {
            Component c = LegacyText.Parse($"§{code}x");
            Assert.Equal(color, c.Style.Color);
        }
    }

    [Fact]
    public void LegacyParsesAllDecorationCodes()
    {
        Assert.True(LegacyText.Parse("§kx").Style.Obfuscated);
        Assert.True(LegacyText.Parse("§lx").Style.Bold);
        Assert.True(LegacyText.Parse("§mx").Style.Strikethrough);
        Assert.True(LegacyText.Parse("§nx").Style.Underlined);
        Assert.True(LegacyText.Parse("§ox").Style.Italic);
    }

    [Fact]
    public void LegacyEncodesAllDecorations()
    {
        var component = new Component(new TextContent("x"), new Style
        {
            Bold = true,
            Strikethrough = true,
            Underlined = true,
            Italic = true,
            Obfuscated = true,
        });
        string encoded = LegacyText.Encode(component);
        Assert.Contains("§l", encoded, StringComparison.Ordinal);
        Assert.Contains("§m", encoded, StringComparison.Ordinal);
        Assert.Contains("§n", encoded, StringComparison.Ordinal);
        Assert.Contains("§o", encoded, StringComparison.Ordinal);
        Assert.Contains("§k", encoded, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyEncodesChildrenWithInheritedStyle()
    {
        var component = new Component(
            new TextContent("a"),
            new Style { Color = TextColor.Red },
            [new Component(new TextContent("b"), new Style { Bold = true })]);
        string encoded = LegacyText.Encode(component);
        Assert.Equal("§ca§c§lb", encoded);
    }

    [Fact]
    public void LegacyEncodesSelectorKeybindNbtContent()
    {
        Assert.Equal("@a", LegacyText.Encode(new Component(new SelectorContent("@a", null))));
        Assert.Equal("key.jump", LegacyText.Encode(new Component(new KeybindContent("key.jump"))));
        Assert.Equal("Path", LegacyText.Encode(new Component(new NbtContent("Path", false, null, NbtDataSource.Block, "0 0 0"))));
    }

    [Fact]
    public void LegacyTrailingPrefixKeptVerbatim()
    {
        Component c = LegacyText.Parse("text§");
        Assert.Equal("text§", c.ToPlainText());
    }

    [Fact]
    public void NbtLegacyShowItemRoundTrips()
    {
        var data = new NbtCompound();
        data.PutInt("Damage", 2);
        var input = new Component(new TextContent("i"), new Style
        {
            HoverEvent = new HoverShowItem("minecraft:stone", 3, data),
        });
        NbtTag tag = ComponentNbt.To(input, ComponentWireEra.Legacy);
        Component back = ComponentNbt.From(tag, ComponentWireEra.Legacy);
        var hover = Assert.IsType<HoverShowItem>(back.Style.HoverEvent);
        Assert.Equal("minecraft:stone", hover.ItemId);
        Assert.Equal(3, hover.Count);
        Assert.Equal(data, hover.Data);
    }

    [Fact]
    public void NbtShowTextLegacyRoundTrips()
    {
        var input = new Component(new TextContent("t"), new Style
        {
            HoverEvent = new HoverShowText(Component.Text("tooltip")),
        });
        Assert.Equal(input, ComponentNbt.From(ComponentNbt.To(input, ComponentWireEra.Legacy), ComponentWireEra.Legacy));
    }

    [Fact]
    public void NbtClickEventLegacyRoundTrips()
    {
        var input = new Component(new TextContent("c"), new Style
        {
            ClickEvent = new ClickEvent(ClickEventAction.OpenUrl, "https://x.y"),
        });
        Assert.Equal(input, ComponentNbt.From(ComponentNbt.To(input, ComponentWireEra.Legacy), ComponentWireEra.Legacy));
    }

    [Fact]
    public void NbtCustomClickEventCarriesPayload()
    {
        var payload = new NbtCompound();
        payload.PutString("k", "v");
        var input = new Component(new TextContent("c"), new Style
        {
            ClickEvent = new ClickEvent("my:evt", payload),
        });
        Component back = ComponentNbt.From(ComponentNbt.To(input, ComponentWireEra.Modern), ComponentWireEra.Modern);
        var click = Assert.IsType<ClickEvent>(back.Style.ClickEvent);
        Assert.Equal("my:evt", click.Value);
        Assert.Equal(payload, click.Payload);
    }

    [Fact]
    public void NbtSelectorSeparatorRoundTrips()
    {
        var input = new Component(new SelectorContent("@a", Component.Text(" | ")));
        Assert.Equal(input, ComponentNbt.From(ComponentNbt.To(input), ComponentWireEra.Modern));
    }

    [Fact]
    public void NbtNumericTagDecodesToText()
    {
        Component c = ComponentNbt.From(new NbtInt(42));
        Assert.Equal("42", c.ToPlainText());
    }

    [Fact]
    public void NbtTextFieldFromNumericTag()
    {
        var compound = new NbtCompound();
        compound.PutInt("text", 7);
        Component c = ComponentNbt.From(compound);
        Assert.Equal("7", c.ToPlainText());
    }

    [Fact]
    public void JsonNumberAndBoolCoerceToText()
    {
        Assert.Equal("123", ComponentJson.Parse("123").ToPlainText());
        Assert.Equal("true", ComponentJson.Parse("true").ToPlainText());
        Assert.Equal("false", ComponentJson.Parse("false").ToPlainText());
    }

    [Fact]
    public void JsonSingleElementArrayIsThatElement()
    {
        Component c = ComponentJson.Parse("[\"solo\"]");
        Assert.Equal("solo", c.ToPlainText());
        Assert.Empty(c.Children);
    }

    [Fact]
    public void JsonOpenFileClickModernRoundTrips()
    {
        var input = new Component(new TextContent("f"), new Style
        {
            ClickEvent = new ClickEvent(ClickEventAction.OpenFile, "/tmp/x"),
        });
        string json = ComponentJson.ToJsonString(input, ComponentWireEra.Modern);
        Component back = ComponentJson.Parse(json, ComponentWireEra.Modern);
        var click = Assert.IsType<ClickEvent>(back.Style.ClickEvent);
        Assert.Equal(ClickEventAction.OpenFile, click.Action);
        Assert.Equal("/tmp/x", click.Value);
    }

    [Fact]
    public void JsonNbtContentInterpretAndSeparatorRoundTrip()
    {
        var input = new Component(new NbtContent("Items", true, Component.Text(","), NbtDataSource.Block, "1 2 3"));
        Assert.Equal(input, ComponentJson.Parse(ComponentJson.ToJsonString(input), ComponentWireEra.Modern));
    }

    [Fact]
    public void JsonStyledObjectWithNoContentKeyIsEmptyText()
    {
        Component c = ComponentJson.Parse("{\"bold\":true}");
        Assert.IsType<TextContent>(c.Content);
        Assert.Equal(string.Empty, c.ToPlainText());
        Assert.True(c.Style.Bold);
    }

    [Fact]
    public void TranslatableEqualityAndHashCode()
    {
        var a = new TranslatableContent("k", "fb", [Component.Text("x")]);
        var b = new TranslatableContent("k", "fb", [Component.Text("x")]);
        var c = new TranslatableContent("k", "fb", [Component.Text("y")]);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void ClickEventInequalityByPayload()
    {
        var a = new ClickEvent("id", new NbtString("a"));
        var b = new ClickEvent("id", new NbtString("b"));
        var c = new ClickEvent("id", (NbtTag?)null);
        Assert.NotEqual(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(a, new ClickEvent("id", new NbtString("a")));
    }

    [Fact]
    public void HoverShowItemEqualityAndHash()
    {
        var a = new HoverShowItem("minecraft:stone", 1, null);
        var b = new HoverShowItem("minecraft:stone", 1, null);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, new HoverShowItem("minecraft:dirt", 1, null));
    }

    [Fact]
    public void JsonBareItemIdShorthandContents()
    {
        // Legacy show_item where contents is a bare item id string.
        Component c = ComponentJson.Parse(
            "{\"text\":\"x\",\"hoverEvent\":{\"action\":\"show_item\",\"contents\":\"minecraft:apple\"}}",
            ComponentWireEra.Legacy);
        var hover = Assert.IsType<HoverShowItem>(c.Style.HoverEvent);
        Assert.Equal("minecraft:apple", hover.ItemId);
    }

    [Fact]
    public void UnknownClickActionThrows()
    {
        Assert.Throws<ComponentFormatException>(() => ComponentJson.Parse(
            "{\"text\":\"x\",\"clickEvent\":{\"action\":\"bogus\",\"value\":\"v\"}}",
            ComponentWireEra.Legacy));
    }

    [Fact]
    public void UnknownHoverActionThrows()
    {
        Assert.Throws<ComponentFormatException>(() => ComponentJson.Parse(
            "{\"text\":\"x\",\"hoverEvent\":{\"action\":\"bogus\"}}",
            ComponentWireEra.Legacy));
    }
}
