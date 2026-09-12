namespace Umpk.Nbt;

/// <summary>The terminator tag (id 0). Also the null-NBT marker on the network wire.</summary>
public sealed class NbtEnd : NbtTag
{
    /// <summary>The single shared instance; the End tag carries no data.</summary>
    public static readonly NbtEnd Instance = new();

    private NbtEnd()
    {
    }

    /// <inheritdoc/>
    public override NbtTagType Type => NbtTagType.End;

    /// <inheritdoc/>
    public override NbtTag Copy() => Instance;
}

/// <summary>A signed 8-bit integer (id 1). Booleans are stored as this tag (0 or 1).</summary>
public sealed class NbtByte : NbtNumeric
{
    /// <summary>Creates a byte tag from a signed byte.</summary>
    public NbtByte(sbyte value) => Value = value;

    /// <summary>Creates a byte tag from a boolean (true = 1, false = 0).</summary>
    public NbtByte(bool value) => Value = value ? (sbyte)1 : (sbyte)0;

    /// <summary>The stored value.</summary>
    public sbyte Value { get; }

    /// <summary>The value interpreted as a boolean (non-zero is true).</summary>
    public bool AsBool => Value != 0;

    /// <inheritdoc/>
    public override NbtTagType Type => NbtTagType.Byte;

    /// <inheritdoc/>
    public override long AsLong => Value;

    /// <inheritdoc/>
    public override int AsInt => Value;

    /// <inheritdoc/>
    public override short AsShort => Value;

    /// <inheritdoc/>
    public override sbyte AsSByte => Value;

    /// <inheritdoc/>
    public override double AsDouble => Value;

    /// <inheritdoc/>
    public override float AsFloat => Value;

    /// <inheritdoc/>
    public override NbtTag Copy() => this;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is NbtByte other && other.Value == Value;

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();
}

/// <summary>A signed big-endian 16-bit integer (id 2).</summary>
public sealed class NbtShort : NbtNumeric
{
    /// <summary>Creates a short tag.</summary>
    public NbtShort(short value) => Value = value;

    /// <summary>The stored value.</summary>
    public short Value { get; }

    /// <inheritdoc/>
    public override NbtTagType Type => NbtTagType.Short;

    /// <inheritdoc/>
    public override long AsLong => Value;

    /// <inheritdoc/>
    public override int AsInt => Value;

    /// <inheritdoc/>
    public override short AsShort => Value;

    /// <inheritdoc/>
    public override sbyte AsSByte => (sbyte)(Value & 0xFF);

    /// <inheritdoc/>
    public override double AsDouble => Value;

    /// <inheritdoc/>
    public override float AsFloat => Value;

    /// <inheritdoc/>
    public override NbtTag Copy() => this;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is NbtShort other && other.Value == Value;

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();
}

/// <summary>A signed big-endian 32-bit integer (id 3).</summary>
public sealed class NbtInt : NbtNumeric
{
    /// <summary>Creates an int tag.</summary>
    public NbtInt(int value) => Value = value;

    /// <summary>The stored value.</summary>
    public int Value { get; }

    /// <inheritdoc/>
    public override NbtTagType Type => NbtTagType.Int;

    /// <inheritdoc/>
    public override long AsLong => Value;

    /// <inheritdoc/>
    public override int AsInt => Value;

    /// <inheritdoc/>
    public override short AsShort => (short)(Value & 0xFFFF);

    /// <inheritdoc/>
    public override sbyte AsSByte => (sbyte)(Value & 0xFF);

    /// <inheritdoc/>
    public override double AsDouble => Value;

    /// <inheritdoc/>
    public override float AsFloat => Value;

    /// <inheritdoc/>
    public override NbtTag Copy() => this;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is NbtInt other && other.Value == Value;

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();
}

/// <summary>A signed big-endian 64-bit integer (id 4).</summary>
public sealed class NbtLong : NbtNumeric
{
    /// <summary>Creates a long tag.</summary>
    public NbtLong(long value) => Value = value;

    /// <summary>The stored value.</summary>
    public long Value { get; }

    /// <inheritdoc/>
    public override NbtTagType Type => NbtTagType.Long;

    /// <inheritdoc/>
    public override long AsLong => Value;

    /// <inheritdoc/>
    public override int AsInt => (int)Value;

    /// <inheritdoc/>
    public override short AsShort => (short)(Value & 0xFFFF);

    /// <inheritdoc/>
    public override sbyte AsSByte => (sbyte)(Value & 0xFF);

    /// <inheritdoc/>
    public override double AsDouble => Value;

    /// <inheritdoc/>
    public override float AsFloat => Value;

    /// <inheritdoc/>
    public override NbtTag Copy() => this;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is NbtLong other && other.Value == Value;

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();
}

/// <summary>A big-endian IEEE-754 single (id 5).</summary>
public sealed class NbtFloat : NbtNumeric
{
    /// <summary>Creates a float tag.</summary>
    public NbtFloat(float value) => Value = value;

    /// <summary>The stored value.</summary>
    public float Value { get; }

    /// <inheritdoc/>
    public override NbtTagType Type => NbtTagType.Float;

    /// <inheritdoc/>
    public override long AsLong => (long)Value;

    /// <inheritdoc/>
    public override int AsInt => FloorToInt(Value);

    /// <inheritdoc/>
    public override short AsShort => (short)(FloorToInt(Value) & 0xFFFF);

    /// <inheritdoc/>
    public override sbyte AsSByte => (sbyte)(FloorToInt(Value) & 0xFF);

    /// <inheritdoc/>
    public override double AsDouble => Value;

    /// <inheritdoc/>
    public override float AsFloat => Value;

    /// <inheritdoc/>
    public override NbtTag Copy() => this;

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is NbtFloat other && BitConverter.SingleToInt32Bits(other.Value) == BitConverter.SingleToInt32Bits(Value);

    /// <inheritdoc/>
    public override int GetHashCode() => BitConverter.SingleToInt32Bits(Value);

    // Floor before narrowing to int.
    private static int FloorToInt(float value)
    {
        int i = (int)value;
        return value < i ? i - 1 : i;
    }
}

/// <summary>A big-endian IEEE-754 double (id 6).</summary>
public sealed class NbtDouble : NbtNumeric
{
    /// <summary>Creates a double tag.</summary>
    public NbtDouble(double value) => Value = value;

    /// <summary>The stored value.</summary>
    public double Value { get; }

    /// <inheritdoc/>
    public override NbtTagType Type => NbtTagType.Double;

    /// <inheritdoc/>
    public override long AsLong => (long)Math.Floor(Value);

    /// <inheritdoc/>
    public override int AsInt => FloorToInt(Value);

    /// <inheritdoc/>
    public override short AsShort => (short)(FloorToInt(Value) & 0xFFFF);

    /// <inheritdoc/>
    public override sbyte AsSByte => (sbyte)(FloorToInt(Value) & 0xFF);

    /// <inheritdoc/>
    public override double AsDouble => Value;

    /// <inheritdoc/>
    public override float AsFloat => (float)Value;

    /// <inheritdoc/>
    public override NbtTag Copy() => this;

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is NbtDouble other && BitConverter.DoubleToInt64Bits(other.Value) == BitConverter.DoubleToInt64Bits(Value);

    /// <inheritdoc/>
    public override int GetHashCode() => BitConverter.DoubleToInt64Bits(Value).GetHashCode();

    // Floor before narrowing to int.
    private static int FloorToInt(double value)
    {
        int i = (int)value;
        return value < i ? i - 1 : i;
    }
}

/// <summary>A modified-UTF-8 string (id 8).</summary>
public sealed class NbtString : NbtTag
{
    /// <summary>The shared empty-string instance.</summary>
    public static readonly NbtString Empty = new(string.Empty);

    /// <summary>Creates a string tag.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public NbtString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    /// <summary>The stored string.</summary>
    public string Value { get; }

    /// <inheritdoc/>
    public override NbtTagType Type => NbtTagType.String;

    /// <inheritdoc/>
    public override NbtTag Copy() => this;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is NbtString other && string.Equals(other.Value, Value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
}
