namespace Umpk.Protocol.Bedrock;

/// <summary>The frozen protocol-data and registry identity a protocol profile decodes against.</summary>
/// <remarks>The value is an opaque dataset key such as <c>bedrock-1.26.45-2169</c>, not a protocol number; negotiated runtime registries and translator fallbacks never substitute for this pinned identity, and the default value is invalid by construction.</remarks>
public readonly record struct BedrockDataProfileId
{
    /// <summary>Creates a data profile identifier for the given dataset key.</summary>
    /// <param name="value">The opaque dataset key, which must be non-empty because the default identifier is rejected wherever a profile is built.</param>
    /// <exception cref="ArgumentException"><paramref name="value"/> is <see langword="null"/>, empty, or whitespace.</exception>
    public BedrockDataProfileId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>Gets the opaque dataset key.</summary>
    public string Value { get; }

    internal bool IsValid => !string.IsNullOrWhiteSpace(Value);

    /// <summary>Returns the dataset key unchanged for diagnostics and logging.</summary>
    /// <returns>The dataset key, or an empty string for the default identifier.</returns>
    public override string ToString() => Value ?? string.Empty;
}
