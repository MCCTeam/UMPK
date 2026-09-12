using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityServerboundCodecs
{
    /// <summary>1.8 swing arm: no fields.</summary>
    public static readonly PacketCodec<ServerboundSwingPacket> SwingV1_8 =
        PacketCodec<ServerboundSwingPacket>.Of(
            static (ref PacketWriter _, ServerboundSwingPacket _, PacketCodecContext _) => { },
            static (ref PacketReader _, PacketCodecContext _) => new ServerboundSwingPacket(0, false));

    /// <summary>Modern swing arm: a hand VarInt.</summary>
    public static readonly PacketCodec<ServerboundSwingPacket> SwingV1_9 =
        PacketCodec<ServerboundSwingPacket>.Of(
            static (ref PacketWriter w, ServerboundSwingPacket p, PacketCodecContext _) => w.WriteVarInt(p.Hand),
            static (ref PacketReader r, PacketCodecContext _) => new ServerboundSwingPacket(r.ReadVarInt(), true));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSwing(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Serverbound.Swing)
            .From(JavaProtocols.V1_8, EntityServerboundCodecs.SwingV1_8)
            .From(JavaProtocols.V1_9, EntityServerboundCodecs.SwingV1_9);
    }
}
