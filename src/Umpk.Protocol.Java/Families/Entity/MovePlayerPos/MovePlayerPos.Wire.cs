using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityServerboundCodecs
{
    /// <summary>1.8 position move (C04): x/y/z double, on-ground byte.</summary>
    public static readonly PacketCodec<ServerboundMovePlayerPosPacket> MovePlayerPosV1_8 =
        PacketCodec<ServerboundMovePlayerPosPacket>.Of(
            static (ref PacketWriter w, ServerboundMovePlayerPosPacket p, PacketCodecContext _) =>
            {
                w.WriteDouble(p.X); w.WriteDouble(p.Y); w.WriteDouble(p.Z);
                w.WriteBool(p.OnGround);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundMovePlayerPosPacket(r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadBool(), false));

    /// <summary>Modern position move: x/y/z double, flags byte (on-ground + horizontal-collision).</summary>
    public static readonly PacketCodec<ServerboundMovePlayerPosPacket> MovePlayerPosV1_14 =
        PacketCodec<ServerboundMovePlayerPosPacket>.Of(
            static (ref PacketWriter w, ServerboundMovePlayerPosPacket p, PacketCodecContext _) =>
            {
                w.WriteDouble(p.X); w.WriteDouble(p.Y); w.WriteDouble(p.Z);
                w.WriteByte(PackMoveFlags(p.OnGround, p.HorizontalCollision));
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                double x = r.ReadDouble(), y = r.ReadDouble(), z = r.ReadDouble();
                byte flags = r.ReadByte();
                return new ServerboundMovePlayerPosPacket(x, y, z, (flags & 0x01) != 0, (flags & 0x02) != 0);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareMovePlayerPos(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Serverbound.MovePlayerPos)
            .From(JavaProtocols.V1_8, EntityServerboundCodecs.MovePlayerPosV1_8)
            .From(JavaProtocols.V1_14, EntityServerboundCodecs.MovePlayerPosV1_14);
    }
}
