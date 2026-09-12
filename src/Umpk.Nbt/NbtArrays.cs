namespace Umpk.Nbt;

/// <summary>A length-prefixed array of signed bytes (id 7).</summary>
public sealed class NbtByteArray : NbtTag
{
    /// <summary>Wraps <paramref name="value"/> without copying. The tag owns the array afterwards.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public NbtByteArray(sbyte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    /// <summary>The backing array. Mutable in place; treated as owned by this tag.</summary>
    public sbyte[] Value { get; }

    /// <inheritdoc/>
    public override NbtTagType Type => NbtTagType.ByteArray;

    /// <inheritdoc/>
    public override NbtTag Copy() => new NbtByteArray((sbyte[])Value.Clone());

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is NbtByteArray other && Value.AsSpan().SequenceEqual(other.Value);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.AddBytes(System.Runtime.InteropServices.MemoryMarshal.AsBytes(Value.AsSpan()));
        return hash.ToHashCode();
    }
}

/// <summary>A length-prefixed array of big-endian 32-bit integers (id 11).</summary>
public sealed class NbtIntArray : NbtTag
{
    /// <summary>Wraps <paramref name="value"/> without copying. The tag owns the array afterwards.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public NbtIntArray(int[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    /// <summary>The backing array. Mutable in place; treated as owned by this tag.</summary>
    public int[] Value { get; }

    /// <inheritdoc/>
    public override NbtTagType Type => NbtTagType.IntArray;

    /// <inheritdoc/>
    public override NbtTag Copy() => new NbtIntArray((int[])Value.Clone());

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is NbtIntArray other && Value.AsSpan().SequenceEqual(other.Value);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.AddBytes(System.Runtime.InteropServices.MemoryMarshal.AsBytes(Value.AsSpan()));
        return hash.ToHashCode();
    }
}

/// <summary>A length-prefixed array of big-endian 64-bit integers (id 12).</summary>
public sealed class NbtLongArray : NbtTag
{
    /// <summary>Wraps <paramref name="value"/> without copying. The tag owns the array afterwards.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public NbtLongArray(long[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    /// <summary>The backing array. Mutable in place; treated as owned by this tag.</summary>
    public long[] Value { get; }

    /// <inheritdoc/>
    public override NbtTagType Type => NbtTagType.LongArray;

    /// <inheritdoc/>
    public override NbtTag Copy() => new NbtLongArray((long[])Value.Clone());

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is NbtLongArray other && Value.AsSpan().SequenceEqual(other.Value);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.AddBytes(System.Runtime.InteropServices.MemoryMarshal.AsBytes(Value.AsSpan()));
        return hash.ToHashCode();
    }
}
