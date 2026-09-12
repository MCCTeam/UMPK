using Umpk.Nbt;
using Umpk.Text;
using Umpk.Text.Serialization;
using Xunit;

namespace Umpk.Text.Tests;

/// <summary>The two <c>ComponentJson</c> fidelity residuals: the pre-1.20.3 KEY ORDER, and the <c>show_item</c> count. Every expectation is a hand-authored contract value, independent of this library's encoder, because an encoder cannot be its own oracle for a fidelity item.</summary>
/// <remarks>
/// <para>The pre-1.20.3 serializer has a stable, straight-line key order, so these tests pin it. From 1.20.3, codec composition can reorder keys when the style schema changes. Pinning that internal order per version would add no semantic value because readers treat object-key order as insignificant. See the note in <c>ComponentJson</c>.</para>
/// </remarks>
public sealed class ComponentJsonVanillaShapeTests
{
    private static readonly Style RedBoldInsertionFont = new()
    {
        Color = TextColor.Red,
        Bold = true,
        Insertion = "ins",
        Font = "minecraft:alt",
    };

    /// <summary>The style block, then <c>extra</c>, then the content. Verbatim 1.19 output for <c>literal-component construction.withStyle(color RED, bold, insertion "ins", font minecraft:alt)</c> with <c>literal-component construction</c> appended.</summary>
    [Fact]
    public void StyledLiteral_MatchesTheGsonKeyOrder()
    {
        var component = new Component(
            new TextContent("hello"), RedBoldInsertionFont, [Component.Text("child")]);

        Assert.Equal(
            "{\"bold\":true,\"color\":\"red\",\"insertion\":\"ins\",\"font\":\"minecraft:alt\","
            + "\"extra\":[{\"text\":\"child\"}],\"text\":\"hello\"}",
            ComponentJson.ToJsonString(component, ComponentWireEra.Legacy, ComponentJsonLiteralForm.Object));
    }

    /// <summary>The same order holds for translatable content, whose <c>translate</c>/<c>with</c> pair is written last as one block. Verbatim 1.19 output for <c>translatable-component construction("chat.type.text", literal("A"), literal("B")).withStyle(GREEN)</c> with <c>literal("x")</c> appended.</summary>
    [Fact]
    public void StyledTranslatable_MatchesTheGsonKeyOrder()
    {
        var component = new Component(
            new TranslatableContent("chat.type.text", null, [Component.Text("A"), Component.Text("B")]),
            new Style { Color = TextColor.Green },
            [Component.Text("x")]);

        Assert.Equal(
            "{\"color\":\"green\",\"extra\":[{\"text\":\"x\"}],\"translate\":\"chat.type.text\","
            + "\"with\":[{\"text\":\"A\"},{\"text\":\"B\"}]}",
            ComponentJson.ToJsonString(component, ComponentWireEra.Legacy, ComponentJsonLiteralForm.Object));
    }

    /// <summary>Insertion, then <c>clickEvent</c>, then <c>hoverEvent</c>, and the content after all three. The two events occur between insertion and font, making this more than a style/content split.</summary>
    [Fact]
    public void ClickAndHover_SitWhereGsonPutsThem()
    {
        var component = new Component(
            new TextContent("h"),
            new Style
            {
                Insertion = "zz",
                ClickEvent = new ClickEvent(ClickEventAction.RunCommand, "/say hi"),
                HoverEvent = new HoverShowText(Component.Text("tip")),
            });

        Assert.Equal(
            "{\"insertion\":\"zz\",\"clickEvent\":{\"action\":\"run_command\",\"value\":\"/say hi\"},"
            + "\"hoverEvent\":{\"action\":\"show_text\",\"contents\":{\"text\":\"tip\"}},\"text\":\"h\"}",
            ComponentJson.ToJsonString(component, ComponentWireEra.Legacy, ComponentJsonLiteralForm.Object));
    }

    /// <summary>The full ten-key style, so a key that is merely never exercised cannot hide in the order. The required sequence is bold, italic, underlined, strikethrough, obfuscated, color, insertion, clickEvent, hoverEvent, then font across the protocol 47-764 range.</summary>
    [Fact]
    public void EveryStyleKey_IsWrittenInTheGsonOrder()
    {
        var component = new Component(
            new TextContent("f"),
            new Style
            {
                Color = TextColor.Red,
                Bold = true,
                Italic = true,
                Underlined = true,
                Strikethrough = true,
                Obfuscated = true,
                Insertion = "i2",
                Font = "minecraft:alt",
                ClickEvent = new ClickEvent(ClickEventAction.OpenUrl, "http://x"),
                HoverEvent = new HoverShowText(Component.Text("t")),
            });

        Assert.Equal(
            "{\"bold\":true,\"italic\":true,\"underlined\":true,\"strikethrough\":true,\"obfuscated\":true,"
            + "\"color\":\"red\",\"insertion\":\"i2\","
            + "\"clickEvent\":{\"action\":\"open_url\",\"value\":\"http://x\"},"
            + "\"hoverEvent\":{\"action\":\"show_text\",\"contents\":{\"text\":\"t\"}},"
            + "\"font\":\"minecraft:alt\",\"text\":\"f\"}",
            ComponentJson.ToJsonString(component, ComponentWireEra.Legacy, ComponentJsonLiteralForm.Object));
    }

    /// <summary>A count of one is omitted on the pre-1.20.3 generation and present from 1.20.5. Both strings are independent wire-contract values. The two are asserted together because the point of the item is that they DIFFER.</summary>
    [Fact]
    public void ShowItemCount_IsOmittedOnlyOnTheGsonGeneration()
    {
        Component one = HoverItem(count: 1);
        Component five = HoverItem(count: 5);

        Assert.Equal(
            "{\"hoverEvent\":{\"action\":\"show_item\",\"contents\":{\"id\":\"minecraft:diamond\"}},\"text\":\"i\"}",
            ComponentJson.ToJsonString(one, ComponentWireEra.Legacy, ComponentJsonLiteralForm.Object));

        Assert.Equal(
            "{\"hoverEvent\":{\"action\":\"show_item\",\"contents\":{\"id\":\"minecraft:diamond\",\"count\":5}},\"text\":\"i\"}",
            ComponentJson.ToJsonString(five, ComponentWireEra.Legacy, ComponentJsonLiteralForm.Object));

        // 1.20.5 onward the count field is ItemStack.CODEC's fieldOf("count").orElse, which supplies a decode default only, so the encoder emits it for every value. Asserted as containment because the surrounding key order on that generation is deliberately not vanilla's.
        Assert.Contains(
            "\"count\":1",
            ComponentJson.ToJsonString(one, ComponentWireEra.Modern, ComponentJsonLiteralForm.Collapsed),
            StringComparison.Ordinal);
    }

    /// <summary>The omission is a width change, not a spelling change. Readers default an absent count to one. The byte-count assertion distinguishes the two accepted representations.</summary>
    [Fact]
    public void OmittedCount_IsNarrowerAndStillReadsBack()
    {
        Component one = HoverItem(count: 1);

        byte[] gson = ComponentJson.ToUtf8Bytes(one, ComponentWireEra.Legacy, ComponentJsonLiteralForm.Object);
        byte[] codec = ComponentJson.ToUtf8Bytes(one, ComponentWireEra.Legacy, ComponentJsonLiteralForm.Collapsed);

        Assert.Equal(codec.Length - "\"count\":1,".Length, gson.Length);
        Assert.Equal(one, ComponentJson.Parse(gson, ComponentWireEra.Legacy));
        Assert.Equal(one, ComponentJson.Parse(codec, ComponentWireEra.Legacy));
    }

    /// <summary>The order is a write-side axis only. Readers are key-driven on both generations, so a document in either order must parse to the same component.</summary>
    [Fact]
    public void BothOrders_ParseBackToTheSameComponent()
    {
        var component = new Component(
            new TextContent("hello"), RedBoldInsertionFont, [Component.Text("child")]);

        string gson = ComponentJson.ToJsonString(component, ComponentWireEra.Legacy, ComponentJsonLiteralForm.Object);
        string codec = ComponentJson.ToJsonString(component, ComponentWireEra.Legacy, ComponentJsonLiteralForm.Collapsed);

        Assert.NotEqual(gson, codec);
        Assert.Equal(component, ComponentJson.Parse(gson, ComponentWireEra.Legacy));
        Assert.Equal(component, ComponentJson.Parse(codec, ComponentWireEra.Legacy));
    }

    private static Component HoverItem(int count) =>
        new(
            new TextContent("i"),
            new Style { HoverEvent = new HoverShowItem("minecraft:diamond", count, (NbtTag?)null) });
}
