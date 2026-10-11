namespace Umpk.Protocol.Bedrock;

/// <summary>The immutable identity set binding one Bedrock release to the packet layout, dataset, and behavior rules its codecs use.</summary>
/// <remarks>A profile keeps four axes apart because each can change on its own: the edition-qualified release, the packet dialect, the frozen dataset, and the measured behavior rules. Construction rejects invalid identities up front so codecs never meet an <see cref="BedrockPacketDialect.Unknown"/> dialect or a default profile key at decode time; generated registration decides which valid combinations UMPK actually supports.</remarks>
public sealed record BedrockProtocolProfile
{
    /// <summary>Creates a profile binding a release to its packet, data, and behavior identities.</summary>
    /// <param name="version">The edition-qualified release, which must belong to <see cref="GameEdition.Bedrock"/> with a non-empty name and a positive protocol number.</param>
    /// <param name="packetDialect">The packet layout family, of which only <see cref="BedrockPacketDialect.Base2168"/> is supported.</param>
    /// <param name="dataProfile">The frozen dataset identity; the default identifier is rejected.</param>
    /// <param name="behaviorProfile">The behavior-rules identity; the default identifier is rejected.</param>
    /// <exception cref="ArgumentNullException"><paramref name="version"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="version"/> is not a Bedrock release, has an empty name, or either profile identifier is the invalid default.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> carries a non-positive protocol number, or <paramref name="packetDialect"/> is not a supported layout family.</exception>
    public BedrockProtocolProfile(
        GameVersion version,
        BedrockPacketDialect packetDialect,
        BedrockDataProfileId dataProfile,
        BedrockBehaviorProfileId behaviorProfile)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (version.Edition is not GameEdition.Bedrock)
        {
            throw new ArgumentException("The version must identify the Bedrock edition.", nameof(version));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(version.Name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version.Protocol);
        if (packetDialect is not BedrockPacketDialect.Base2168)
        {
            throw new ArgumentOutOfRangeException(nameof(packetDialect), packetDialect, "The packet dialect is not supported.");
        }

        if (!dataProfile.IsValid)
        {
            throw new ArgumentException("The data profile identifier is required.", nameof(dataProfile));
        }

        if (!behaviorProfile.IsValid)
        {
            throw new ArgumentException("The behavior profile identifier is required.", nameof(behaviorProfile));
        }

        Version = version;
        PacketDialect = packetDialect;
        DataProfile = dataProfile;
        BehaviorProfile = behaviorProfile;
    }

    /// <summary>Gets the edition-qualified release this profile describes.</summary>
    public GameVersion Version { get; }

    /// <summary>Gets the packet layout family codecs bind to instead of the numeric protocol.</summary>
    public BedrockPacketDialect PacketDialect { get; }

    /// <summary>Gets the frozen dataset identity used for registry and content decoding.</summary>
    public BedrockDataProfileId DataProfile { get; }

    /// <summary>Gets the behavior-rules identity used for simulation and interaction semantics.</summary>
    public BedrockBehaviorProfileId BehaviorProfile { get; }
}
