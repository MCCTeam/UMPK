namespace Umpk.Protocol.Bedrock;

/// <summary>A two-bit Bedrock subclient identifier selecting which client endpoint on a connection a packet belongs to.</summary>
/// <remarks>The wire header carries two 2-bit subclient fields, and value <c>0</c> is the ordinary game client. The current batch decoder rejects nonzero routing because this release supports one local player per connection, while the value type preserves the complete field width for a future profile that adds subclients.</remarks>
public readonly record struct BedrockSubclientId
{
    /// <summary>Creates a subclient identifier for the given wire value.</summary>
    /// <param name="value">The subclient value, which must be between 0 and 3 inclusive to fit the two-bit wire field.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative or greater than 3.</exception>
    public BedrockSubclientId(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 3);
        Value = value;
    }

    /// <summary>Gets the wire value between 0 and 3 inclusive.</summary>
    public int Value { get; }

    /// <summary>Returns the wire value in invariant form for diagnostics and logging.</summary>
    /// <returns>The decimal wire value.</returns>
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
