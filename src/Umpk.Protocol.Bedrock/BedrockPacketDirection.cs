namespace Umpk.Protocol.Bedrock;

/// <summary>Which endpoint sent a Bedrock packet, as observed at the codec boundary.</summary>
public enum BedrockPacketDirection
{
    /// <summary>The direction is not known; codecs reject this value wherever a routing decision is required.</summary>
    Unknown = 0,
    /// <summary>A packet sent by the server toward the client.</summary>
    Clientbound = 1,
    /// <summary>A packet sent by the client toward the server.</summary>
    Serverbound = 2,
}
