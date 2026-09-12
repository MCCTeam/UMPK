using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityServerboundCodecs
{
    /// <summary>1.8 rotation move (C05): yaw/pitch float, on-ground byte.</summary>
    public static readonly PacketCodec<ServerboundMovePlayerRotPacket> MovePlayerRotV1_8 =
        PacketCodec<ServerboundMovePlayerRotPacket>.Of(
            static (ref PacketWriter w, ServerboundMovePlayerRotPacket p, PacketCodecContext _) =>
            {
                w.WriteFloat(p.Yaw); w.WriteFloat(p.Pitch);
                w.WriteBool(p.OnGround);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundMovePlayerRotPacket(r.ReadFloat(), r.ReadFloat(), r.ReadBool(), false));

    /// <summary>Modern rotation move: yaw/pitch float, flags byte.</summary>
    public static readonly PacketCodec<ServerboundMovePlayerRotPacket> MovePlayerRotV1_14 =
        PacketCodec<ServerboundMovePlayerRotPacket>.Of(
            static (ref PacketWriter w, ServerboundMovePlayerRotPacket p, PacketCodecContext _) =>
            {
                w.WriteFloat(p.Yaw); w.WriteFloat(p.Pitch);
                w.WriteByte(PackMoveFlags(p.OnGround, p.HorizontalCollision));
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                float yaw = r.ReadFloat(), pitch = r.ReadFloat();
                byte flags = r.ReadByte();
                return new ServerboundMovePlayerRotPacket(yaw, pitch, (flags & 0x01) != 0, (flags & 0x02) != 0);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareMovePlayerRot(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Serverbound.MovePlayerRot)
            .From(JavaProtocols.V1_8, EntityServerboundCodecs.MovePlayerRotV1_8)
            .From(JavaProtocols.V1_14, EntityServerboundCodecs.MovePlayerRotV1_14);
    }
}
