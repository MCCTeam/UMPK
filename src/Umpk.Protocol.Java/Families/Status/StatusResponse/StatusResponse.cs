namespace Umpk.Protocol.Java.Packets;

public static partial class StatusPackets
{
    public static partial class Clientbound
    {
        /// <summary>The status response JSON (<c>minecraft:status_response</c>).</summary>
        public static readonly PacketType<ClientboundStatusResponsePacket> StatusResponse =
            new(ProtocolPhase.Status, PacketFlow.Clientbound, Identifier.Minecraft("status_response"));
    }
}

/// <summary>The server's status response: the MOTD/version/players JSON blob.</summary>
public sealed record ClientboundStatusResponsePacket(string Json) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => StatusPackets.Clientbound.StatusResponse;
}
