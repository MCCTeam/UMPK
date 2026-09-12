using Umpk.Game.Registries;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;

namespace Umpk.Client.Internal;

/// <summary>Turns the server's own <c>minecraft:chat_type</c> data into a <see cref="Registry{T}"/> of <see cref="ChatTypeDefinition"/>, plus the vanilla built-in table used when the server sends an entry with its element elided.</summary>
/// <remarks>
/// <para>The same three wire shapes that carry the dimension types carry these, so this mirrors <see cref="DimensionTypes"/> exactly:</para>
/// <list type="bullet">
/// <item>1.20.2+ (764+): configuration-phase <c>registry_data</c>, one packet per registry, entries in
/// network-id order (<see cref="FromPackedEntries"/>).</item>
/// <item>1.19-1.20.1: the whole registry set as one named-root NBT blob inside JoinGame, each
/// registry a <c>{type, value:[{name,id,element}]}</c> compound (<see cref="FromJoinGameRegistry"/>).</item>
/// </list>
/// <para>Each element is a datapack compound rather than a stream-codec payload. Two shapes exist:</para>
/// <list type="bullet">
/// <item>Protocol 760 and up: <c>{chat:{translation_key,parameters,style?},narration:{...}}</c>, with
/// parameter names <c>sender</c>, <c>target</c>, <c>content</c>.</item>
/// <item>Protocol 759 only: <c>{chat:{decoration?:{...}},overlay:{...},narration:{...}}</c>, with
/// parameter names <c>sender</c>, <c>team_name</c>, <c>content</c>; the chat display may carry NO decoration, which means "show the body undecorated".</item>
/// </list>
/// <para>The boundary is protocol 759 versus 760. Protocol 759 uses <c>team_name</c> and the nested <c>decoration</c> form; protocol 760 and later use the two-component form with <c>sender</c>/<c>target</c>/<c>content</c> parameters. Reading both shapes here rather than branching on protocol keeps a datapack that ships either form readable, and the two are unambiguous: only 759 nests a <c>decoration</c>.</para>
/// <para>From 1.20.5 a server answers the known-packs handshake by sending an entry with NO element (<see cref="PackedRegistryEntry.Data"/> null) for everything the client said it already had. This client echoes the server's pack list verbatim, so on 766+ EVERY vanilla chat type arrives as an identifier alone and the decoration has to come from the built-in table (<see cref="TryVanillaDefinition"/>). An entry with no element and no built-in is dropped rather than invented, exactly as a dimension type is.</para>
/// </remarks>
internal static class ChatTypes
{
    private const string ChatKey = "chat";
    private const string DecorationKey = "decoration";
    private const string TranslationKeyKey = "translation_key";
    private const string ParametersKey = "parameters";
    private const string StyleKey = "style";

    /// <summary>Builds a chat-type registry from one configuration-phase <c>registry_data</c> packet's entries. Network ids are the entry order, which is how vanilla assigns them. Returns null when nothing usable was present.</summary>
    public static Registry<ChatTypeDefinition>? FromPackedEntries(
        IReadOnlyList<PackedRegistryEntry> entries, ComponentWireEra era)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var builder = new RegistryBuilder<ChatTypeDefinition>(RegistryIds.ChatType, entries.Count);
        for (int id = 0; id < entries.Count; id++)
        {
            PackedRegistryEntry entry = entries[id];
            if (TryReadElement(entry.Data, era, out ChatTypeDefinition definition)
                || TryVanillaDefinition(entry.Id, out definition))
                builder.Add(id, entry.Id, definition);

        }

        return builder.Count == 0 ? null : builder.Build();
    }

    /// <summary>Builds a chat-type registry from the 1.19-1.20.1 JoinGame registry blob (also the 1.20.2/1.20.4 configuration-phase blob, which has the same shape). Returns null when the blob carries no usable <c>minecraft:chat_type</c> section.</summary>
    public static Registry<ChatTypeDefinition>? FromJoinGameRegistry(NbtTag? blob, ComponentWireEra era)
    {
        if (blob is not NbtCompound root)
            return null;

        NbtCompound? section = root.GetCompound(RegistryIds.ChatType.ToString());
        NbtList? values = section?.GetList("value");
        if (values is null)
            return null;

        var builder = new RegistryBuilder<ChatTypeDefinition>(RegistryIds.ChatType, values.Count);
        foreach (NbtTag item in values)
        {
            if (item is not NbtCompound record
                || !Identifier.TryParse(record.GetString("name"), out Identifier name)
                || !record.ContainsKey("id"))
                continue;

            if (TryReadElement(record.GetCompound("element"), era, out ChatTypeDefinition definition)
                || TryVanillaDefinition(name, out definition))
                builder.Add(record.GetInt("id"), name, definition);

        }

        return builder.Count == 0 ? null : builder.Build();
    }

    /// <summary>Reads one chat-type datapack compound. Returns false when the tag is not a compound or carries no <c>chat</c> member at all, so an absent element is never mistaken for an undecorated type.</summary>
    public static bool TryReadElement(NbtTag? tag, ComponentWireEra era, out ChatTypeDefinition definition)
    {
        definition = null!;
        if (tag is not NbtCompound element || element.GetCompound(ChatKey) is not { } chat)
            return false;

        // 759 wraps the decoration in a TextDisplay; 760+ IS the decoration. Only 759 has the nested key, so the shape is decided by the data rather than by a protocol number.
        NbtCompound? decoration = chat.GetCompound(DecorationKey) ?? (chat.ContainsKey(TranslationKeyKey) ? chat : null);
        if (decoration is null)
        {
            // Protocol 759 permits a chat type with no decoration, meaning the content is shown bare.
            definition = new ChatTypeDefinition(null);
            return true;
        }

        if (!decoration.TryGet(TranslationKeyKey, out NbtString? key)
            || decoration.GetList(ParametersKey) is not { } parameters)
            return false;

        var selected = new List<ChatDecorationParameter>(parameters.Count);
        foreach (NbtTag parameter in parameters)
        {
            if (parameter is not NbtString name || !TryParseParameter(name.Value, out ChatDecorationParameter value))
            {
                // An unmodelled parameter name would silently shift every later argument by one slot, so the whole decoration is refused rather than composed from the wrong values.
                return false;
            }

            selected.Add(value);
        }

        definition = new ChatTypeDefinition(
            new ChatDecorationDefinition(key.Value, selected, ReadStyle(decoration.GetCompound(StyleKey), era)));
        return true;
    }

    /// <summary>The vanilla built-in decoration for a chat-type identifier, or false when the identifier is not one of the seven built-ins (a datapack type, which must come from the server or not at all).</summary>
    /// <remarks>
    /// <para>The built-in table is stable across supported modern versions. Only the <c>chat</c> half is taken; narration has no consumer here.</para>
    /// <para>This table is reached only for an entry the server sent with its element elided, which happens only from 1.20.5. Protocol 759's built-in set is a different one (it has <c>system</c>, <c>game_info</c>, <c>msg_command</c>, <c>team_msg_command</c> and <c>tellraw_command</c>), but 759 has no known-packs handshake and always sends the elements, so that set is never needed.</para>
    /// </remarks>
    public static bool TryVanillaDefinition(Identifier name, out ChatTypeDefinition definition)
    {
        switch (name.ToString())
        {
            case "minecraft:chat":
                definition = WithSender("chat.type.text");
                return true;
            case "minecraft:say_command":
                definition = WithSender("chat.type.announcement");
                return true;
            case "minecraft:emote_command":
                definition = WithSender("chat.type.emote");
                return true;
            case "minecraft:msg_command_incoming":
                definition = Decorated(
                    "commands.message.display.incoming",
                    [ChatDecorationParameter.Sender, ChatDecorationParameter.Content],
                    GrayItalic);
                return true;
            case "minecraft:msg_command_outgoing":
                definition = Decorated(
                    "commands.message.display.outgoing",
                    [ChatDecorationParameter.Target, ChatDecorationParameter.Content],
                    GrayItalic);
                return true;
            case "minecraft:team_msg_command_incoming":
                definition = TeamMessage("chat.type.team.text");
                return true;
            case "minecraft:team_msg_command_outgoing":
                definition = TeamMessage("chat.type.team.sent");
                return true;
            default:
                definition = null!;
                return false;
        }
    }

    /// <summary>Vanilla's <c>incomingDirectMessage</c>/<c>outgoingDirectMessage</c> style: gray and italic (an otherwise empty gray italic style).</summary>
    private static Style GrayItalic => new() { Color = TextColor.Gray, Italic = true };

    private static ChatTypeDefinition WithSender(string key) =>
        Decorated(key, [ChatDecorationParameter.Sender, ChatDecorationParameter.Content], Style.Empty);

    private static ChatTypeDefinition TeamMessage(string key) =>
        Decorated(
            key,
            [ChatDecorationParameter.Target, ChatDecorationParameter.Sender, ChatDecorationParameter.Content],
            Style.Empty);

    private static ChatTypeDefinition Decorated(
        string key, ChatDecorationParameter[] parameters, Style style) =>
        new(new ChatDecorationDefinition(key, parameters, style));

    private static bool TryParseParameter(string name, out ChatDecorationParameter parameter)
    {
        switch (name)
        {
            case "sender":
                parameter = ChatDecorationParameter.Sender;
                return true;
            case "target":
            case "team_name":
                parameter = ChatDecorationParameter.Target;
                return true;
            case "content":
                parameter = ChatDecorationParameter.Content;
                return true;
            default:
                parameter = default;
                return false;
        }
    }

    /// <summary>Reads the decoration's style. The style compound is exactly the style half of a component compound, so it is read by the component reader that already models every style field rather than by a second parser that would drift from it. A style that fails to parse degrades to the empty style, which is vanilla's own default for the optional field, instead of dropping the decoration.</summary>
    private static Style ReadStyle(NbtCompound? style, ComponentWireEra era)
    {
        if (style is null || style.IsEmpty)
            return Style.Empty;

        var probe = new NbtCompound();
        foreach (KeyValuePair<string, NbtTag> field in style)
            probe.Put(field.Key, field.Value);

        probe.PutString("text", string.Empty);
        try
        {
            return ComponentNbt.From(probe, era).Style;
        }
        catch (ComponentFormatException)
        {
            return Style.Empty;
        }
    }
}
