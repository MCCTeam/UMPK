using Brigadier.NET.Context;
using Brigadier.NET.Suggestion;
using Brigadier.NET.Tree;
using Umpk.Commands.Internal;

namespace Umpk.Commands;

/// <summary>Builds an argument command node parsing a typed value with an optional suggestion provider.</summary>
/// <typeparam name="TSource">The command source type.</typeparam>
/// <typeparam name="T">The parsed argument value type.</typeparam>
public sealed class ArgumentCommandBuilder<TSource, T> : CommandNodeBuilder<TSource>
    where TSource : ICommandSource
    where T : notnull
{
    private readonly IArgumentType<T> _type;
    private ArgumentSuggestionProvider<TSource>? _suggestions;

    internal ArgumentCommandBuilder(string name, IArgumentType<T> type)
    {
        Name = name;
        _type = type;
    }

    /// <summary>The argument name used for typed lookup in the command body.</summary>
    public string Name { get; }

    /// <summary>Attaches a suggestion provider that queries live source state.</summary>
    public ArgumentCommandBuilder<TSource, T> Suggests(ArgumentSuggestionProvider<TSource> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _suggestions = provider;
        return this;
    }

    internal override CommandNode<TSource> Build(
        Dictionary<CommandNodeBuilder<TSource>, CommandNode<TSource>> resolved)
    {
        if (resolved.TryGetValue(this, out var existing))
            return existing;

        if (_type is not BrigadierArgumentType<T> wrapped)
            throw new InvalidOperationException(
                $"Argument type for '{Name}' was not produced by {nameof(Arguments)}.");

        Brigadier.NET.Command<TSource>? command = Execution is { } exec
            ? context => ExecutionBridge<TSource>.Invoke(exec, context)
            : null;

        SuggestionProvider<TSource>? suggestionProvider = _suggestions is { } provider
            ? (context, builder) => InvokeSuggestionsAsync(provider, context, builder)
            : null;

        CommandNode<TSource>? redirect = RedirectTargetBuilder?.Build(resolved);

        var node = new ArgumentCommandNode<TSource, T>(
            Name,
            wrapped.Inner,
            command,
            RequirementPredicate ?? (_ => true),
            redirect,
            modifier: null,
            forks: false,
            suggestionProvider);

        resolved[this] = node;
        ApplyChildren(node, resolved);
        return node;
    }

    private static async Task<Suggestions> InvokeSuggestionsAsync(
        ArgumentSuggestionProvider<TSource> provider,
        CommandContext<TSource> context,
        SuggestionsBuilder builder)
    {
        var sink = new BrigadierSuggestionSink(builder);
        await provider(new BrigadierCommandContext<TSource>(context), sink).ConfigureAwait(false);
        return builder.Build();
    }
}
