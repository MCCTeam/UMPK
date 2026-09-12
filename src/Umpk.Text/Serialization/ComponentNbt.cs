using Umpk.Nbt;
using Umpk.Nbt.Snbt;

namespace Umpk.Text.Serialization;

/// <summary>
/// Encodes and decodes <see cref="Component"/> trees to and from the NBT wire form used since 1.20.3. A component is a bare <see cref="NbtString"/> (plain text), a bare <see cref="NbtList"/> (the first element is the base, the rest are siblings), or an <see cref="NbtCompound"/> whose content type is inferred from its keys. Style booleans are stored as byte tags. This operates on decoded <see cref="NbtTag"/> values; framing on the wire uses <see cref="NbtWireFormat.JavaRootTagOrString"/>.
/// <para>A bare string tag is always a literal, never re-parsed as JSON. Therefore the client renders <c>[Admin] </c> or <c>{"not":"json"</c> verbatim. Which form a wire position carries is decided by the protocol, not by inspecting the value. The caller already knows the era, so content sniffing is neither needed nor correct here.</para>
/// </summary>
public static class ComponentNbt
{
    /// <summary>Decodes a component from an NBT tag using the modern wire era.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="tag"/> is null.</exception>
    /// <exception cref="ComponentFormatException">The tag is not a valid component.</exception>
    public static Component From(NbtTag tag) => From(tag, ComponentWireEra.Modern);

    /// <summary>Decodes a component from an NBT tag.</summary>
    /// <param name="tag">The decoded NBT value (typically read with <see cref="NbtWireFormat.JavaRootTagOrString"/>).</param>
    /// <param name="era">Which wire era's click/hover field shapes to expect.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tag"/> is null.</exception>
    /// <exception cref="ComponentFormatException">The tag is not a valid component.</exception>
    public static Component From(NbtTag tag, ComponentWireEra era)
    {
        ArgumentNullException.ThrowIfNull(tag);
        return ReadComponent(tag, era);
    }

    /// <summary>Encodes a component to an NBT tag using the given wire era (default modern).</summary>
    /// <exception cref="ArgumentNullException"><paramref name="component"/> is null.</exception>
    public static NbtTag To(Component component, ComponentWireEra era = ComponentWireEra.Modern)
    {
        ArgumentNullException.ThrowIfNull(component);

        // Collapse a pure literal string with no style and no children to a bare string tag.
        if (component.Content is TextContent tc && component.Style.IsEmpty && component.Children.Count == 0)
            return new NbtString(tc.Text);

        var compound = new NbtCompound();
        WriteContent(compound, component.Content, era);
        WriteStyle(compound, component.Style, era);
        if (component.Children.Count > 0)
        {
            var extra = new NbtList();
            foreach (Component child in component.Children)
                extra.Add(To(child, era));

            compound.Put("extra", extra);
        }

        return compound;
    }

    // decode

    private static Component ReadComponent(NbtTag tag, ComponentWireEra era)
    {
        switch (tag)
        {
            case NbtString str:
                // Vanilla's string branch is Component::literal. A value that happens to look like JSON ("[Admin] ", "{}", "{\"text\":\"x\"}") is still literal text, so no sniffing happens.
                return Component.Text(str.Value);

            case NbtNumeric num:
                return Component.Text(num.AsDouble.ToString(System.Globalization.CultureInfo.InvariantCulture));

            case NbtList list:
                return ReadListComponent(list, era);

            case NbtCompound compound:
                return ReadCompoundComponent(compound, era);

            default:
                throw new ComponentFormatException($"Cannot decode component from NBT tag of type {tag.Type}");
        }
    }

    private static Component ReadListComponent(NbtList list, ComponentWireEra era)
    {
        if (list.Count == 0)
            throw new ComponentFormatException("Component list must not be empty");

        Component first = ReadComponent(list[0], era);
        if (list.Count == 1)
            return first;

        var children = new List<Component>(first.Children);
        for (int i = 1; i < list.Count; i++)
            children.Add(ReadComponent(list[i], era));

        return new Component(first.Content, first.Style, children);
    }

    private static Component ReadCompoundComponent(NbtCompound c, ComponentWireEra era)
    {
        ComponentContent content = ReadContent(c, era);
        Style style = ReadStyle(c, era);

        var children = new List<Component>();
        if (c.GetList("extra") is NbtList extra)
            foreach (NbtTag child in extra)
                children.Add(ReadComponent(child, era));

        return new Component(content, style, children);
    }

    private static ComponentContent ReadContent(NbtCompound c, ComponentWireEra era)
    {
        if (c.TryGet("text", out NbtTag? textTag))
            return new TextContent(AsString(textTag));

        if (c.TryGet("translate", out NbtString? translate))
        {
            string? fallback = c.TryGet("fallback", out NbtString? fb) ? fb.Value : null;
            var args = new List<Component>();
            if (c.GetList("with") is NbtList with)
                foreach (NbtTag arg in with)
                    args.Add(ReadComponent(arg, era));

            return new TranslatableContent(translate.Value, fallback, args);
        }

        if (c.GetCompound("score") is NbtCompound score)
            return new ScoreContent(score.GetString("name"), score.GetString("objective"));

        if (c.TryGet("selector", out NbtString? selector))
        {
            Component? sep = c.TryGet("separator", out NbtTag? sepTag) ? ReadComponent(sepTag, era) : null;
            return new SelectorContent(selector.Value, sep);
        }

        if (c.TryGet("keybind", out NbtString? keybind))
            return new KeybindContent(keybind.Value);

        if (c.TryGet("nbt", out NbtString? nbtPath))
        {
            bool interpret = c.GetBool("interpret");
            Component? sep = c.TryGet("separator", out NbtTag? sepTag) ? ReadComponent(sepTag, era) : null;
            (NbtDataSource source, string value) = ReadNbtSource(c);
            return new NbtContent(nbtPath.Value, interpret, sep, source, value);
        }

        return TextContent.Empty;
    }

    private static (NbtDataSource Source, string Value) ReadNbtSource(NbtCompound c)
    {
        if (c.TryGet("block", out NbtString? block))
            return (NbtDataSource.Block, block.Value);

        if (c.TryGet("entity", out NbtString? entity))
            return (NbtDataSource.Entity, entity.Value);

        if (c.TryGet("storage", out NbtString? storage))
            return (NbtDataSource.Storage, storage.Value);

        throw new ComponentFormatException("nbt content requires a 'block', 'entity', or 'storage' source");
    }

    private static Style ReadStyle(NbtCompound c, ComponentWireEra era)
    {
        var style = new StyleBuilder();
        if (c.TryGet("color", out NbtString? color))
            style.Color = TextColor.Parse(color.Value);

        style.Bold = ReadOptionalBool(c, "bold");
        style.Italic = ReadOptionalBool(c, "italic");
        style.Underlined = ReadOptionalBool(c, "underlined");
        style.Strikethrough = ReadOptionalBool(c, "strikethrough");
        style.Obfuscated = ReadOptionalBool(c, "obfuscated");

        if (c.TryGet("insertion", out NbtString? insertion))
            style.Insertion = insertion.Value;

        if (c.TryGet("font", out NbtString? font))
            style.Font = font.Value;

        NbtCompound? clickCompound = c.GetCompound("click_event") ?? c.GetCompound("clickEvent");
        if (clickCompound is not null)
            style.ClickEvent = ReadClickEvent(clickCompound, era);

        NbtCompound? hoverCompound = c.GetCompound("hover_event") ?? c.GetCompound("hoverEvent");
        if (hoverCompound is not null)
            style.HoverEvent = ReadHoverEvent(hoverCompound, era);

        return style.Build();
    }

    private static bool? ReadOptionalBool(NbtCompound c, string key) =>
        c.TryGet(key, out NbtNumeric? n) ? n.AsSByte != 0 : null;

    private static ClickEvent ReadClickEvent(NbtCompound c, ComponentWireEra era)
    {
        string action = c.GetString("action");
        switch (action)
        {
            case "open_url":
                return new ClickEvent(ClickEventAction.OpenUrl, StringField(c, "url", "value"));
            case "open_file":
                return new ClickEvent(ClickEventAction.OpenFile, StringField(c, "path", "value"));
            case "run_command":
                return new ClickEvent(ClickEventAction.RunCommand, StringField(c, "command", "value"));
            case "suggest_command":
                return new ClickEvent(ClickEventAction.SuggestCommand, StringField(c, "command", "value"));
            case "change_page":
                string page = c.TryGet("page", out NbtNumeric? p)
                    ? p.AsInt.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : c.GetString("value");
                return new ClickEvent(ClickEventAction.ChangePage, page.Length == 0 ? "1" : page);
            case "copy_to_clipboard":
                return new ClickEvent(ClickEventAction.CopyToClipboard, c.GetString("value"));
            case "show_dialog":
                return new ClickEvent(ClickEventAction.ShowDialog, StringField(c, "dialog", "value"));
            case "custom":
                string id = StringField(c, "id", "value");
                NbtTag? payload = c.TryGet("payload", out NbtTag? pl) ? pl : null;
                return new ClickEvent(id, payload);
            default:
                throw new ComponentFormatException($"Unknown clickEvent action '{action}'");
        }
    }

    private static HoverEvent ReadHoverEvent(NbtCompound c, ComponentWireEra era)
    {
        string action = c.GetString("action");
        switch (action)
        {
            case "show_text":
                NbtTag? textTag = c.TryGet("value", out NbtTag? v) ? v : (c.TryGet("contents", out NbtTag? ct) ? ct : null);
                return new HoverShowText(textTag is not null ? ReadComponent(textTag, era) : Component.Empty);

            case "show_item":
                return ReadShowItem(c);

            case "show_entity":
                return ReadShowEntity(c, era);

            default:
                throw new ComponentFormatException($"Unknown hoverEvent action '{action}'");
        }
    }

    private static HoverShowItem ReadShowItem(NbtCompound c)
    {
        // Modern: id/count/components inline. Legacy: nested under 'contents'.
        NbtCompound source = c.GetCompound("contents") ?? c;
        if (c.TryGet("contents", out NbtString? shorthand))
            return new HoverShowItem(shorthand.Value, 1, null);

        string id = source.GetString("id");
        int count = source.TryGet("count", out NbtNumeric? cnt) ? cnt.AsInt : 1;
        NbtTag? data = null;
        if (source.TryGet("components", out NbtTag? comps))
            data = comps;

        else if (source.TryGet("tag", out NbtTag? tag))
            data = tag;

        return new HoverShowItem(id.Length == 0 ? "minecraft:air" : id, count, data);
    }

    private static HoverShowEntity ReadShowEntity(NbtCompound c, ComponentWireEra era)
    {
        NbtCompound source = c.GetCompound("contents") ?? c;
        // Modern uses id/uuid, legacy uses type/id.
        string type = source.TryGet("id", out NbtString? idStr) && era == ComponentWireEra.Modern
            ? idStr.Value
            : source.GetString(era == ComponentWireEra.Modern ? "id" : "type");
        if (type.Length == 0)
            type = source.GetString("type");

        Guid uuid = ReadUuid(source, era);
        Component? name = source.TryGet("name", out NbtTag? nameTag) ? ReadComponent(nameTag, era) : null;
        return new HoverShowEntity(type.Length == 0 ? "minecraft:pig" : type, uuid, name);
    }

    private static Guid ReadUuid(NbtCompound c, ComponentWireEra era)
    {
        string key = era == ComponentWireEra.Modern ? "uuid" : "id";
        // Int-array UUID form (four ints) is the canonical NBT encoding.
        if (c.TryGet(key, out NbtIntArray? arr) && arr.Value.Length == 4)
            return IntsToGuid(arr.Value);

        if (c.TryGet(key, out NbtString? s) && Guid.TryParse(s.Value, out Guid parsed))
            return parsed;

        return Guid.Empty;
    }

    private static string StringField(NbtCompound c, string primary, string fallback)
    {
        if (c.TryGet(primary, out NbtString? s))
            return s.Value;

        return c.TryGet(fallback, out NbtString? f) ? f.Value : string.Empty;
    }

    private static string AsString(NbtTag tag) => tag switch
    {
        NbtString s => s.Value,
        NbtNumeric n => n.AsDouble.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => throw new ComponentFormatException($"Expected a string-like NBT tag, got {tag.Type}"),
    };

    // encode

    private static void WriteContent(NbtCompound c, ComponentContent content, ComponentWireEra era)
    {
        switch (content)
        {
            case TextContent text:
                c.PutString("text", text.Text);
                break;

            case TranslatableContent translatable:
                c.PutString("translate", translatable.Key);
                if (translatable.Fallback is not null)
                    c.PutString("fallback", translatable.Fallback);

                if (translatable.Args.Count > 0)
                {
                    var with = new NbtList();
                    foreach (Component arg in translatable.Args)
                        with.Add(To(arg, era));

                    c.Put("with", with);
                }

                break;

            case ScoreContent score:
                var scoreCompound = new NbtCompound();
                scoreCompound.PutString("name", score.Name);
                scoreCompound.PutString("objective", score.Objective);
                c.Put("score", scoreCompound);
                break;

            case SelectorContent selector:
                c.PutString("selector", selector.Pattern);
                if (selector.Separator is not null)
                    c.Put("separator", To(selector.Separator, era));

                break;

            case KeybindContent keybind:
                c.PutString("keybind", keybind.Keybind);
                break;

            case NbtContent nbt:
                c.PutString("nbt", nbt.NbtPath);
                if (nbt.Interpret)
                    c.PutBool("interpret", true);

                c.PutString(ComponentSerializationNames.SourceKey(nbt.Source), nbt.SourceValue);
                if (nbt.Separator is not null)
                    c.Put("separator", To(nbt.Separator, era));

                break;
        }
    }

    private static void WriteStyle(NbtCompound c, Style style, ComponentWireEra era)
    {
        if (style.Color is TextColor color)
            c.PutString("color", color.Serialize());

        if (style.Bold is bool bold)
            c.PutBool("bold", bold);

        if (style.Italic is bool italic)
            c.PutBool("italic", italic);

        if (style.Underlined is bool underlined)
            c.PutBool("underlined", underlined);

        if (style.Strikethrough is bool strikethrough)
            c.PutBool("strikethrough", strikethrough);

        if (style.Obfuscated is bool obfuscated)
            c.PutBool("obfuscated", obfuscated);

        if (style.Insertion is not null)
            c.PutString("insertion", style.Insertion);

        if (style.Font is not null)
            c.PutString("font", style.Font);

        if (style.ClickEvent is not null)
            c.Put(era == ComponentWireEra.Modern ? "click_event" : "clickEvent", WriteClickEvent(style.ClickEvent, era));

        if (style.HoverEvent is not null)
            c.Put(era == ComponentWireEra.Modern ? "hover_event" : "hoverEvent", WriteHoverEvent(style.HoverEvent, era));

    }

    private static NbtCompound WriteClickEvent(ClickEvent click, ComponentWireEra era)
    {
        var c = new NbtCompound();
        c.PutString("action", ComponentSerializationNames.ClickActionName(click.Action));
        if (era == ComponentWireEra.Modern)
            switch (click.Action)
            {
                case ClickEventAction.OpenUrl:
                    c.PutString("url", click.Value);
                    break;
                case ClickEventAction.OpenFile:
                    c.PutString("path", click.Value);
                    break;
                case ClickEventAction.RunCommand:
                case ClickEventAction.SuggestCommand:
                    c.PutString("command", click.Value);
                    break;
                case ClickEventAction.ChangePage:
                    c.PutInt("page", ParseIntOrOne(click.Value));
                    break;
                case ClickEventAction.CopyToClipboard:
                    c.PutString("value", click.Value);
                    break;
                case ClickEventAction.ShowDialog:
                    c.PutString("dialog", click.Value);
                    break;
                case ClickEventAction.Custom:
                    c.PutString("id", click.Value);
                    if (click.Payload is not null)
                        c.Put("payload", click.Payload);

                    break;
            }

        else
            c.PutString("value", click.Value);

        return c;
    }

    private static NbtCompound WriteHoverEvent(HoverEvent hover, ComponentWireEra era)
    {
        var c = new NbtCompound();
        c.PutString("action", hover.ActionName);
        if (era == ComponentWireEra.Modern)
            WriteHoverModern(c, hover, era);

        else
            WriteHoverLegacy(c, hover, era);

        return c;
    }

    private static void WriteHoverModern(NbtCompound c, HoverEvent hover, ComponentWireEra era)
    {
        switch (hover)
        {
            case HoverShowText text:
                c.Put("value", To(text.Text, era));
                break;
            case HoverShowItem item:
                c.PutString("id", item.ItemId);
                c.PutInt("count", item.Count);
                if (item.Data is not null)
                    c.Put("components", item.Data);

                break;
            case HoverShowEntity entity:
                c.PutString("id", entity.EntityType);
                c.Put("uuid", GuidToIntArray(entity.Id));
                if (entity.Name is not null)
                    c.Put("name", To(entity.Name, era));

                break;
        }
    }

    private static void WriteHoverLegacy(NbtCompound c, HoverEvent hover, ComponentWireEra era)
    {
        switch (hover)
        {
            case HoverShowText text:
                c.Put("contents", To(text.Text, era));
                break;
            case HoverShowItem item:
                var itemCompound = new NbtCompound();
                itemCompound.PutString("id", item.ItemId);
                itemCompound.PutInt("count", item.Count);
                if (item.Data is not null)
                    itemCompound.Put("tag", item.Data);

                c.Put("contents", itemCompound);
                break;
            case HoverShowEntity entity:
                var entityCompound = new NbtCompound();
                entityCompound.PutString("type", entity.EntityType);
                entityCompound.Put("id", GuidToIntArray(entity.Id));
                if (entity.Name is not null)
                    entityCompound.Put("name", To(entity.Name, era));

                c.Put("contents", entityCompound);
                break;
        }
    }

    private static int ParseIntOrOne(string value) =>
        int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int i)
            ? i
            : 1;

    // Vanilla stores UUIDs as an int-array of four 32-bit words, big-endian, most-significant word
    // first. The 16 bytes are the plain Java UUID layout (mostSigBits then leastSigBits, big-endian);
    // .NET's Guid(byte[]) constructor uses a mixed-endian layout, so the bytes are mapped explicitly.
    private static NbtIntArray GuidToIntArray(Guid guid)
    {
        Span<byte> bytes = stackalloc byte[16];
        WriteGuidBigEndian(guid, bytes);
        var ints = new int[4];
        for (int i = 0; i < 4; i++)
            ints[i] = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(i * 4, 4));

        return new NbtIntArray(ints);
    }

    private static Guid IntsToGuid(int[] ints)
    {
        Span<byte> bytes = stackalloc byte[16];
        for (int i = 0; i < 4; i++)
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.Slice(i * 4, 4), ints[i]);

        return ReadGuidBigEndian(bytes);
    }

    private static void WriteGuidBigEndian(Guid guid, Span<byte> destination)
    {
        // Guid.TryWriteBytes emits the .NET mixed-endian layout; reorder to the Java big-endian form.
        Span<byte> raw = stackalloc byte[16];
        _ = guid.TryWriteBytes(raw);
        MixedToBigEndian(raw, destination);
    }

    private static Guid ReadGuidBigEndian(ReadOnlySpan<byte> source)
    {
        Span<byte> raw = stackalloc byte[16];
        BigToMixedEndian(source, raw);
        return new Guid(raw);
    }

    // .NET Guid bytes: first three groups are little-endian (int, short, short), last eight are as-is.
    private static void MixedToBigEndian(ReadOnlySpan<byte> mixed, Span<byte> bigEndian)
    {
        bigEndian[0] = mixed[3];
        bigEndian[1] = mixed[2];
        bigEndian[2] = mixed[1];
        bigEndian[3] = mixed[0];
        bigEndian[4] = mixed[5];
        bigEndian[5] = mixed[4];
        bigEndian[6] = mixed[7];
        bigEndian[7] = mixed[6];
        for (int i = 8; i < 16; i++)
            bigEndian[i] = mixed[i];

    }

    private static void BigToMixedEndian(ReadOnlySpan<byte> bigEndian, Span<byte> mixed)
    {
        mixed[0] = bigEndian[3];
        mixed[1] = bigEndian[2];
        mixed[2] = bigEndian[1];
        mixed[3] = bigEndian[0];
        mixed[4] = bigEndian[5];
        mixed[5] = bigEndian[4];
        mixed[6] = bigEndian[7];
        mixed[7] = bigEndian[6];
        for (int i = 8; i < 16; i++)
            mixed[i] = bigEndian[i];

    }
}
