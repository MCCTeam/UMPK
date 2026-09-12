namespace Umpk.Commands;

/// <summary>The parsed context handed to a command body and to suggestion providers. It exposes the issuing <see cref="Source"/>, the raw input, and typed access to parsed arguments, without leaking the underlying dispatcher types.</summary>
/// <typeparam name="TSource">The command source type carrying the session/host context.</typeparam>
public interface ICommandContext<out TSource>
    where TSource : ICommandSource
{
    /// <summary>The command source that issued this invocation.</summary>
    TSource Source { get; }

    /// <summary>The full input string being parsed or executed.</summary>
    string Input { get; }

    /// <summary>Reads a parsed argument by name, cast to <typeparamref name="T"/>.</summary>
    /// <exception cref="InvalidOperationException">No such argument, or a type mismatch.</exception>
    T GetArgument<T>(string name);
}
