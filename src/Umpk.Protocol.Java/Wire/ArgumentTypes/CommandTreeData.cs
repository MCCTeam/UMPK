using System.Collections.Immutable;

namespace Umpk.Protocol.Java.Packets;

/// <summary>The wire-shaped, indexed command-node graph of the <c>minecraft:commands</c> (declare-commands) packet. This is a faithful, lossless model of the on-wire bytes: an ordered list of node entries plus the root index. It preserves everything on the wire (flags, children indices, redirects, parser ids with their typed property payloads, suggestion-provider ids) so a round-trip of a tree built from known parsers is byte-identical, and so <c>Umpk.Client</c> can reconstruct a typed Brigadier tree from it. A parser id outside the version's argument-type table is not decodable (the property payload has no length prefix); the decoder rejects it rather than desynchronizing.</summary>
/// <remarks>The typed reconstruction (joining this wire model with Brigadier semantics) lives in <c>Umpk.Client.Commands.ServerCommandTree</c>, the layer that references both this package and <c>Umpk.Commands</c>. This package owns only the wire model and the <see cref="ArgumentTypeRegistry"/> contract.</remarks>
public sealed record CommandTreeData(ImmutableArray<CommandNodeData> Nodes, int RootIndex)
{
    /// <summary>The node at the given wire index.</summary>
    public CommandNodeData this[int index] => Nodes[index];
}

/// <summary>The kind of a command node, encoded in the low two bits of the flags byte (<c>MASK_TYPE = 3</c>).</summary>
public enum CommandNodeKind : byte
{
    /// <summary>The root node (<c>TYPE_ROOT = 0</c>); no name, no argument.</summary>
    Root = 0,

    /// <summary>A literal node (<c>TYPE_LITERAL = 1</c>); carries a literal name.</summary>
    Literal = 1,

    /// <summary>An argument node (<c>TYPE_ARGUMENT = 2</c>); carries a name, a parser, and a property payload.</summary>
    Argument = 2,
}

/// <summary>A single wire node entry: the flags byte, child indices, redirect target when flagged, and, for literal/argument nodes, the node stub. Fields that a node kind does not carry are their defaults (name null for root, <see cref="Argument"/> null for non-argument nodes), so this one record round-trips every kind losslessly.</summary>
/// <param name="Kind">The node kind (root/literal/argument), from the low two flag bits.</param>
/// <param name="Flags">The raw flags byte, preserved verbatim so unmodeled bits (e.g. 26.2's <c>FLAG_RESTRICTED = 32</c>) survive a re-encode. The kind and the executable/redirect/custom-suggestion bits are derived from it.</param>
/// <param name="Children">The wire indices of this node's children, in wire order.</param>
/// <param name="RedirectIndex">The redirect target index when <see cref="HasRedirect"/>; otherwise -1.</param>
/// <param name="Name">The literal or argument name; null for the root.</param>
/// <param name="Argument">The argument payload for an argument node; null otherwise.</param>
public sealed record CommandNodeData(
    CommandNodeKind Kind,
    byte Flags,
    ImmutableArray<int> Children,
    int RedirectIndex,
    string? Name,
    CommandArgumentData? Argument)
{
    /// <summary>The executable flag (<c>FLAG_EXECUTABLE = 4</c>): a command runs at this node.</summary>
    public bool IsExecutable => (Flags & 0x04) != 0;

    /// <summary>The redirect flag (<c>FLAG_REDIRECT = 8</c>): this node redirects to <see cref="RedirectIndex"/>.</summary>
    public bool HasRedirect => (Flags & 0x08) != 0;

    /// <summary>The custom-suggestions flag (<c>FLAG_CUSTOM_SUGGESTIONS = 16</c>): an argument node has a suggestion provider.</summary>
    public bool HasCustomSuggestions => (Flags & 0x10) != 0;

    /// <summary>The restricted flag (<c>FLAG_RESTRICTED = 32</c>, 1.21.6+): the node's requirement fails for a source with no permissions, so running it needs elevation. It is a property of the node rather than the recipient, so an operator receives the commands they may run flagged too.</summary>
    public bool IsRestricted => (Flags & 0x20) != 0;
}

/// <summary>The argument payload of an <see cref="CommandNodeKind.Argument"/> node: the parser identity plus its serialized property payload and an optional suggestion-provider id. The parser is carried both as the wire registry id (as decoded) and, once resolved through an <see cref="ArgumentTypeRegistry"/>, as its parser identifier and typed <see cref="ArgumentParserProperties"/>. Only known parsers are representable: the wire has no length prefix on the property payload, so the decoder cannot skip an unknown parser and stay frame-aligned. It therefore throws on an unknown parser id (see <c>CommandTreeCodecs.ReadArgument</c>) rather than preserving opaque bytes, and this type has no raw-bytes fallback. <see cref="ParserName"/> is null only for a hand-built, non-serializable stub.</summary>
/// <param name="ParserId">The wire registry id of the parser (per-version; resolved via the registry).</param>
/// <param name="ParserName">The resolved parser identifier (e.g. <c>brigadier:string</c>); never null for a decoded node.</param>
/// <param name="Properties">The typed parser properties for the resolved parser.</param>
/// <param name="SuggestionProvider">The suggestion-provider id when the custom-suggestions flag is set; null otherwise.</param>
public sealed record CommandArgumentData(
    int ParserId,
    string? ParserName,
    ArgumentParserProperties? Properties,
    Identifier? SuggestionProvider);
