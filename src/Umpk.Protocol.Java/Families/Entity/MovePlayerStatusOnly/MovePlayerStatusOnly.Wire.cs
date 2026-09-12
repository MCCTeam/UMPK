using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityServerboundCodecs
{
    /// <summary>Modern status-only move: a flags byte (on-ground + horizontal-collision).</summary>
    public static readonly PacketCodec<ServerboundMovePlayerStatusOnlyPacket> MovePlayerStatusOnly =
        PacketCodec<ServerboundMovePlayerStatusOnlyPacket>.Of(
            static (ref PacketWriter w, ServerboundMovePlayerStatusOnlyPacket p, PacketCodecContext _) =>
                w.WriteByte(PackMoveFlags(p.OnGround, p.HorizontalCollision)),
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                byte flags = r.ReadByte();
                return new ServerboundMovePlayerStatusOnlyPacket((flags & 0x01) != 0, (flags & 0x02) != 0);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareMovePlayerStatusOnly(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Serverbound.MovePlayerStatusOnly)
            .From(JavaEras.Caves, EntityServerboundCodecs.MovePlayerStatusOnly);
    }
}
