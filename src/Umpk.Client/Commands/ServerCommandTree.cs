using System.Collections.Immutable;
using Umpk.Commands;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Commands;

/// <summary>A reconstructed server command tree: the typed, cycle-safe graph the vanilla client builds from a <see cref="CommandTreeData"/>, joined with an <see cref="ArgumentTypeRegistry"/> so argument nodes carry resolved parser names. It supports a local completion walk (literals plus locally resolvable argument types), ask-server detection (custom-suggestion providers or argument types needing live server data), and signed-argument span extraction for secure chat.</summary>
public sealed class ServerCommandTree
{
    private readonly CommandNodeView[] _nodes;

    private ServerCommandTree(CommandNodeView root, CommandNodeView[] nodes)
    {
        Root = root;
        _nodes = nodes;
    }

    /// <summary>The root node of the reconstructed tree.</summary>
    public CommandNodeView Root { get; }

    /// <summary>Every reconstructed node, indexed by wire index.</summary>
    public IReadOnlyList<CommandNodeView> Nodes => _nodes;

    /// <summary>Reconstructs a typed tree from the wire node graph. Nodes are created first, then children and redirects are wired by index, so cycles (a redirect pointing back at an ancestor) are safe. The registry resolves parser names on argument nodes; the wire data already carries the resolved name from decode, so this is a straight lift.</summary>
    public static ServerCommandTree Build(CommandTreeData wire, ArgumentTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(wire);
        ArgumentNullException.ThrowIfNull(registry);

        int count = wire.Nodes.Length;
        var nodes = new CommandNodeView[count];
        for (int i = 0; i < count; i++)
        {
            CommandNodeData data = wire.Nodes[i];
            nodes[i] = new CommandNodeView(i, data.Kind, data.Name, data.Argument, data.IsExecutable, data.IsRestricted);
        }

        for (int i = 0; i < count; i++)
        {
            CommandNodeData data = wire.Nodes[i];
            var children = new CommandNodeView[data.Children.Length];
            for (int c = 0; c < data.Children.Length; c++)
            {
                int childIndex = data.Children[c];
                if (childIndex < 0 || childIndex >= count)
                    throw new ArgumentException($"Command node {i} references out-of-range child {childIndex}.", nameof(wire));

                children[c] = nodes[childIndex];
            }

            nodes[i].SetChildren(children);

            if (data.HasRedirect)
            {
                if (data.RedirectIndex < 0 || data.RedirectIndex >= count)
                    throw new ArgumentException($"Command node {i} redirects to out-of-range index {data.RedirectIndex}.", nameof(wire));

                nodes[i].SetRedirect(nodes[data.RedirectIndex]);
            }
        }

        return new ServerCommandTree(nodes[wire.RootIndex], nodes);
    }

    /// <summary>Computes local completions at the cursor: literal children whose text extends the token under the cursor. Argument tokens that are locally resolvable contribute nothing here (there is no static value set to complete from); nodes needing server data are handled by <see cref="NeedsServerCompletion"/> and the client's TabComplete round trip.</summary>
    /// <remarks>A RESTRICTED node (<c>FLAG_RESTRICTED = 32</c>, 1.21.6 onward) remains eligible for completion. The flag marks commands that require elevation; it does not state that the current player lacks permission. Permission confirmation occurs when the command is sent, not while completing it.</remarks>
    public CompletionResult CompleteLocally(string input, int cursor)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (cursor < 0 || cursor > input.Length)
            throw new ArgumentOutOfRangeException(nameof(cursor));

        WalkState state = Walk(input, cursor);
        if (state.Frontier is null)
            return CompletionResult.Empty;

        int tokenStart = state.LastTokenStart;
        string prefix = input.Substring(tokenStart, cursor - tokenStart);

        var builder = ImmutableArray.CreateBuilder<CompletionSuggestion>();
        foreach (CommandNodeView child in state.Frontier.EffectiveChildren)
        {
            if (child.Kind != CommandNodeKind.Literal || child.Name is null)
                continue;

            if (child.Name.StartsWith(prefix, StringComparison.Ordinal))
                builder.Add(new CompletionSuggestion(child.Name, tokenStart, cursor, null));

        }

        return builder.Count == 0
            ? CompletionResult.Empty
            : new CompletionResult(tokenStart, cursor, builder.ToImmutable());
    }

    /// <summary>Whether completion at the cursor requires the server round trip: the token under the cursor falls on an argument node whose suggestions are server-provided (an <c>ask_server</c> custom suggestion provider, or an argument type whose values only the server knows).</summary>
    /// <remarks>Restricted argument nodes count too, for the same reason <see cref="CompleteLocally"/> offers restricted literals: the flag marks a command as needing elevation, not as forbidden to this session, and vanilla's suggestion source holds the permission that satisfies it. Skipping them stopped the round trip from ever being made under an elevated command, which is indistinguishable from a server that answered nothing.</remarks>
    public bool NeedsServerCompletion(string input, int cursor)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (cursor < 0 || cursor > input.Length)
            throw new ArgumentOutOfRangeException(nameof(cursor));

        WalkState state = Walk(input, cursor);
        if (state.Frontier is null)
            return false;

        foreach (CommandNodeView child in state.Frontier.EffectiveChildren)
            if (child.Kind == CommandNodeKind.Argument && ArgumentNeedsServer(child))
                return true;

        return false;
    }

    /// <summary>Extracts the argument spans of <paramref name="command"/> that must be signed: the values of argument nodes whose parser is a signed argument (<c>minecraft:message</c>). The command is the text without a leading slash. Returns spans in left-to-right order.</summary>
    public IReadOnlyList<SignedArgumentSpan> GetSignedArguments(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var result = new List<SignedArgumentSpan>();
        WalkForParse(command, node =>
        {
            if (node.MatchedNode.Kind == CommandNodeKind.Argument &&
                node.MatchedNode.ParserName is { } parser &&
                IsSignedArgument(parser) &&
                node.MatchedNode.Name is { } name)
            {
                string value = command.Substring(node.Start, node.Length);
                result.Add(new SignedArgumentSpan(name, value, node.Start, node.Length));
            }
        });

        return result;
    }

    private static bool IsSignedArgument(string parserName) =>
        string.Equals(parserName, "minecraft:message", StringComparison.Ordinal);

    private static bool ArgumentNeedsServer(CommandNodeView argument)
    {
        // A custom suggestion provider is by definition server-driven unless it is a known local one. Vanilla's local providers are ask_server (round trip), summonable entities, and available sounds/biomes; only the ask_server provider needs the live exchange, and unknown providers default to ask_server. All of these need the server, so any custom suggestion provider marks the node as server-completed.
        if (argument.Argument?.SuggestionProvider is not null)
            return true;

        // Argument types whose value space only the live server knows (selectors, world coordinates, block/item registry entries). Locally resolvable types (bool, plain strings, numbers) do not.
        return argument.ParserName switch
        {
            "minecraft:entity" or "minecraft:game_profile" or "minecraft:score_holder"
                or "minecraft:objective" or "minecraft:team" or "minecraft:message" => true,
            _ => false,
        };
    }

    private readonly record struct WalkState(CommandNodeView? Frontier, int LastTokenStart);

    private readonly record struct MatchedToken(CommandNodeView MatchedNode, int Start, int Length);

    /// <summary>Walks the tree along <paramref name="input"/> up to <paramref name="cursor"/>, returning the frontier node whose children complete the token under the cursor and the start index of that token. Whitespace-delimited; greedy string/message arguments consume the remainder.</summary>
    private WalkState Walk(string input, int cursor)
    {
        CommandNodeView current = Root;
        int pos = 0;

        while (true)
        {
            // Skip spaces.
            while (pos < cursor && input[pos] == ' ')
                pos++;

            int tokenStart = pos;
            if (pos >= cursor)
                return new WalkState(current, tokenStart);

            // Read the token up to the next space or the cursor.
            int tokenEnd = pos;
            while (tokenEnd < input.Length && input[tokenEnd] != ' ')
                tokenEnd++;

            // The token under the cursor (not yet followed by a space): complete from here.
            if (tokenEnd >= cursor)
                return new WalkState(current, tokenStart);

            string token = input.Substring(tokenStart, tokenEnd - tokenStart);
            CommandNodeView? next = MatchChild(current, token, input, tokenStart, out int consumedEnd);
            if (next is null)
            {
                // No child matches; the frontier is the current node so its children can still be listed.
                return new WalkState(current, tokenStart);
            }

            current = next;
            pos = consumedEnd;
        }
    }

    /// <summary>Walks the whole command, invoking <paramref name="onMatch"/> for each matched node with its consumed span. Used for signed-argument extraction (full parse, no cursor).</summary>
    private void WalkForParse(string command, Action<MatchedToken> onMatch)
    {
        CommandNodeView current = Root;
        int pos = 0;

        while (pos < command.Length)
        {
            while (pos < command.Length && command[pos] == ' ')
                pos++;

            if (pos >= command.Length)
                break;

            int tokenStart = pos;
            int tokenEnd = pos;
            while (tokenEnd < command.Length && command[tokenEnd] != ' ')
                tokenEnd++;

            string token = command.Substring(tokenStart, tokenEnd - tokenStart);
            CommandNodeView? next = MatchChild(current, token, command, tokenStart, out int consumedEnd);
            if (next is null)
                break;

            onMatch(new MatchedToken(next, tokenStart, consumedEnd - tokenStart));
            current = next;
            pos = consumedEnd;
        }
    }

    /// <summary>Matches a token against a node's effective children, preferring a literal exact match, then the first argument child. Returns the matched child and the input index the match consumed to (greedy string/message arguments consume the rest of the line).</summary>
    private static CommandNodeView? MatchChild(CommandNodeView node, string token, string input, int tokenStart, out int consumedEnd)
    {
        foreach (CommandNodeView child in node.EffectiveChildren)
            if (child.Kind == CommandNodeKind.Literal &&
                string.Equals(child.Name, token, StringComparison.Ordinal))
            {
                consumedEnd = tokenStart + token.Length;
                return child;
            }

        foreach (CommandNodeView child in node.EffectiveChildren)
        {
            if (child.Kind != CommandNodeKind.Argument)
                continue;

            if (ConsumesRemainder(child))
            {
                consumedEnd = input.Length;
                return child;
            }

            consumedEnd = tokenStart + token.Length;
            return child;
        }

        consumedEnd = tokenStart + token.Length;
        return null;
    }

    private static bool ConsumesRemainder(CommandNodeView argument)
    {
        if (string.Equals(argument.ParserName, "minecraft:message", StringComparison.Ordinal))
            return true;

        // A greedy brigadier:string consumes the remainder of the line.
        return argument.Argument?.Properties is StringArgumentProperties { Kind: BrigadierStringKind.GreedyPhrase };
    }
}
