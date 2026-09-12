namespace Umpk.Protocol.Java.Packets;

public static partial class StatusPackets
{
    public static partial class Serverbound
    {
        /// <summary>The ping request carrying a nonce (<c>minecraft:ping_request</c>).</summary>
        public static readonly PacketType<ServerboundPingRequestPacket> PingRequest =
            new(ProtocolPhase.Status, PacketFlow.Serverbound, Identifier.Minecraft("ping_request"));
    }
}

/// <summary>The ping request carrying a nonce the server echoes back.</summary>
public sealed record ServerboundPingRequestPacket(long Payload) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => StatusPackets.Serverbound.PingRequest;
}
