using System.Collections.Immutable;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>The declare-commands (<c>minecraft:commands</c>) codec, one era member per wire era. The wire shape is byte-identical across 770 and 776 (the 26.2 packet only adds a <c>FLAG_RESTRICTED</c> flag bit, preserved verbatim in <see cref="CommandNodeData.Flags"/>); the only version difference is which <see cref="ArgumentTypeRegistry"/> resolves parser ids to names and their property payloads. Each era member closes over its registry, so no version comparison happens on the hot path.</summary>
/// <remarks>Each node has a flags byte, a VarInt children array, an optional redirect VarInt, and a literal or argument stub; the list ends with the root index. The 26.2 read shape is unchanged.</remarks>
public static partial class CommandTreeCodecs
{
    private const byte MaskType = 0x03;

    private const byte FlagRedirect = 0x08;

    private const byte FlagCustomSuggestions = 0x10;

    /// <summary>The 1.21.5 (protocol 770) declare-commands codec.</summary>
    public static PacketCodec<ClientboundCommandsPacket> V1_21_5 { get; } = Build(ArgumentTypeRegistry.V1_21_5);

    /// <summary>The 26.2 (protocol 776) declare-commands codec.</summary>
    public static PacketCodec<ClientboundCommandsPacket> V26_2 { get; } = Build(ArgumentTypeRegistry.V26_2);

    // Internal (not private) so the per-version era members can bind their own argument-type tables. timeHasMin is the second per-era axis: minecraft:time gained its int min at protocol 762, so the 759-761 members must read and write an EMPTY payload for it (see ArgumentTypeRegistry.ReadProperties).
    internal static PacketCodec<ClientboundCommandsPacket> Build(ArgumentTypeRegistry registry, bool timeHasMin = true) =>
        PacketCodec<ClientboundCommandsPacket>.Of(
            (ref PacketWriter w, ClientboundCommandsPacket p, PacketCodecContext _) => Encode(ref w, p.Tree, registry, timeHasMin),
            (ref PacketReader r, PacketCodecContext _) => new ClientboundCommandsPacket(Decode(ref r, registry, timeHasMin)),
            WireShape.Of("varint*node,varint", $"{registry.ShapeToken},timemin={(timeHasMin ? 1 : 0)}"));

    private static CommandTreeData Decode(ref PacketReader r, ArgumentTypeRegistry registry, bool timeHasMin)
    {
        int count = r.ReadVarInt();
        if (count < 0 || count > r.Remaining + 1)
            throw new ProtocolViolationException(
                $"Command tree node count {count} is implausible for {r.Remaining} remaining bytes.");

        var nodes = ImmutableArray.CreateBuilder<CommandNodeData>(count);
        for (int i = 0; i < count; i++)
            nodes.Add(ReadNode(ref r, registry, timeHasMin));

        int rootIndex = r.ReadVarInt();
        if (rootIndex < 0 || rootIndex >= count)
            throw new ProtocolViolationException($"Command tree root index {rootIndex} is out of range [0, {count}).");

        return new CommandTreeData(nodes.MoveToImmutable(), rootIndex);
    }

    private static CommandNodeData ReadNode(ref PacketReader r, ArgumentTypeRegistry registry, bool timeHasMin)
    {
        byte flags = r.ReadByte();
        int[] children = r.ReadList(static (ref PacketReader sr) => sr.ReadVarInt());
        int redirect = (flags & FlagRedirect) != 0 ? r.ReadVarInt() : -1;

        var kind = (CommandNodeKind)(flags & MaskType);
        string? name = null;
        CommandArgumentData? argument = null;

        switch (kind)
        {
            case CommandNodeKind.Literal:
                name = r.ReadString();
                break;
            case CommandNodeKind.Argument:
                name = r.ReadString();
                argument = ReadArgument(ref r, flags, registry, timeHasMin);
                break;
            case CommandNodeKind.Root:
                break;
            default:
                throw new ProtocolViolationException($"Unknown command node type {(int)kind} in declare-commands.");
        }

        return new CommandNodeData(kind, flags, [.. children], redirect, name, argument);
    }

    private static CommandArgumentData ReadArgument(ref PacketReader r, byte flags, ArgumentTypeRegistry registry, bool timeHasMin)
    {
        int parserId = r.ReadVarInt();
        string? parserName = registry.NameFromId(parserId);
        if (parserName is null)
        {
            // The property payload has no length prefix, so an unknown parser cannot be skipped while keeping the following nodes aligned. Reject the frame instead of desynchronizing the tree.
            //
            // Blast radius: servers that register custom Brigadier argument types (Paper/Fabric plugins and mods do this routinely with ids no vanilla table carries) trip this on the first such node, aborting the whole Commands packet. The vanilla client only tolerates those because a modded client negotiates the matching argument types; a protocol library will meet these servers. A frame-exact rejection is the only safe result without the matching parser.
            throw new ProtocolViolationException(
                $"Declare-commands references unknown argument parser id {parserId} (registry has {registry.Count} entries); " +
                "cannot decode its property payload without desynchronizing the node list.");
        }

        ArgumentParserProperties? properties = ArgumentTypeRegistry.ReadProperties(ref r, parserName, timeHasMin);
        Identifier? suggestion = (flags & FlagCustomSuggestions) != 0
            ? Identifier.Parse(r.ReadString())
            : null;

        return new CommandArgumentData(parserId, parserName, properties, suggestion);
    }

    private static void Encode(ref PacketWriter w, CommandTreeData tree, ArgumentTypeRegistry registry, bool timeHasMin)
    {
        w.WriteVarInt(tree.Nodes.Length);
        foreach (CommandNodeData node in tree.Nodes)
            WriteNode(ref w, node, registry, timeHasMin);

        w.WriteVarInt(tree.RootIndex);
    }

    private static void WriteNode(ref PacketWriter w, CommandNodeData node, ArgumentTypeRegistry registry, bool timeHasMin)
    {
        w.WriteByte(node.Flags);
        w.WriteList(node.Children, static (ref PacketWriter sw, int child) => sw.WriteVarInt(child));
        if (node.HasRedirect)
            w.WriteVarInt(node.RedirectIndex);

        switch (node.Kind)
        {
            case CommandNodeKind.Literal:
                w.WriteString(node.Name ?? string.Empty);
                break;
            case CommandNodeKind.Argument:
                w.WriteString(node.Name ?? string.Empty);
                WriteArgument(ref w, node, registry, timeHasMin);
                break;
            case CommandNodeKind.Root:
                break;
            default:
                throw new ProtocolViolationException($"Unknown command node type {(int)node.Kind} in declare-commands.");
        }
    }

    private static void WriteArgument(ref PacketWriter w, CommandNodeData node, ArgumentTypeRegistry registry, bool timeHasMin)
    {
        CommandArgumentData argument = node.Argument
            ?? throw new ProtocolViolationException("Argument command node has no argument payload.");

        if (argument.ParserName is null)
        {
            // Symmetric with the decode side: an unknown parser has no length-prefixed payload, so there is nothing to re-serialize. The decoder never produces this state (it throws first); an argument node with no resolved parser can only be a hand-built stub, which is not encodable.
            throw new ProtocolViolationException(
                $"Argument node '{node.Name}' has parser id {argument.ParserId} with no resolved parser name; " +
                "unknown parsers are not re-encodable (their property payload has no length prefix).");
        }

        int parserId = registry.IdFromName(argument.ParserName);
        if (parserId < 0)
            throw new ProtocolViolationException(
                $"Argument parser '{argument.ParserName}' is not in this version's argument-type table.");

        w.WriteVarInt(parserId);
        ArgumentTypeRegistry.WriteProperties(ref w, argument.ParserName,
            argument.Properties ?? ArgumentParserProperties.Empty, timeHasMin);

        if (node.HasCustomSuggestions && argument.SuggestionProvider is { } suggestion)
            w.WriteString(suggestion.ToString());

    }

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareCommands(PacketBindings bindings)
    {
        // The wire node layout is identical everywhere Brigadier exists (1.13+); what changes per era is how an argument node names its parser and how long that parser's property payload is. 1.13-1.18.2 identify parsers by resource-location string, so one string-keyed codec covers that whole span with timeHasMin:false. Numeric ids arrive at 1.19, and from there every step below has a distinct minecraft:command_argument_type registry: 759/760, 761, 762-764, 765, 766-769, 770, 771-775, 776. A step that spans two different registries is not a rounding error: ids resolve to parsers with different payload lengths, and one such node desynchronizes every node after it. minecraft:time additionally gains its int min at 762, which the 759/761 codecs encode as a second era axis.
        bindings.Packet(CommandsPackets.Clientbound.Commands)
            .From(JavaProtocols.V1_13, CommandTreeCodecs.V1_13)
            .From(JavaProtocols.V1_19, CommandTreeCodecs.V1_19)
            .From(JavaProtocols.V1_19_3, CommandTreeCodecs.V1_19_3)
            .From(JavaProtocols.V1_19_4, CommandTreeCodecs.V1_19_4)
            .From(JavaProtocols.V1_20_3, CommandTreeCodecs.V1_20_3)
            .From(JavaProtocols.V1_20_5, CommandTreeCodecs.V1_20_5)
            .From(JavaProtocols.V1_21_5, CommandTreeCodecs.V1_21_5)
            .From(JavaProtocols.V1_21_6, CommandTreeCodecs.V1_21_6)
            .From(JavaProtocols.V26_2, CommandTreeCodecs.V26_2);
    }

    /// <summary>The protocol-759/760 (1.19, 1.19.1, 1.19.2) declare-commands codec. <c>minecraft:time</c> is a payload-free singleton here.</summary>
    public static PacketCodec<ClientboundCommandsPacket> V1_19 { get; } = Build(ArgumentTypeRegistry.V759, timeHasMin: false);

    /// <summary>The protocol-761 (1.19.3) declare-commands codec; <c>minecraft:time</c> is still payload-free.</summary>
    public static PacketCodec<ClientboundCommandsPacket> V1_19_3 { get; } = Build(ArgumentTypeRegistry.V761, timeHasMin: false);

    /// <summary>The protocol-762/763/764 (1.19.4, 1.20, 1.20.1, 1.20.2) declare-commands codec.</summary>
    public static PacketCodec<ClientboundCommandsPacket> V1_19_4 { get; } = Build(ArgumentTypeRegistry.V764);

    /// <summary>The protocol-765 (1.20.3/4) declare-commands codec.</summary>
    public static PacketCodec<ClientboundCommandsPacket> V1_20_3 { get; } = Build(ArgumentTypeRegistry.V765);

    /// <summary>The protocol-766..769 (1.20.5-1.21.4) declare-commands codec.</summary>
    public static PacketCodec<ClientboundCommandsPacket> V1_20_5 { get; } = Build(ArgumentTypeRegistry.V766);

    /// <summary>Declare-commands codec for protocols 771-775 (1.21.6-26.1 argument-type table).</summary>
    public static PacketCodec<ClientboundCommandsPacket> V1_21_6 { get; } = Build(ArgumentTypeRegistry.V1_21_6);

    /// <summary>Declare-commands codec for protocols 477-578 (string-keyed argument parsers).</summary>
    public static PacketCodec<ClientboundCommandsPacket> V1_13 { get; } =
        PacketCodec<ClientboundCommandsPacket>.Of(
            (ref PacketWriter w, ClientboundCommandsPacket p, PacketCodecContext _) => EncodeStringParser(ref w, p.Tree),
            (ref PacketReader r, PacketCodecContext _) => new ClientboundCommandsPacket(DecodeStringParser(ref r)));

    private static CommandTreeData DecodeStringParser(ref PacketReader r)
    {
        int count = r.ReadVarInt();
        if (count < 0 || count > r.Remaining + 1)
            throw new ProtocolViolationException(
                $"Command tree node count {count} is implausible for {r.Remaining} remaining bytes.");

        var nodes = ImmutableArray.CreateBuilder<CommandNodeData>(count);
        for (int i = 0; i < count; i++)
            nodes.Add(ReadNodeStringParser(ref r));

        int rootIndex = r.ReadVarInt();
        if (rootIndex < 0 || rootIndex >= count)
            throw new ProtocolViolationException($"Command tree root index {rootIndex} is out of range [0, {count}).");

        return new CommandTreeData(nodes.MoveToImmutable(), rootIndex);
    }

    private static CommandNodeData ReadNodeStringParser(ref PacketReader r)
    {
        byte flags = r.ReadByte();
        int[] children = r.ReadList(static (ref PacketReader sr) => sr.ReadVarInt());
        int redirect = (flags & FlagRedirect) != 0 ? r.ReadVarInt() : -1;

        var kind = (CommandNodeKind)(flags & MaskType);
        string? name = null;
        CommandArgumentData? argument = null;

        switch (kind)
        {
            case CommandNodeKind.Literal:
                name = r.ReadString();
                break;
            case CommandNodeKind.Argument:
                name = r.ReadString();
                string parserName = r.ReadString();
                // Pre-1.19 (string-keyed) era: minecraft:time has no min field yet (added 1.19.4), so it carries an empty payload here.
                ArgumentParserProperties? properties = ArgumentTypeRegistry.ReadProperties(ref r, parserName, timeHasMin: false);
                Identifier? suggestion = (flags & FlagCustomSuggestions) != 0
                    ? Identifier.Parse(r.ReadString())
                    : null;
                argument = new CommandArgumentData(-1, parserName, properties, suggestion);
                break;
            case CommandNodeKind.Root:
                break;
            default:
                throw new ProtocolViolationException($"Unknown command node type {(int)kind} in declare-commands.");
        }

        return new CommandNodeData(kind, flags, [.. children], redirect, name, argument);
    }

    private static void EncodeStringParser(ref PacketWriter w, CommandTreeData tree)
    {
        w.WriteVarInt(tree.Nodes.Length);
        foreach (CommandNodeData node in tree.Nodes)
            WriteNodeStringParser(ref w, node);

        w.WriteVarInt(tree.RootIndex);
    }

    private static void WriteNodeStringParser(ref PacketWriter w, CommandNodeData node)
    {
        w.WriteByte(node.Flags);
        w.WriteList(node.Children, static (ref PacketWriter sw, int child) => sw.WriteVarInt(child));
        if (node.HasRedirect)
            w.WriteVarInt(node.RedirectIndex);

        switch (node.Kind)
        {
            case CommandNodeKind.Literal:
                w.WriteString(node.Name ?? string.Empty);
                break;
            case CommandNodeKind.Argument:
                w.WriteString(node.Name ?? string.Empty);
                CommandArgumentData argument = node.Argument
                    ?? throw new ProtocolViolationException("Argument command node has no argument payload.");
                if (argument.ParserName is null)
                    throw new ProtocolViolationException(
                        $"Argument node '{node.Name}' has no resolved parser name; cannot re-encode.");

                w.WriteString(argument.ParserName);
                ArgumentTypeRegistry.WriteProperties(ref w, argument.ParserName,
                    argument.Properties ?? ArgumentParserProperties.Empty, timeHasMin: false);
                if (node.HasCustomSuggestions && argument.SuggestionProvider is { } suggestion)
                    w.WriteString(suggestion.ToString());

                break;
            case CommandNodeKind.Root:
                break;
            default:
                throw new ProtocolViolationException($"Unknown command node type {(int)node.Kind} in declare-commands.");
        }
    }
}
