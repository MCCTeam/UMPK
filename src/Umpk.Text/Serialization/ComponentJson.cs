using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Umpk.Nbt;
using Umpk.Nbt.Snbt;

namespace Umpk.Text.Serialization;

/// <summary>Encodes and decodes <see cref="Component"/> trees to and from the JSON wire form used before 1.20.3 (and still accepted afterwards). A component is a bare string (plain text), a non-empty array (the first element is the base, the rest are siblings), or an object whose content type is inferred from its keys (<c>text</c>, <c>translate</c>, <c>score</c>, <c>selector</c>, <c>keybind</c>, <c>nbt</c>). Implemented with <see cref="Utf8JsonReader"/> / <see cref="Utf8JsonWriter"/> (no reflection).</summary>
public static class ComponentJson
{
    /// <summary>Parses a component from UTF-8 JSON using the modern wire era.</summary>
    /// <exception cref="ComponentFormatException">The JSON is not a valid component.</exception>
    public static Component Parse(ReadOnlySpan<byte> utf8Json) => Parse(utf8Json, ComponentWireEra.Modern);

    /// <summary>Parses a component from UTF-8 JSON using the given wire era.</summary>
    /// <exception cref="ComponentFormatException">The JSON is not a valid component.</exception>
    public static Component Parse(ReadOnlySpan<byte> utf8Json, ComponentWireEra era)
    {
        try
        {
            var reader = new Utf8JsonReader(utf8Json, new JsonReaderOptions { AllowTrailingCommas = false });
            if (!reader.Read())
                throw new ComponentFormatException("Empty JSON input for component");

            Component result = ReadComponent(ref reader, era);
            if (reader.Read())
                throw new ComponentFormatException("Trailing JSON data after component");

            return result;
        }
        catch (JsonException ex)
        {
            throw new ComponentFormatException("Malformed component JSON", ex);
        }
    }

    /// <summary>Parses a component from a JSON string using the given wire era.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
    /// <exception cref="ComponentFormatException">The JSON is not a valid component.</exception>
    public static Component Parse(string json, ComponentWireEra era)
    {
        ArgumentNullException.ThrowIfNull(json);
        return Parse(Encoding.UTF8.GetBytes(json), era);
    }

    /// <summary>Parses a component from a JSON string using the modern wire era.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
    /// <exception cref="ComponentFormatException">The JSON is not a valid component.</exception>
    public static Component Parse(string json) => Parse(json, ComponentWireEra.Modern);

    /// <summary>Encodes a component to a UTF-8 JSON byte array using the given wire era (default modern).</summary>
    /// <param name="component">The component.</param>
    /// <param name="era">The click/hover interaction dialect (the 1.21.5 axis).</param>
    /// <param name="literalForm">How a pure literal is written (the 1.20.3 axis). Protocols 764 and below need <see cref="ComponentJsonLiteralForm.Object"/>; see that type for the vanilla anchors.</param>
    /// <returns>The UTF-8 JSON.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="component"/> is null.</exception>
    public static byte[] ToUtf8Bytes(
        Component component,
        ComponentWireEra era = ComponentWireEra.Modern,
        ComponentJsonLiteralForm literalForm = ComponentJsonLiteralForm.Collapsed)
    {
        ArgumentNullException.ThrowIfNull(component);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
            WriteComponent(writer, component, era, literalForm);

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Encodes a component to a JSON string using the given wire era (default modern).</summary>
    /// <param name="component">The component.</param>
    /// <param name="era">The click/hover interaction dialect (the 1.21.5 axis).</param>
    /// <param name="literalForm">How a pure literal is written (the 1.20.3 axis). Protocols 764 and below need <see cref="ComponentJsonLiteralForm.Object"/>; see that type for the vanilla anchors.</param>
    /// <returns>The JSON string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="component"/> is null.</exception>
    public static string ToJsonString(
        Component component,
        ComponentWireEra era = ComponentWireEra.Modern,
        ComponentJsonLiteralForm literalForm = ComponentJsonLiteralForm.Collapsed) =>
        Encoding.UTF8.GetString(ToUtf8Bytes(component, era, literalForm));

    // decode

    private static Component ReadComponent(ref Utf8JsonReader reader, ComponentWireEra era)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return Component.Text(reader.GetString()!);

            case JsonTokenType.True:
                return Component.Text("true");

            case JsonTokenType.False:
                return Component.Text("false");

            case JsonTokenType.Number:
                return Component.Text(ReadRawNumber(ref reader));

            case JsonTokenType.StartArray:
                return ReadArrayComponent(ref reader, era);

            case JsonTokenType.StartObject:
                return ReadObjectComponent(ref reader, era);

            default:
                throw new ComponentFormatException($"Unexpected JSON token {reader.TokenType} for component");
        }
    }

    private static Component ReadArrayComponent(ref Utf8JsonReader reader, ComponentWireEra era)
    {
        var elements = new List<Component>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            elements.Add(ReadComponent(ref reader, era));

        if (elements.Count == 0)
            throw new ComponentFormatException("Component array must not be empty");

        Component first = elements[0];
        if (elements.Count == 1)
            return first;

        var children = new List<Component>(first.Children);
        for (int i = 1; i < elements.Count; i++)
            children.Add(elements[i]);

        return new Component(first.Content, first.Style, children);
    }

    private static Component ReadObjectComponent(ref Utf8JsonReader reader, ComponentWireEra era)
    {
        string? text = null;
        string? translate = null;
        string? translateFallback = null;
        List<Component>? translateWith = null;
        ScoreContent? score = null;
        string? selector = null;
        Component? selectorSeparator = null;
        string? keybind = null;
        NbtContent? nbt = null;
        string? nbtPath = null;
        bool nbtInterpret = false;
        Component? nbtSeparator = null;
        NbtDataSource? nbtSource = null;
        string? nbtSourceValue = null;

        var children = new List<Component>();
        var style = new StyleBuilder();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string name = reader.GetString()!;
            reader.Read();
            switch (name)
            {
                case "text":
                    text = ReadScalarString(ref reader);
                    break;
                case "translate":
                    translate = reader.GetString();
                    break;
                case "fallback":
                    translateFallback = reader.GetString();
                    break;
                case "with":
                    translateWith = ReadComponentList(ref reader, era);
                    break;
                case "score":
                    score = ReadScoreObject(ref reader);
                    break;
                case "selector":
                    selector = reader.GetString();
                    break;
                case "keybind":
                    keybind = reader.GetString();
                    break;
                case "nbt":
                    nbtPath = reader.GetString();
                    break;
                case "interpret":
                    nbtInterpret = reader.TokenType == JsonTokenType.True;
                    break;
                case "block":
                    nbtSource = NbtDataSource.Block;
                    nbtSourceValue = reader.GetString();
                    break;
                case "entity":
                    nbtSource = NbtDataSource.Entity;
                    nbtSourceValue = reader.GetString();
                    break;
                case "storage":
                    nbtSource = NbtDataSource.Storage;
                    nbtSourceValue = reader.GetString();
                    break;
                case "separator":
                    // Shared by selector and nbt; keep it for whichever content wins.
                    selectorSeparator = ReadComponent(ref reader, era);
                    nbtSeparator = selectorSeparator;
                    break;
                case "extra":
                    children.AddRange(ReadComponentList(ref reader, era));
                    break;
                default:
                    ReadStyleField(ref reader, name, style, era);
                    break;
            }
        }

        ComponentContent content = SelectContent(
            text, translate, translateFallback, translateWith,
            score, selector, selectorSeparator, keybind,
            nbtPath, nbtInterpret, nbtSeparator, nbtSource, nbtSourceValue,
            ref nbt);

        return new Component(content, style.Build(), children);
    }

    private static ComponentContent SelectContent(
        string? text, string? translate, string? translateFallback, List<Component>? translateWith,
        ScoreContent? score, string? selector, Component? selectorSeparator, string? keybind,
        string? nbtPath, bool nbtInterpret, Component? nbtSeparator, NbtDataSource? nbtSource, string? nbtSourceValue,
        ref NbtContent? nbt)
    {
        if (text is not null)
            return new TextContent(text);

        if (translate is not null)
            return new TranslatableContent(translate, translateFallback, translateWith ?? (IReadOnlyList<Component>)[]);

        if (score is not null)
            return score;

        if (selector is not null)
            return new SelectorContent(selector, selectorSeparator);

        if (keybind is not null)
            return new KeybindContent(keybind);

        if (nbtPath is not null && nbtSource is not null && nbtSourceValue is not null)
        {
            nbt = new NbtContent(nbtPath, nbtInterpret, nbtSeparator, nbtSource.Value, nbtSourceValue);
            return nbt;
        }

        // A styled object with no recognized content key is an empty-text component (vanilla accepts it).
        return TextContent.Empty;
    }

    private static ScoreContent ReadScoreObject(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new ComponentFormatException("'score' must be an object");

        string? name = null;
        string? objective = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string field = reader.GetString()!;
            reader.Read();
            switch (field)
            {
                case "name":
                    name = reader.GetString();
                    break;
                case "objective":
                    objective = reader.GetString();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        if (name is null || objective is null)
            throw new ComponentFormatException("'score' requires 'name' and 'objective'");

        return new ScoreContent(name, objective);
    }

    private static List<Component> ReadComponentList(ref Utf8JsonReader reader, ComponentWireEra era)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            // A single component is accepted where a list is expected (vanilla's either-list handling).
            return [ReadComponent(ref reader, era)];
        }

        var list = new List<Component>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            list.Add(ReadComponent(ref reader, era));

        return list;
    }

    private static void ReadStyleField(ref Utf8JsonReader reader, string name, StyleBuilder style, ComponentWireEra era)
    {
        switch (name)
        {
            case "color":
                style.Color = TextColor.Parse(reader.GetString()!);
                break;
            case "bold":
                style.Bold = ReadBool(ref reader);
                break;
            case "italic":
                style.Italic = ReadBool(ref reader);
                break;
            case "underlined":
                style.Underlined = ReadBool(ref reader);
                break;
            case "strikethrough":
                style.Strikethrough = ReadBool(ref reader);
                break;
            case "obfuscated":
                style.Obfuscated = ReadBool(ref reader);
                break;
            case "insertion":
                style.Insertion = reader.GetString();
                break;
            case "font":
                style.Font = reader.GetString();
                break;
            case "clickEvent":
            case "click_event":
                style.ClickEvent = ReadClickEvent(ref reader, era);
                break;
            case "hoverEvent":
            case "hover_event":
                style.HoverEvent = ReadHoverEvent(ref reader, era);
                break;
            default:
                reader.Skip();
                break;
        }
    }

    private static ClickEvent ReadClickEvent(ref Utf8JsonReader reader, ComponentWireEra era)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new ComponentFormatException("clickEvent must be an object");

        string? action = null;
        string? value = null;
        string? url = null;
        string? command = null;
        string? path = null;
        int? page = null;
        string? customId = null;
        string? dialog = null;
        NbtTag? payload = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string field = reader.GetString()!;
            reader.Read();
            switch (field)
            {
                case "action":
                    action = reader.GetString();
                    break;
                case "value":
                    value = ReadScalarString(ref reader);
                    break;
                case "url":
                    url = reader.GetString();
                    break;
                case "command":
                    command = reader.GetString();
                    break;
                case "path":
                    path = reader.GetString();
                    break;
                case "page":
                    page = reader.TokenType == JsonTokenType.Number ? reader.GetInt32() : ParseInt(reader.GetString());
                    break;
                case "id":
                    customId = reader.GetString();
                    break;
                case "dialog":
                    dialog = reader.GetString();
                    break;
                case "payload":
                    payload = ReadEmbeddedNbt(ref reader);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        if (action is null)
            throw new ComponentFormatException("clickEvent requires 'action'");

        return BuildClickEvent(action, era, value, url, command, path, page, customId, dialog, payload);
    }

    private static ClickEvent BuildClickEvent(
        string action, ComponentWireEra era,
        string? value, string? url, string? command, string? path, int? page,
        string? customId, string? dialog, NbtTag? payload)
    {
        switch (action)
        {
            case "open_url":
                return new ClickEvent(ClickEventAction.OpenUrl, url ?? value ?? string.Empty);
            case "open_file":
                return new ClickEvent(ClickEventAction.OpenFile, path ?? value ?? string.Empty);
            case "run_command":
                return new ClickEvent(ClickEventAction.RunCommand, command ?? value ?? string.Empty);
            case "suggest_command":
                return new ClickEvent(ClickEventAction.SuggestCommand, command ?? value ?? string.Empty);
            case "change_page":
                string pageValue = page is not null
                    ? page.Value.ToString(CultureInfo.InvariantCulture)
                    : value ?? "1";
                return new ClickEvent(ClickEventAction.ChangePage, pageValue);
            case "copy_to_clipboard":
                return new ClickEvent(ClickEventAction.CopyToClipboard, value ?? string.Empty);
            case "show_dialog":
                return new ClickEvent(ClickEventAction.ShowDialog, dialog ?? value ?? string.Empty);
            case "custom":
                return new ClickEvent(customId ?? value ?? string.Empty, payload);
            default:
                throw new ComponentFormatException($"Unknown clickEvent action '{action}'");
        }
    }

    private sealed class HoverFields
    {
        public string? Action { get; set; }

        public Component? TextComponent { get; set; }

        public string? ItemId { get; set; }

        public int ItemCount { get; set; } = 1;

        public NbtTag? ItemData { get; set; }

        public string? EntityType { get; set; }

        public string? EntityUuidText { get; set; }

        public Component? EntityName { get; set; }
    }

    private static HoverEvent ReadHoverEvent(ref Utf8JsonReader reader, ComponentWireEra era)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new ComponentFormatException("hoverEvent must be an object");

        var fields = new HoverFields();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string field = reader.GetString()!;
            reader.Read();
            switch (field)
            {
                case "action":
                    fields.Action = reader.GetString();
                    break;
                case "contents":
                    ReadHoverContents(ref reader, era, fields);
                    break;
                case "value":
                    // Modern show_text inlines the component under 'value'; legacy nests a stringified fallback.
                    fields.TextComponent = reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray
                        ? ReadComponent(ref reader, era)
                        : Component.Text(reader.GetString() ?? string.Empty);
                    break;
                // Modern inline show_item / show_entity fields alongside 'action'.
                case "id":
                    // For show_item this is the item id; for show_entity (modern) it is the entity type.
                    string idValue = reader.GetString()!;
                    fields.ItemId = idValue;
                    fields.EntityType = idValue;
                    break;
                case "count":
                    fields.ItemCount = reader.TokenType == JsonTokenType.Number ? reader.GetInt32() : 1;
                    break;
                case "components":
                case "tag":
                    fields.ItemData = ReadEmbeddedNbt(ref reader);
                    break;
                case "uuid":
                    fields.EntityUuidText = reader.GetString();
                    break;
                case "name":
                    fields.EntityName = ReadComponent(ref reader, era);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        if (fields.Action is null)
            throw new ComponentFormatException("hoverEvent requires 'action'");

        return BuildHoverEvent(fields);
    }

    private static void ReadHoverContents(ref Utf8JsonReader reader, ComponentWireEra era, HoverFields fields)
    {
        // 'contents' (legacy) can be: a show_text component, a show_item/show_entity object, or a bare item-id string (show_item shorthand).
        if (reader.TokenType == JsonTokenType.String)
        {
            fields.ItemId = reader.GetString();
            fields.TextComponent = Component.Text(fields.ItemId!);
            return;
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            fields.TextComponent = ReadComponent(ref reader, era);
            return;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new ComponentFormatException("hoverEvent 'contents' must be a string, array, or object");

        // The object is either a show_text component or a show_item/show_entity payload.
        var textStyle = new StyleBuilder();
        string? text = null;
        string? translate = null;
        var extra = new List<Component>();
        bool sawPayloadField = false;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string field = reader.GetString()!;
            reader.Read();
            switch (field)
            {
                case "id":
                    // In a legacy contents object, 'id' is the item id (show_item) or the UUID (show_entity).
                    string idValue = reader.GetString()!;
                    fields.ItemId = idValue;
                    fields.EntityUuidText = idValue;
                    sawPayloadField = true;
                    break;
                case "count":
                    fields.ItemCount = reader.TokenType == JsonTokenType.Number ? reader.GetInt32() : 1;
                    sawPayloadField = true;
                    break;
                case "type":
                    fields.EntityType = reader.GetString();
                    sawPayloadField = true;
                    break;
                case "uuid":
                    fields.EntityUuidText = reader.GetString();
                    sawPayloadField = true;
                    break;
                case "tag":
                case "components":
                    fields.ItemData = ReadEmbeddedNbt(ref reader);
                    sawPayloadField = true;
                    break;
                case "name":
                    fields.EntityName = ReadComponent(ref reader, era);
                    break;
                case "text":
                    text = ReadScalarString(ref reader);
                    break;
                case "translate":
                    translate = reader.GetString();
                    break;
                case "extra":
                    extra.AddRange(ReadComponentList(ref reader, era));
                    break;
                default:
                    ReadStyleField(ref reader, field, textStyle, era);
                    break;
            }
        }

        if (!sawPayloadField)
        {
            ComponentContent content = translate is not null
                ? new TranslatableContent(translate, null, (IReadOnlyList<Component>)[])
                : new TextContent(text ?? string.Empty);
            fields.TextComponent = new Component(content, textStyle.Build(), extra);
        }
    }

    private static HoverEvent BuildHoverEvent(HoverFields fields)
    {
        switch (fields.Action)
        {
            case "show_text":
                return new HoverShowText(fields.TextComponent ?? Component.Empty);
            case "show_item":
                return new HoverShowItem(fields.ItemId ?? "minecraft:air", fields.ItemCount, fields.ItemData);
            case "show_entity":
                return new HoverShowEntity(fields.EntityType ?? "minecraft:pig", ParseUuid(fields.EntityUuidText), fields.EntityName);
            default:
                throw new ComponentFormatException($"Unknown hoverEvent action '{fields.Action}'");
        }
    }

    private static NbtTag? ReadEmbeddedNbt(ref Utf8JsonReader reader)
    {
        // In JSON, embedded NBT (item tag / custom payload) is carried as an SNBT string.
        if (reader.TokenType == JsonTokenType.String)
        {
            string snbt = reader.GetString()!;
            try
            {
                return SnbtParser.ParseValue(snbt);
            }
            catch (NbtFormatException)
            {
                return new NbtString(snbt);
            }
        }

        reader.Skip();
        return null;
    }

    private static string ReadScalarString(ref Utf8JsonReader reader) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString()!,
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Number => ReadRawNumber(ref reader),
            _ => throw new ComponentFormatException($"Expected a string scalar, got {reader.TokenType}"),
        };

    private static bool ReadBool(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.True => true,
        JsonTokenType.False => false,
        JsonTokenType.String => bool.TryParse(reader.GetString(), out bool b) && b,
        _ => false,
    };

    private static string ReadRawNumber(ref Utf8JsonReader reader) =>
        Encoding.UTF8.GetString(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan.ToArray());

    private static int ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : 0;

    private static Guid ParseUuid(string? text)
    {
        if (text is null)
            return Guid.Empty;

        return Guid.TryParse(text, out Guid guid) ? guid : Guid.Empty;
    }

    // Before 1.20.3, components use a stable insertion order: style, extra, then content. Style fields are bold, italic, underlined, strikethrough, obfuscated, color, insertion, clickEvent, hoverEvent, then font. These representative contract values pin that order:
    //   {"bold":true,"color":"red","insertion":"ins","font":"minecraft:alt","extra":[{"text":"child"}],"text":"hello"}
    //   {"color":"green","extra":[{"text":"x"}],"translate":"chat.type.text","with":[{"text":"A"},{"text":"B"}]}
    //
    // From 1.20.3, codec composition can reorder style keys as the schema changes. Reproducing that
    // internal order per version has no semantic value because JSON object-key order is insignificant.
    // The modern output is therefore key-complete and value-exact without promising byte ordering.

    /// <summary>The style keys in the order the pre-1.20.3 GSON <c>legacy style serialization</c> writes them.</summary>
    private static readonly StyleKey[] GsonStyleOrder =
    [
        StyleKey.Bold, StyleKey.Italic, StyleKey.Underlined, StyleKey.Strikethrough, StyleKey.Obfuscated,
        StyleKey.Color, StyleKey.Insertion, StyleKey.ClickEvent, StyleKey.HoverEvent, StyleKey.Font,
    ];

    /// <summary>The style keys in this library's own 1.20.3+ order. See the note above for why it is not vanilla's.</summary>
    private static readonly StyleKey[] CodecStyleOrder =
    [
        StyleKey.Color, StyleKey.Bold, StyleKey.Italic, StyleKey.Underlined, StyleKey.Strikethrough,
        StyleKey.Obfuscated, StyleKey.Insertion, StyleKey.Font, StyleKey.ClickEvent, StyleKey.HoverEvent,
    ];

    private enum StyleKey
    {
        Color,
        Bold,
        Italic,
        Underlined,
        Strikethrough,
        Obfuscated,
        Insertion,
        Font,
        ClickEvent,
        HoverEvent,
    }

    private static void WriteComponent(
        Utf8JsonWriter writer, Component component, ComponentWireEra era, ComponentJsonLiteralForm literalForm)
    {
        // A pure literal (PlainTextContents, empty style, no siblings) is the one component whose JSON form MOVED: pre-1.20.3 GSON always wrote the object, 1.20.3+ JsonOps collapses it to a bare string. The predicate below is literal-component collapse's, verbatim. See ComponentJsonLiteralForm for the anchors on both sides of the boundary.
        if (literalForm == ComponentJsonLiteralForm.Collapsed
            && component.Content is TextContent tc
            && component.Style.IsEmpty
            && component.Children.Count == 0)
        {
            writer.WriteStringValue(tc.Text);
            return;
        }

        writer.WriteStartObject();
        if (literalForm == ComponentJsonLiteralForm.Object)
        {
            WriteStyle(writer, component.Style, era, literalForm);
            WriteChildren(writer, component, era, literalForm);
            WriteContent(writer, component.Content, era, literalForm);
        }
        else
        {
            WriteContent(writer, component.Content, era, literalForm);
            WriteStyle(writer, component.Style, era, literalForm);
            WriteChildren(writer, component, era, literalForm);
        }

        writer.WriteEndObject();
    }

    private static void WriteChildren(
        Utf8JsonWriter writer, Component component, ComponentWireEra era, ComponentJsonLiteralForm literalForm)
    {
        if (component.Children.Count == 0)
            return;

        writer.WritePropertyName("extra");
        writer.WriteStartArray();
        foreach (Component child in component.Children)
            WriteComponent(writer, child, era, literalForm);

        writer.WriteEndArray();
    }

    private static void WriteContent(
        Utf8JsonWriter writer, ComponentContent content, ComponentWireEra era, ComponentJsonLiteralForm literalForm)
    {
        switch (content)
        {
            case TextContent text:
                writer.WriteString("text", text.Text);
                break;

            case TranslatableContent translatable:
                writer.WriteString("translate", translatable.Key);
                if (translatable.Fallback is not null)
                    writer.WriteString("fallback", translatable.Fallback);

                if (translatable.Args.Count > 0)
                {
                    writer.WritePropertyName("with");
                    writer.WriteStartArray();
                    foreach (Component arg in translatable.Args)
                        WriteComponent(writer, arg, era, literalForm);

                    writer.WriteEndArray();
                }

                break;

            case ScoreContent score:
                writer.WritePropertyName("score");
                writer.WriteStartObject();
                writer.WriteString("name", score.Name);
                writer.WriteString("objective", score.Objective);
                writer.WriteEndObject();
                break;

            case SelectorContent selector:
                writer.WriteString("selector", selector.Pattern);
                if (selector.Separator is not null)
                {
                    writer.WritePropertyName("separator");
                    WriteComponent(writer, selector.Separator, era, literalForm);
                }

                break;

            case KeybindContent keybind:
                writer.WriteString("keybind", keybind.Keybind);
                break;

            case NbtContent nbt:
                writer.WriteString("nbt", nbt.NbtPath);
                if (nbt.Interpret)
                    writer.WriteBoolean("interpret", true);

                writer.WriteString(ComponentSerializationNames.SourceKey(nbt.Source), nbt.SourceValue);
                if (nbt.Separator is not null)
                {
                    writer.WritePropertyName("separator");
                    WriteComponent(writer, nbt.Separator, era, literalForm);
                }

                break;
        }
    }

    private static void WriteStyle(
        Utf8JsonWriter writer, Style style, ComponentWireEra era, ComponentJsonLiteralForm literalForm)
    {
        StyleKey[] order = literalForm == ComponentJsonLiteralForm.Object ? GsonStyleOrder : CodecStyleOrder;
        foreach (StyleKey key in order)
            switch (key)
            {
                case StyleKey.Color when style.Color is TextColor color:
                    writer.WriteString("color", color.Serialize());
                    break;
                case StyleKey.Bold when style.Bold is bool bold:
                    writer.WriteBoolean("bold", bold);
                    break;
                case StyleKey.Italic when style.Italic is bool italic:
                    writer.WriteBoolean("italic", italic);
                    break;
                case StyleKey.Underlined when style.Underlined is bool underlined:
                    writer.WriteBoolean("underlined", underlined);
                    break;
                case StyleKey.Strikethrough when style.Strikethrough is bool strikethrough:
                    writer.WriteBoolean("strikethrough", strikethrough);
                    break;
                case StyleKey.Obfuscated when style.Obfuscated is bool obfuscated:
                    writer.WriteBoolean("obfuscated", obfuscated);
                    break;
                case StyleKey.Insertion when style.Insertion is not null:
                    writer.WriteString("insertion", style.Insertion);
                    break;
                case StyleKey.Font when style.Font is not null:
                    writer.WriteString("font", style.Font);
                    break;
                case StyleKey.ClickEvent when style.ClickEvent is not null:
                    WriteClickEvent(writer, style.ClickEvent, era);
                    break;
                case StyleKey.HoverEvent when style.HoverEvent is not null:
                    WriteHoverEvent(writer, style.HoverEvent, era, literalForm);
                    break;
                default:
                    break;
            }

    }

    private static void WriteClickEvent(Utf8JsonWriter writer, ClickEvent click, ComponentWireEra era)
    {
        writer.WritePropertyName(era == ComponentWireEra.Modern ? "click_event" : "clickEvent");
        writer.WriteStartObject();
        writer.WriteString("action", ComponentSerializationNames.ClickActionName(click.Action));
        if (era == ComponentWireEra.Modern)
            switch (click.Action)
            {
                case ClickEventAction.OpenUrl:
                    writer.WriteString("url", click.Value);
                    break;
                case ClickEventAction.OpenFile:
                    writer.WriteString("path", click.Value);
                    break;
                case ClickEventAction.RunCommand:
                case ClickEventAction.SuggestCommand:
                    writer.WriteString("command", click.Value);
                    break;
                case ClickEventAction.ChangePage:
                    writer.WriteNumber("page", ParseInt(click.Value));
                    break;
                case ClickEventAction.CopyToClipboard:
                    writer.WriteString("value", click.Value);
                    break;
                case ClickEventAction.ShowDialog:
                    writer.WriteString("dialog", click.Value);
                    break;
                case ClickEventAction.Custom:
                    writer.WriteString("id", click.Value);
                    if (click.Payload is not null)
                        writer.WriteString("payload", SnbtPrinter.Print(click.Payload));

                    break;
            }

        else
            writer.WriteString("value", click.Value);

        writer.WriteEndObject();
    }

    private static void WriteHoverEvent(
        Utf8JsonWriter writer, HoverEvent hover, ComponentWireEra era, ComponentJsonLiteralForm literalForm)
    {
        writer.WritePropertyName(era == ComponentWireEra.Modern ? "hover_event" : "hoverEvent");
        writer.WriteStartObject();
        writer.WriteString("action", hover.ActionName);

        if (era == ComponentWireEra.Modern)
            WriteHoverModern(writer, hover, era, literalForm);

        else
            WriteHoverLegacy(writer, hover, era, literalForm);

        writer.WriteEndObject();
    }

    private static void WriteHoverModern(
        Utf8JsonWriter writer, HoverEvent hover, ComponentWireEra era, ComponentJsonLiteralForm literalForm)
    {
        switch (hover)
        {
            case HoverShowText text:
                writer.WritePropertyName("value");
                WriteComponent(writer, text.Text, era, literalForm);
                break;
            case HoverShowItem item:
                writer.WriteString("id", item.ItemId);

                // From 1.20.5, the encoder always emits count, including the default value of one.
                writer.WriteNumber("count", item.Count);
                if (item.Data is not null)
                    writer.WriteString("components", SnbtPrinter.Print(item.Data));

                break;
            case HoverShowEntity entity:
                writer.WriteString("id", entity.EntityType);
                writer.WriteString("uuid", entity.Id.ToString());
                if (entity.Name is not null)
                {
                    writer.WritePropertyName("name");
                    WriteComponent(writer, entity.Name, era, literalForm);
                }

                break;
        }
    }

    private static void WriteHoverLegacy(
        Utf8JsonWriter writer, HoverEvent hover, ComponentWireEra era, ComponentJsonLiteralForm literalForm)
    {
        writer.WritePropertyName("contents");
        switch (hover)
        {
            case HoverShowText text:
                WriteComponent(writer, text.Text, era, literalForm);
                break;
            case HoverShowItem item:
                writer.WriteStartObject();
                writer.WriteString("id", item.ItemId);

                // Before 1.20.3, the encoder omits a count of one and the decoder restores that default. From protocol 766, the modern form always writes count. Protocol 765 is intentionally approximated by the modern form because its JSON component use is limited to the login-disconnect reason; an extra default-valued key is semantically invisible there.
                if (literalForm != ComponentJsonLiteralForm.Object || item.Count != 1)
                    writer.WriteNumber("count", item.Count);

                if (item.Data is not null)
                    writer.WriteString("tag", SnbtPrinter.Print(item.Data));

                writer.WriteEndObject();
                break;
            case HoverShowEntity entity:
                writer.WriteStartObject();
                writer.WriteString("type", entity.EntityType);
                writer.WriteString("id", entity.Id.ToString());
                if (entity.Name is not null)
                {
                    writer.WritePropertyName("name");
                    WriteComponent(writer, entity.Name, era, literalForm);
                }

                writer.WriteEndObject();
                break;
        }
    }

}
