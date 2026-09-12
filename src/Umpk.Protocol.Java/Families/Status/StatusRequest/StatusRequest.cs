namespace Umpk.Protocol.Java.Packets;

public static partial class StatusPackets
{
    public static partial class Serverbound
    {
        /// <summary>The status request (<c>minecraft:status_request</c>).</summary>
        public static readonly PacketType<ServerboundStatusRequestPacket> StatusRequest =
            new(ProtocolPhase.Status, PacketFlow.Serverbound, Identifier.Minecraft("status_request"));
    }
}

/// <summary>The empty status request.</summary>
public sealed record ServerboundStatusRequestPacket : IPacket
{
    /// <inheritdoc />
    public PacketType Type => StatusPackets.Serverbound.StatusRequest;
}
