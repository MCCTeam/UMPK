namespace Umpk.Protocol.Java.Packets;

/// <summary>Handshake-phase packet type declarations (serverbound only; the client dials).</summary>
public static partial class HandshakePackets
{
    /// <summary>Serverbound handshake packets.</summary>
    public static partial class Serverbound
    {
        /// <summary>The intention/handshake packet (<c>minecraft:intention</c>).</summary>
        public static readonly PacketType<ServerboundHandshakePacket> Intention =
            new(ProtocolPhase.Handshake, PacketFlow.Serverbound, Identifier.Minecraft("intention"));
    }
}

/// <summary>The handshake intention packet: protocol version, server address and port the client dialed, and the next phase (1 = status, 2 = login, 3 = transfer). Frozen wire format across all versions.</summary>
public sealed record ServerboundHandshakePacket(
    int ProtocolVersion,
    string ServerAddress,
    ushort ServerPort,
    int NextState) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => HandshakePackets.Serverbound.Intention;
}
