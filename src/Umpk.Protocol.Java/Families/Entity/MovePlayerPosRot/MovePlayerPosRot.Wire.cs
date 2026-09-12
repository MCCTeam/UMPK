using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityServerboundCodecs
{
    /// <summary>1.8 position + rotation move (C06): x/y/z double, yaw/pitch float, on-ground byte.</summary>
    public static readonly PacketCodec<ServerboundMovePlayerPosRotPacket> MovePlayerPosRotV1_8 =
        PacketCodec<ServerboundMovePlayerPosRotPacket>.Of(
            static (ref PacketWriter w, ServerboundMovePlayerPosRotPacket p, PacketCodecContext _) =>
            {
                w.WriteDouble(p.X); w.WriteDouble(p.Y); w.WriteDouble(p.Z);
                w.WriteFloat(p.Yaw); w.WriteFloat(p.Pitch);
                w.WriteBool(p.OnGround);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundMovePlayerPosRotPacket(r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadFloat(), r.ReadFloat(), r.ReadBool(), false));

    /// <summary>Modern position + rotation move: x/y/z double, yaw/pitch float, flags byte.</summary>
    public static readonly PacketCodec<ServerboundMovePlayerPosRotPacket> MovePlayerPosRotV1_14 =
        PacketCodec<ServerboundMovePlayerPosRotPacket>.Of(
            static (ref PacketWriter w, ServerboundMovePlayerPosRotPacket p, PacketCodecContext _) =>
            {
                w.WriteDouble(p.X); w.WriteDouble(p.Y); w.WriteDouble(p.Z);
                w.WriteFloat(p.Yaw); w.WriteFloat(p.Pitch);
                w.WriteByte(PackMoveFlags(p.OnGround, p.HorizontalCollision));
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                double x = r.ReadDouble(), y = r.ReadDouble(), z = r.ReadDouble();
                float yaw = r.ReadFloat(), pitch = r.ReadFloat();
                byte flags = r.ReadByte();
                return new ServerboundMovePlayerPosRotPacket(x, y, z, yaw, pitch, (flags & 0x01) != 0, (flags & 0x02) != 0);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareMovePlayerPosRot(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Serverbound.MovePlayerPosRot)
            .From(JavaProtocols.V1_8, EntityServerboundCodecs.MovePlayerPosRotV1_8)
            .From(JavaProtocols.V1_14, EntityServerboundCodecs.MovePlayerPosRotV1_14);
    }
}
