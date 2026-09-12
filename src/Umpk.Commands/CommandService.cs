using System.Collections.Immutable;
using Brigadier.NET;
using Brigadier.NET.Context;
using Brigadier.NET.Exceptions;
using Brigadier.NET.Suggestion;
using Brigadier.NET.Tree;

namespace Umpk.Commands;

/// <summary>UMPK's command dispatcher wrapper. It owns a compiled, immutable <see cref="CommandDispatcher{TSource}"/> snapshot rebuilt from the live scopes; the Brigadier type never appears on this surface. Registration happens in scopes (<see cref="CreateScope"/>); unregistration is scope disposal plus a rebuild, using only public Brigadier API.</summary>
/// <remarks>Host-ownable, not session-scoped: the public parameterless constructor lets a host new() this up directly and hold it for the process lifetime, registering long-lived commands in one scope and opening/closing a shorter-lived scope per session around it (see <see cref="ICommandRegistrationScope{TSource}"/>). UMPK itself routes no input into this service; nothing here reads console lines, chat messages, or any other input source on its own. A host that wants a registered command to actually execute must call <see cref="ExecuteAsync"/> itself with whatever text it decided counts as a command.</remarks>
/// <typeparam name="TSource">The command source type carrying the session/host context.</typeparam>
public sealed class CommandService<TSource>
    where TSource : ICommandSource
{
    private readonly object _gate = new();
    private readonly List<RegistrationScope> _scopes = [];
    private volatile CommandDispatcher<TSource> _dispatcher = new();

    /// <summary>Creates a command service with an empty command tree.</summary>
    public CommandService()
    {
    }

    /// <summary>Opens a registration scope owned by <paramref name="ownerId"/>.</summary>
    public ICommandRegistrationScope<TSource> CreateScope(string ownerId)
    {
        ArgumentNullException.ThrowIfNull(ownerId);
        var scope = new RegistrationScope(this, ownerId);
        lock (_gate)
            _scopes.Add(scope);

        return scope;
    }

    /// <summary>Parses and executes <paramref name="input"/> from the given source.</summary>
    public Task<CommandResult> ExecuteAsync(string input, TSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        CommandDispatcher<TSource> dispatcher = _dispatcher;
        try
        {
            ParseResults<TSource> parse = dispatcher.Parse(input, source);
            int value = dispatcher.Execute(parse);
            return Task.FromResult(CommandResult.Ok(value));
        }
        catch (CommandSyntaxException ex)
        {
            return Task.FromResult(CommandResult.Failure(ex.Message, ex.Cursor));
        }
    }

    /// <summary>Computes local completions for locally registered commands at the cursor.</summary>
    public async Task<CompletionResult> CompleteAsync(
        string input,
        int cursor,
        TSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(source);
        if (cursor < 0 || cursor > input.Length)
            throw new ArgumentOutOfRangeException(nameof(cursor));

        cancellationToken.ThrowIfCancellationRequested();

        CommandDispatcher<TSource> dispatcher = _dispatcher;
        ParseResults<TSource> parse = dispatcher.Parse(input, source);
        Suggestions suggestions = await GetFilteredSuggestionsAsync(parse, cursor, source).ConfigureAwait(false);

        if (suggestions.IsEmpty())
            return CompletionResult.Empty;

        var builder = ImmutableArray.CreateBuilder<CompletionSuggestion>(suggestions.List.Count);
        foreach (Suggestion s in suggestions.List)
            builder.Add(new CompletionSuggestion(s.Text, s.Range.Start, s.Range.End, s.Tooltip?.String));

        return new CompletionResult(suggestions.Range.Start, suggestions.Range.End, builder.MoveToImmutable());
    }

    /// <summary>The smart-usage listing of every top-level command <paramref name="source"/> may use, one <see cref="CommandUsage"/> per root literal, sorted by <see cref="CommandUsage.Name"/> with ordinal comparison. Brigadier's own <c>GetSmartUsage</c> hands back a plain dictionary with no guaranteed iteration order; a library surface must not leak that, so this sorts before returning. A command a <c>Requires</c> predicate hides from <paramref name="source"/> is omitted entirely, matching the filtering <see cref="CompleteAsync"/> already applies.</summary>
    public ImmutableArray<CommandUsage> GetUsage(TSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        CommandDispatcher<TSource> dispatcher = _dispatcher;
        IDictionary<CommandNode<TSource>, string> usage = dispatcher.GetSmartUsage(dispatcher.Root, source);
        var builder = ImmutableArray.CreateBuilder<CommandUsage>(usage.Count);
        foreach (KeyValuePair<CommandNode<TSource>, string> entry in usage)
            builder.Add(new CommandUsage(entry.Key.Name, entry.Value));

        builder.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return builder.MoveToImmutable();
    }

    /// <summary>Every usage line for <paramref name="commandName"/>'s whole subtree, restricted to branches <paramref name="source"/> may use. Empty when no such top-level command is registered. Each line omits <paramref name="commandName"/> itself (Brigadier's own shape: the caller already knows the command it asked about); a branch that redirects renders as <c>child -> target</c> rather than expanding the target's subtree again.</summary>
    public ImmutableArray<string> GetUsage(string commandName, TSource source)
    {
        ArgumentNullException.ThrowIfNull(commandName);
        ArgumentNullException.ThrowIfNull(source);

        CommandDispatcher<TSource> dispatcher = _dispatcher;
        CommandNode<TSource>? node = dispatcher.Root.GetChild(commandName);
        if (node is null)
            return ImmutableArray<string>.Empty;

        return ImmutableArray.Create(dispatcher.GetAllUsage(node, source, restricted: true));
    }

    /// <summary>Computes completion suggestions while honoring node requirements. Brigadier's own <see cref="CommandDispatcher{TSource}.GetCompletionSuggestions(ParseResults{TSource}, int)"/> lists suggestions for every child of the frontier node without consulting <see cref="CommandNode{TSource}.CanUse"/> (matching Java Brigadier, which relies on the server pre-filtering the tree it sends). UMPK filters here so a <c>Requires</c> predicate hides a node from completion, mirroring what a vanilla client sees. This uses public Brigadier API only.</summary>
    private static async Task<Suggestions> GetFilteredSuggestionsAsync(
        ParseResults<TSource> parse,
        int cursor,
        TSource source)
    {
        CommandContextBuilder<TSource> context = parse.Context;
        SuggestionContext<TSource> nodeBeforeCursor = context.FindSuggestionContext(cursor);
        CommandNode<TSource> parent = nodeBeforeCursor.Parent;
        int start = Math.Min(nodeBeforeCursor.StartPos, cursor);

        string fullInput = parse.Reader.String;
        string truncatedInput = fullInput.Substring(0, cursor);
        string truncatedInputLowerCase = truncatedInput.ToLowerInvariant();

        var futures = new List<Task<Suggestions>>();
        foreach (CommandNode<TSource> node in parent.Children)
        {
            if (!node.CanUse(source))
                continue;

            try
            {
                futures.Add(node.ListSuggestions(
                    context.Build(truncatedInput),
                    new SuggestionsBuilder(truncatedInput, truncatedInputLowerCase, start)));
            }
            catch (CommandSyntaxException)
            {
                // A child that cannot produce suggestions for this input contributes nothing.
            }
        }

        await Task.WhenAll(futures).ConfigureAwait(false);
        return Suggestions.Merge(fullInput, futures.Select(f => f.Result).ToArray());
    }

    private void Rebuild()
    {
        // Caller holds _gate.
        var dispatcher = new CommandDispatcher<TSource>();
        RootCommandNode<TSource> root = dispatcher.Root;
        foreach (RegistrationScope scope in _scopes)
            foreach (Action<CommandBuilder<TSource>> registration in scope.Registrations)
            {
                var builder = new CommandBuilder<TSource>();
                registration(builder);
                foreach (LiteralCommandNode<TSource> node in builder.BuildRoots())
                    root.AddChild(node);

            }

        _dispatcher = dispatcher;
    }

    private void RemoveScope(RegistrationScope scope)
    {
        lock (_gate)
            if (_scopes.Remove(scope))
                Rebuild();

    }

    private sealed class RegistrationScope : ICommandRegistrationScope<TSource>
    {
        private readonly CommandService<TSource> _owner;
        private readonly List<Action<CommandBuilder<TSource>>> _registrations = [];
        private bool _disposed;

        internal RegistrationScope(CommandService<TSource> owner, string ownerId)
        {
            _owner = owner;
            OwnerId = ownerId;
        }

        public string OwnerId { get; }

        internal IReadOnlyList<Action<CommandBuilder<TSource>>> Registrations => _registrations;

        public void Register(Action<CommandBuilder<TSource>> build)
        {
            ArgumentNullException.ThrowIfNull(build);
            lock (_owner._gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _registrations.Add(build);
                _owner.Rebuild();
            }
        }

        public void Dispose()
        {
            lock (_owner._gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
            }

            _owner.RemoveScope(this);
        }
    }
}
