using Umpk.Nbt;
using Umpk.Nbt.Snbt;
using Umpk.Text;
using Umpk.Text.Serialization;
using Xunit;

namespace Umpk.Text.Tests;

/// <summary>Anchors the modern (1.21.5+) click/hover event field names to hand-authored literal JSON and SNBT expectations. The round-trip tests elsewhere feed production encode into production decode, so a symmetric field rename (for example <c>command</c> -&gt; <c>kommand</c> on both sides) is invisible to them; every expectation in this file is an independent literal, so such a regression fails here. The expected mappings are: open_url -&gt; url, open_file -&gt; path, run_command / suggest_command -&gt; command, change_page -&gt; page (int), copy_to_clipboard -&gt; value, show_dialog -&gt; dialog, custom -&gt; id + payload; show_text -&gt; value, show_item -&gt; id + count + components, show_entity -&gt; id + uuid + name.</summary>
public sealed class EventFieldNameCodecTests
{
    private static readonly Guid KnownGuid = new("00010203-0405-0607-0809-0a0b0c0d0e0f");

    private static Component Click(ClickEvent click) =>
        new(new TextContent("c"), new Style { ClickEvent = click });

    private static Component Hover(HoverEvent hover) =>
        new(new TextContent("h"), new Style { HoverEvent = hover });

    public static TheoryData<string, string> EncodeCases()
    {
        var data = new TheoryData<string, string>();
        // Literal SNBT of the modern-era NBT encoding, keyed by a serialized marker built below in EncodeCaseComponent. Both columns are hand-authored literals.
        data.Add("run_command", "{text:\"c\",click_event:{action:\"run_command\",command:\"/say hi\"}}");
        data.Add("suggest_command", "{text:\"c\",click_event:{action:\"suggest_command\",command:\"/tp \"}}");
        data.Add("open_url", "{text:\"c\",click_event:{action:\"open_url\",url:\"https://example.com\"}}");
        data.Add("open_file", "{text:\"c\",click_event:{action:\"open_file\",path:\"/tmp/f.txt\"}}");
        data.Add("change_page", "{text:\"c\",click_event:{action:\"change_page\",page:7}}");
        data.Add("copy_to_clipboard", "{text:\"c\",click_event:{action:\"copy_to_clipboard\",value:\"clip\"}}");
        data.Add("show_dialog", "{text:\"c\",click_event:{action:\"show_dialog\",dialog:\"my:dialog\"}}");
        data.Add("custom", "{text:\"c\",click_event:{action:\"custom\",id:\"my:event\",payload:{}}}");
        data.Add("show_text", "{text:\"h\",hover_event:{action:\"show_text\",value:\"tip\"}}");
        data.Add("show_item", "{text:\"h\",hover_event:{action:\"show_item\",id:\"minecraft:stone\",count:4}}");
        data.Add(
            "show_entity",
            "{text:\"h\",hover_event:{action:\"show_entity\",id:\"minecraft:pig\",uuid:[I;66051,67438087,134810123,202182159],name:\"Babe\"}}");
        return data;
    }

    private static Component EncodeCaseComponent(string name) => name switch
    {
        "run_command" => Click(new ClickEvent(ClickEventAction.RunCommand, "/say hi")),
        "suggest_command" => Click(new ClickEvent(ClickEventAction.SuggestCommand, "/tp ")),
        "open_url" => Click(new ClickEvent(ClickEventAction.OpenUrl, "https://example.com")),
        "open_file" => Click(new ClickEvent(ClickEventAction.OpenFile, "/tmp/f.txt")),
        "change_page" => Click(new ClickEvent(ClickEventAction.ChangePage, "7")),
        "copy_to_clipboard" => Click(new ClickEvent(ClickEventAction.CopyToClipboard, "clip")),
        "show_dialog" => Click(new ClickEvent(ClickEventAction.ShowDialog, "my:dialog")),
        "custom" => Click(new ClickEvent("my:event", new NbtCompound())),
        "show_text" => Hover(new HoverShowText(Component.Text("tip"))),
        "show_item" => Hover(new HoverShowItem("minecraft:stone", 4, null)),
        "show_entity" => Hover(new HoverShowEntity("minecraft:pig", KnownGuid, Component.Text("Babe"))),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(EncodeCases))]
    public void ModernNbtEncodingMatchesLiteralSnbt(string name, string expectedSnbt)
    {
        NbtTag tag = ComponentNbt.To(EncodeCaseComponent(name), ComponentWireEra.Modern);
        Assert.Equal(expectedSnbt, SnbtPrinter.Print(tag));
    }

    [Fact]
    public void ModernJsonEncodingMatchesLiteralJson()
    {
        // JSON exact-byte anchors for the two field families the mutation probe proved unanchored (command) plus one hover representative. show_entity uuid intentionally differs between JSON (string form) and NBT (int array), so anchor it separately.
        Assert.Equal(
            "{\"text\":\"c\",\"click_event\":{\"action\":\"run_command\",\"command\":\"/say hi\"}}",
            ComponentJson.ToJsonString(Click(new ClickEvent(ClickEventAction.RunCommand, "/say hi")), ComponentWireEra.Modern));
        Assert.Equal(
            "{\"text\":\"c\",\"click_event\":{\"action\":\"open_url\",\"url\":\"https://example.com\"}}",
            ComponentJson.ToJsonString(Click(new ClickEvent(ClickEventAction.OpenUrl, "https://example.com")), ComponentWireEra.Modern));
        Assert.Equal(
            "{\"text\":\"h\",\"hover_event\":{\"action\":\"show_item\",\"id\":\"minecraft:stone\",\"count\":4}}",
            ComponentJson.ToJsonString(Hover(new HoverShowItem("minecraft:stone", 4, null)), ComponentWireEra.Modern));
        Assert.Equal(
            "{\"text\":\"h\",\"hover_event\":{\"action\":\"show_entity\",\"id\":\"minecraft:pig\","
                + "\"uuid\":\"00010203-0405-0607-0809-0a0b0c0d0e0f\",\"name\":\"Babe\"}}",
            ComponentJson.ToJsonString(Hover(new HoverShowEntity("minecraft:pig", KnownGuid, Component.Text("Babe"))), ComponentWireEra.Modern));
    }

    [Fact]
    public void DecodesHandAuthoredModernClickJson()
    {
        // Decode-side anchors: literal vanilla-shaped JSON authored by hand (never produced by our encoder), so a decode-side rename cannot hide behind the encoder.
        var url = ComponentJson.Parse("{\"text\":\"x\",\"click_event\":{\"action\":\"open_url\",\"url\":\"https://a.b\"}}", ComponentWireEra.Modern);
        Assert.Equal(new ClickEvent(ClickEventAction.OpenUrl, "https://a.b"), url.Style.ClickEvent);

        var file = ComponentJson.Parse("{\"text\":\"x\",\"click_event\":{\"action\":\"open_file\",\"path\":\"/tmp/p\"}}", ComponentWireEra.Modern);
        Assert.Equal(new ClickEvent(ClickEventAction.OpenFile, "/tmp/p"), file.Style.ClickEvent);

        var run = ComponentJson.Parse("{\"text\":\"x\",\"click_event\":{\"action\":\"run_command\",\"command\":\"/help\"}}", ComponentWireEra.Modern);
        Assert.Equal(new ClickEvent(ClickEventAction.RunCommand, "/help"), run.Style.ClickEvent);

        var suggest = ComponentJson.Parse("{\"text\":\"x\",\"click_event\":{\"action\":\"suggest_command\",\"command\":\"/tp \"}}", ComponentWireEra.Modern);
        Assert.Equal(new ClickEvent(ClickEventAction.SuggestCommand, "/tp "), suggest.Style.ClickEvent);

        var page = ComponentJson.Parse("{\"text\":\"x\",\"click_event\":{\"action\":\"change_page\",\"page\":7}}", ComponentWireEra.Modern);
        Assert.Equal(new ClickEvent(ClickEventAction.ChangePage, "7"), page.Style.ClickEvent);

        var dialog = ComponentJson.Parse("{\"text\":\"x\",\"click_event\":{\"action\":\"show_dialog\",\"dialog\":\"my:d\"}}", ComponentWireEra.Modern);
        Assert.Equal(new ClickEvent(ClickEventAction.ShowDialog, "my:d"), dialog.Style.ClickEvent);
    }

    [Fact]
    public void DecodesHandAuthoredModernClickNbt()
    {
        var custom = new NbtCompound();
        custom.PutString("action", "custom");
        custom.PutString("id", "my:event");
        var payload = new NbtCompound();
        payload.PutInt("n", 5);
        custom.Put("payload", payload);
        var root = new NbtCompound();
        root.PutString("text", "x");
        root.Put("click_event", custom);

        Component c = ComponentNbt.From(root, ComponentWireEra.Modern);
        var click = Assert.IsType<ClickEvent>(c.Style.ClickEvent);
        Assert.Equal(ClickEventAction.Custom, click.Action);
        Assert.Equal("my:event", click.Value);
        Assert.Equal(payload, click.Payload);

        var run = new NbtCompound();
        run.PutString("action", "run_command");
        run.PutString("command", "/say hi");
        var runRoot = new NbtCompound();
        runRoot.PutString("text", "x");
        runRoot.Put("click_event", run);
        Assert.Equal(
            new ClickEvent(ClickEventAction.RunCommand, "/say hi"),
            ComponentNbt.From(runRoot, ComponentWireEra.Modern).Style.ClickEvent);
    }

    [Fact]
    public void DecodesHandAuthoredModernHoverNbt()
    {
        var item = new NbtCompound();
        item.PutString("action", "show_item");
        item.PutString("id", "minecraft:stone");
        item.PutInt("count", 4);
        var components = new NbtCompound();
        components.PutInt("minecraft:damage", 3);
        item.Put("components", components);
        var itemRoot = new NbtCompound();
        itemRoot.PutString("text", "x");
        itemRoot.Put("hover_event", item);

        var showItem = Assert.IsType<HoverShowItem>(
            ComponentNbt.From(itemRoot, ComponentWireEra.Modern).Style.HoverEvent);
        Assert.Equal("minecraft:stone", showItem.ItemId);
        Assert.Equal(4, showItem.Count);
        Assert.Equal(components, showItem.Data);

        var entity = new NbtCompound();
        entity.PutString("action", "show_entity");
        entity.PutString("id", "minecraft:pig");
        entity.Put("uuid", new NbtIntArray([66051, 67438087, 134810123, 202182159]));
        var entityRoot = new NbtCompound();
        entityRoot.PutString("text", "x");
        entityRoot.Put("hover_event", entity);

        var showEntity = Assert.IsType<HoverShowEntity>(
            ComponentNbt.From(entityRoot, ComponentWireEra.Modern).Style.HoverEvent);
        Assert.Equal("minecraft:pig", showEntity.EntityType);
        Assert.Equal(KnownGuid, showEntity.Id);
    }
}
