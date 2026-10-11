namespace Umpk.Protocol.Bedrock;

/// <summary>The immutable profile and routing context one packet codec decodes or encodes under.</summary>
/// <remarks>This context carries stable profile and routing values only. Stateful item, block, biome, and actor registries live in the session codec context and are published at their own barriers before dependent packets decode.</remarks>
public sealed record BedrockCodecContext
{
    /// <summary>Creates a codec context for one packet under the given profile and routing.</summary>
    /// <param name="profile">The protocol profile selecting packet, data, and behavior identities.</param>
    /// <param name="direction">Which endpoint sent the packet; <see cref="BedrockPacketDirection.Unknown"/> is rejected because dispatch needs a definite routing.</param>
    /// <param name="senderSubclient">The subclient endpoint credited with sending the packet.</param>
    /// <param name="targetSubclient">The subclient endpoint the packet is addressed to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="direction"/> is not a routable direction.</exception>
    public BedrockCodecContext(
        BedrockProtocolProfile profile,
        BedrockPacketDirection direction,
        BedrockSubclientId senderSubclient,
        BedrockSubclientId targetSubclient)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (direction is not (BedrockPacketDirection.Clientbound or BedrockPacketDirection.Serverbound))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "The packet direction is not supported.");
        }

        Profile = profile;
        Direction = direction;
        SenderSubclient = senderSubclient;
        TargetSubclient = targetSubclient;
    }

    /// <summary>Gets the protocol profile selecting packet, data, and behavior identities.</summary>
    public BedrockProtocolProfile Profile { get; }

    /// <summary>Gets which endpoint sent the packet.</summary>
    public BedrockPacketDirection Direction { get; }

    /// <summary>Gets the subclient endpoint credited with sending the packet.</summary>
    public BedrockSubclientId SenderSubclient { get; }

    /// <summary>Gets the subclient endpoint the packet is addressed to.</summary>
    public BedrockSubclientId TargetSubclient { get; }
}
