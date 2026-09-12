namespace Umpk.Nbt;

/// <summary>Common base for the six numeric scalar tags. Each tag can be read as any numeric CLR type through a widening or truncating conversion.</summary>
public abstract class NbtNumeric : NbtTag
{
    private protected NbtNumeric()
    {
    }

    /// <summary>The value narrowed or widened to <see cref="long"/> (bit-truncating for float/double).</summary>
    public abstract long AsLong { get; }

    /// <summary>The value as <see cref="int"/>.</summary>
    public abstract int AsInt { get; }

    /// <summary>The value as <see cref="short"/>.</summary>
    public abstract short AsShort { get; }

    /// <summary>The value as <see cref="sbyte"/>.</summary>
    public abstract sbyte AsSByte { get; }

    /// <summary>The value as <see cref="double"/>.</summary>
    public abstract double AsDouble { get; }

    /// <summary>The value as <see cref="float"/>.</summary>
    public abstract float AsFloat { get; }
}
