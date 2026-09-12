using Brig = Brigadier.NET.Context;

namespace Umpk.Commands.Internal;

/// <summary>Internal adapter presenting a Brigadier <see cref="Brig.CommandContext{TSource}"/> as UMPK's opaque <see cref="ICommandContext{TSource}"/>.</summary>
internal sealed class BrigadierCommandContext<TSource> : ICommandContext<TSource>
    where TSource : ICommandSource
{
    private readonly Brig.CommandContext<TSource> _inner;

    internal BrigadierCommandContext(Brig.CommandContext<TSource> inner)
    {
        _inner = inner;
    }

    public TSource Source => _inner.Source;

    public string Input => _inner.Input;

    public T GetArgument<T>(string name) => _inner.GetArgument<T>(name);
}
