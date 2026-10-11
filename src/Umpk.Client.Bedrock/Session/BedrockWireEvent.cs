namespace Umpk.Client.Bedrock.Session;

/// <summary>Names which side of the client connection a recorded packet crossed.</summary>
internal enum BedrockWireDirection
{
    /// <summary>Marks a packet the client received from the peer.</summary>
    Inbound,
    /// <summary>Marks a packet the client handed to the transport.</summary>
    Outbound,
}

/// <summary>Records one packet id observed by the session with its direction and the client clock reading taken when it was logged.</summary>
/// <remarks>The timestamp comes from the session clock at logging time and never carries the packet tick or server time.</remarks>
internal readonly record struct BedrockWireEvent(BedrockWireDirection Direction, int PacketId, DateTimeOffset Timestamp);
