namespace Umpk.Protocol.Java.Packets;

public static partial class StatusPackets
{
    public static partial class Clientbound
    {
        /// <summary>The pong reply echoing the ping payload (<c>minecraft:pong_response</c>).</summary>
        public static readonly PacketType<ClientboundPongResponsePacket> PongResponse =
            new(ProtocolPhase.Status, PacketFlow.Clientbound, Identifier.Minecraft("pong_response"));
    }
}

/// <summary>The pong reply echoing the client's ping nonce.</summary>
public sealed record ClientboundPongResponsePacket(long Payload) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => StatusPackets.Clientbound.PongResponse;
}
