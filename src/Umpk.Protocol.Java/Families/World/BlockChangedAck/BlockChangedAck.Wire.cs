using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.WorldCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class WorldBlockCodecs
{
    /// <summary>Modern block-changed ack: a single VarInt sequence.</summary>
    public static readonly PacketCodec<ClientboundBlockChangedAckPacket> BlockChangedAckV1_19 =
        PacketCodec<ClientboundBlockChangedAckPacket>.Of(
            static (ref PacketWriter w, ClientboundBlockChangedAckPacket p, PacketCodecContext _) => w.WriteVarInt(p.Sequence),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundBlockChangedAckPacket(r.ReadVarInt()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareBlockChangedAck(PacketBindings bindings)
    {
        bindings.Packet(WorldPackets.Clientbound.BlockChangedAck)
            .From(JavaEras.ChatSigning, WorldBlockCodecs.BlockChangedAckV1_19);
    }
}
