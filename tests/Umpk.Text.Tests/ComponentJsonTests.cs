using Umpk.Nbt;
using Umpk.Text;
using Umpk.Text.Serialization;
using Xunit;

namespace Umpk.Text.Tests;

public sealed class ComponentJsonTests
{
    private static Component RoundTrip(Component input, ComponentWireEra era)
    {
        string json = ComponentJson.ToJsonString(input, era);
        return ComponentJson.Parse(json, era);
    }

    [Fact]
    public void ParsesBareString()
    {
        Component c = ComponentJson.Parse("\"hello\"");
        Assert.Equal("hello", c.ToPlainText());
        Assert.True(c.Style.IsEmpty);
    }

    [Fact]
    public void CollapsesPlainTextToBareString()
    {
        Assert.Equal("\"hi\"", ComponentJson.ToJsonString(Component.Text("hi")));
    }

    [Fact]
    public void ParsesArrayFirstIsBase()
    {
        Component c = ComponentJson.Parse("[\"a\",\"b\",\"c\"]");
        Assert.Equal("abc", c.ToPlainText());
    }

    [Fact]
    public void RoundTripsNestedStyles()
    {
        var input = new Component(
            new TextContent("root"),
            new Style { Color = TextColor.Gold, Bold = true },
            [
                new Component(new TextContent("child"), new Style { Italic = true, Color = TextColor.FromRgb(0x123456) }),
            ]);

        foreach (ComponentWireEra era in new[] { ComponentWireEra.Legacy, ComponentWireEra.Modern })
        {
            Component result = RoundTrip(input, era);
            Assert.Equal(input, result);
        }
    }

    [Fact]
    public void RoundTripsAllStyleFlags()
    {
        var input = new Component(new TextContent("x"), new Style
        {
            Bold = true,
            Italic = false,
            Underlined = true,
            Strikethrough = false,
            Obfuscated = true,
            Insertion = "insert me",
            Font = "minecraft:uniform",
            Color = TextColor.Aqua,
        });
        Assert.Equal(input, RoundTrip(input, ComponentWireEra.Modern));
    }

    [Fact]
    public void RoundTripsTranslatableWithFallbackAndArgs()
    {
        var input = new Component(new TranslatableContent(
            "chat.type.text",
            "<%s> %s",
            [Component.Text("Steve"), Component.Text("hello")]));
        Assert.Equal(input, RoundTrip(input, ComponentWireEra.Modern));
    }

    [Fact]
    public void RoundTripsScoreSelectorKeybindNbt()
    {
        Component[] inputs =
        [
            new Component(new ScoreContent("Steve", "deaths")),
            new Component(new SelectorContent("@p", Component.Text(", "))),
            new Component(new KeybindContent("key.inventory")),
            new Component(new NbtContent("Health", true, null, NbtDataSource.Entity, "@s")),
        ];

        foreach (Component input in inputs)
            Assert.Equal(input, RoundTrip(input, ComponentWireEra.Modern));

    }

    [Fact]
    public void RoundTripsClickEventsModern()
    {
        ClickEvent[] clicks =
        [
            new ClickEvent(ClickEventAction.OpenUrl, "https://example.com"),
            new ClickEvent(ClickEventAction.RunCommand, "/say hi"),
            new ClickEvent(ClickEventAction.SuggestCommand, "/tp "),
            new ClickEvent(ClickEventAction.ChangePage, "5"),
            new ClickEvent(ClickEventAction.CopyToClipboard, "clip"),
            new ClickEvent(ClickEventAction.ShowDialog, "my:dialog"),
            new ClickEvent("my:event", new NbtCompound()),
        ];

        foreach (ClickEvent click in clicks)
        {
            var input = new Component(new TextContent("click"), new Style { ClickEvent = click });
            Assert.Equal(input, RoundTrip(input, ComponentWireEra.Modern));
        }
    }

    [Fact]
    public void RoundTripsClickEventsLegacy()
    {
        // Legacy form is a flat {action,value} pair; dialog/custom are 1.21.5+ only.
        ClickEvent[] clicks =
        [
            new ClickEvent(ClickEventAction.OpenUrl, "https://example.com"),
            new ClickEvent(ClickEventAction.RunCommand, "/say hi"),
            new ClickEvent(ClickEventAction.ChangePage, "3"),
            new ClickEvent(ClickEventAction.CopyToClipboard, "clip"),
        ];

        foreach (ClickEvent click in clicks)
        {
            var input = new Component(new TextContent("click"), new Style { ClickEvent = click });
            Assert.Equal(input, RoundTrip(input, ComponentWireEra.Legacy));
        }
    }

    [Fact]
    public void ParsesLegacyFlatClickEvent()
    {
        Component c = ComponentJson.Parse(
            "{\"text\":\"x\",\"clickEvent\":{\"action\":\"open_url\",\"value\":\"https://a.b\"}}",
            ComponentWireEra.Legacy);
        var click = Assert.IsType<ClickEvent>(c.Style.ClickEvent);
        Assert.Equal(ClickEventAction.OpenUrl, click.Action);
        Assert.Equal("https://a.b", click.Value);
    }

    [Fact]
    public void ParsesModernDispatchedClickEvent()
    {
        Component c = ComponentJson.Parse(
            "{\"text\":\"x\",\"click_event\":{\"action\":\"run_command\",\"command\":\"/help\"}}",
            ComponentWireEra.Modern);
        var click = Assert.IsType<ClickEvent>(c.Style.ClickEvent);
        Assert.Equal(ClickEventAction.RunCommand, click.Action);
        Assert.Equal("/help", click.Value);
    }

    [Fact]
    public void RoundTripsShowTextHover()
    {
        var input = new Component(new TextContent("hover"), new Style
        {
            HoverEvent = new HoverShowText(new Component(new TextContent("tip"), new Style { Color = TextColor.Yellow })),
        });
        Assert.Equal(input, RoundTrip(input, ComponentWireEra.Modern));
        Assert.Equal(input, RoundTrip(input, ComponentWireEra.Legacy));
    }

    [Fact]
    public void RoundTripsShowEntityHoverModern()
    {
        var input = new Component(new TextContent("e"), new Style
        {
            HoverEvent = new HoverShowEntity("minecraft:pig", Guid.NewGuid(), Component.Text("Babe")),
        });
        Assert.Equal(input, RoundTrip(input, ComponentWireEra.Modern));
    }

    [Fact]
    public void RoundTripsShowEntityHoverLegacy()
    {
        var input = new Component(new TextContent("e"), new Style
        {
            HoverEvent = new HoverShowEntity("minecraft:cow", Guid.NewGuid(), null),
        });
        Assert.Equal(input, RoundTrip(input, ComponentWireEra.Legacy));
    }

    [Fact]
    public void ShowEntityFieldNamesDifferByWireLayout()
    {
        var comp = new Component(new TextContent("e"), new Style
        {
            HoverEvent = new HoverShowEntity("minecraft:pig", Guid.Empty, null),
        });
        string modern = ComponentJson.ToJsonString(comp, ComponentWireEra.Modern);
        string legacy = ComponentJson.ToJsonString(comp, ComponentWireEra.Legacy);
        Assert.Contains("\"uuid\"", modern, StringComparison.Ordinal);
        Assert.Contains("\"contents\"", legacy, StringComparison.Ordinal);
        Assert.Contains("\"type\"", legacy, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTripsShowItemHover()
    {
        var data = new NbtCompound();
        data.PutInt("Damage", 5);
        var input = new Component(new TextContent("i"), new Style
        {
            HoverEvent = new HoverShowItem("minecraft:diamond_sword", 1, data),
        });
        Component modern = RoundTrip(input, ComponentWireEra.Modern);
        var hover = Assert.IsType<HoverShowItem>(modern.Style.HoverEvent);
        Assert.Equal("minecraft:diamond_sword", hover.ItemId);
        Assert.Equal(1, hover.Count);
        Assert.NotNull(hover.Data);
    }

    [Fact]
    public void ThrowsOnMalformedJson()
    {
        Assert.Throws<ComponentFormatException>(() => ComponentJson.Parse("{"));
        Assert.Throws<ComponentFormatException>(() => ComponentJson.Parse("[]"));
    }

    [Fact]
    public void ThrowsOnNullInput()
    {
        Assert.Throws<ArgumentNullException>(() => ComponentJson.Parse((string)null!));
        Assert.Throws<ArgumentNullException>(() => ComponentJson.ToJsonString(null!));
    }

    [Fact]
    public void ParsesUtf8ByteSpan()
    {
        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes("{\"text\":\"café\"}");
        Component c = ComponentJson.Parse(utf8);
        Assert.Equal("café", c.ToPlainText());
    }
}
