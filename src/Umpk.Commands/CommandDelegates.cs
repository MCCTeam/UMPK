namespace Umpk.Commands;

/// <summary>A command body. Returns a numeric result (Mojang convention: a positive value signals success).</summary>
/// <typeparam name="TSource">The command source type.</typeparam>
/// <param name="context">The parsed invocation context.</param>
public delegate ValueTask<int> CommandExecution<TSource>(ICommandContext<TSource> context)
    where TSource : ICommandSource;

/// <summary>Populates completion candidates for an argument node from the live source state.</summary>
/// <typeparam name="TSource">The command source type.</typeparam>
/// <param name="context">The parse context so far, carrying the source.</param>
/// <param name="builder">The builder to add candidates to.</param>
public delegate ValueTask ArgumentSuggestionProvider<TSource>(
    ICommandContext<TSource> context,
    ISuggestionSink builder)
    where TSource : ICommandSource;

/// <summary>The sink a <see cref="ArgumentSuggestionProvider{TSource}"/> writes candidates into. Candidates replace the current partial token; the dispatcher owns range computation.</summary>
public interface ISuggestionSink
{
    /// <summary>The already-typed remaining text for the token being completed.</summary>
    string Remaining { get; }

    /// <summary>Adds a completion candidate.</summary>
    void Suggest(string text);

    /// <summary>Adds a completion candidate with a tooltip.</summary>
    void Suggest(string text, string tooltip);
}
