namespace Umpk.Protocol.Bedrock;

/// <summary>The identity of the measured behavior rules associated with a protocol profile, kept separate from the dataset identity.</summary>
/// <remarks>Movement, collision, and interaction rules evolve independently of wire layouts and registry contents, so this key names the measured behavior set (for example <c>bedrock-1.26.45-2169</c>) rather than restating the protocol number; the default value is invalid by construction.</remarks>
public readonly record struct BedrockBehaviorProfileId
{
    /// <summary>Creates a behavior profile identifier for the given rules key.</summary>
    /// <param name="value">The opaque behavior-rules key, which must be non-empty because the default identifier is rejected wherever a profile is built.</param>
    /// <exception cref="ArgumentException"><paramref name="value"/> is <see langword="null"/>, empty, or whitespace.</exception>
    public BedrockBehaviorProfileId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>Gets the opaque behavior-rules key.</summary>
    public string Value { get; }

    internal bool IsValid => !string.IsNullOrWhiteSpace(Value);

    /// <summary>Returns the rules key unchanged for diagnostics and logging.</summary>
    /// <returns>The rules key, or an empty string for the default identifier.</returns>
    public override string ToString() => Value ?? string.Empty;
}
