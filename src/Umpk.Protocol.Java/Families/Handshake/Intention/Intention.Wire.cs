using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>Codecs for handshake packets. The intention wire format is frozen across all versions, so the family exposes a single era member reused by every descriptor.</summary>
public static partial class HandshakeCodecs
{
    /// <summary>The handshake intention codec (frozen across versions).</summary>
    public static readonly PacketCodec<ServerboundHandshakePacket> Intention =
        PacketCodec<ServerboundHandshakePacket>.Of(
            static (ref PacketWriter w, ServerboundHandshakePacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.ProtocolVersion);
                w.WriteString(p.ServerAddress, 255);
                w.WriteUShort(p.ServerPort);
                w.WriteVarInt(p.NextState);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundHandshakePacket(r.ReadVarInt(), r.ReadString(255), r.ReadUShort(), r.ReadVarInt()),
            WireShape.Of("varint,string,ushort,varint"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareIntention(PacketBindings bindings)
    {
        bindings.Packet(HandshakePackets.Serverbound.Intention)
            .From(JavaProtocols.V1_8, HandshakeCodecs.Intention);
    }
}
