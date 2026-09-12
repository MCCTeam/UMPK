using System.Collections.Immutable;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Commands;

/// <summary>One node of a reconstructed server command tree: a typed, cycle-safe view over the wire <see cref="CommandNodeData"/> graph. Redirects are resolved to the target node reference (never a copy), so a self-referential redirect (e.g. <c>/execute</c> pointing back at the command root) is representable without infinite expansion. Children are the effective children a parser walk follows: a node's own children, or, when it is a pure redirect with no children of its own, the redirect target's children.</summary>
public sealed class CommandNodeView
{
    private CommandNodeView[] _children = [];

    internal CommandNodeView(int index, CommandNodeKind kind, string? name, CommandArgumentData? argument, bool executable, bool restricted)
    {
        Index = index;
        Kind = kind;
        Name = name;
        Argument = argument;
        IsExecutable = executable;
        IsRestricted = restricted;
    }

    /// <summary>The wire index of this node in the source <see cref="CommandTreeData"/>.</summary>
    public int Index { get; }

    /// <summary>The node kind (root/literal/argument).</summary>
    public CommandNodeKind Kind { get; }

    /// <summary>The literal or argument name; null for the root.</summary>
    public string? Name { get; }

    /// <summary>The argument payload for an argument node; null otherwise.</summary>
    public CommandArgumentData? Argument { get; }

    /// <summary>Whether a command executes at this node.</summary>
    public bool IsExecutable { get; }

    /// <summary>Whether the server marked this node as requiring elevated permissions (<c>FLAG_RESTRICTED = 32</c>, 1.21.6+). It is a property of the NODE, not of this session: the server derives it by testing the node against a source with no permissions, so an operator who may run the command still receives it flagged. It therefore does NOT gate completion (vanilla completes through restricted nodes); a host can use it to warn before sending, which is the only thing vanilla's own client does with it.</summary>
    public bool IsRestricted { get; }

    /// <summary>The node this node redirects to, or null when it does not redirect.</summary>
    public CommandNodeView? Redirect { get; private set; }

    /// <summary>The direct children of this node.</summary>
    public IReadOnlyList<CommandNodeView> Children => _children;

    /// <summary>The children a parse walk follows from this node: its own children, or the redirect target's children when this node redirects and has none of its own (matching vanilla resolution, where a redirect makes the target's children reachable).</summary>
    public IReadOnlyList<CommandNodeView> EffectiveChildren =>
        _children.Length == 0 && Redirect is not null ? Redirect._children : _children;

    /// <summary>The parser identifier of an argument node (e.g. <c>brigadier:string</c>), or null.</summary>
    public string? ParserName => Argument?.ParserName;

    internal void SetChildren(CommandNodeView[] children) => _children = children;

    internal void SetRedirect(CommandNodeView? redirect) => Redirect = redirect;
}
