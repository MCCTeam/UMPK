using Brig = Brigadier.NET.ArgumentTypes;

namespace Umpk.Commands.Internal;

/// <summary>Internal bridge: wraps a Brigadier argument type behind UMPK's opaque <see cref="IArgumentType{T}"/>. The wrapped instance never escapes to the public surface.</summary>
internal sealed class BrigadierArgumentType<T> : IArgumentType<T>
    where T : notnull
{
    internal BrigadierArgumentType(Brig.IArgumentType<T> inner)
    {
        Inner = inner;
    }

    internal Brig.IArgumentType<T> Inner { get; }
}
