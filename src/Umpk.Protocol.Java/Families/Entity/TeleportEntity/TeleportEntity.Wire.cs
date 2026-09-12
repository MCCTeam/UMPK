using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityMoveCodecs
{
    /// <summary>1.8 entity teleport: fixed-point position, byte rotations, and on-ground flag.</summary>
    public static readonly PacketCodec<ClientboundTeleportEntityPacket> TeleportEntityV1_8 =
        PacketCodec<ClientboundTeleportEntityPacket>.Of(
            static (ref PacketWriter w, ClientboundTeleportEntityPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteInt(PackLegacyPos(p.X));
                w.WriteInt(PackLegacyPos(p.Y));
                w.WriteInt(PackLegacyPos(p.Z));
                w.WriteAngle(p.Yaw);
                w.WriteAngle(p.Pitch);
                w.WriteBool(p.OnGround);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                double x = UnpackLegacyPos(r.ReadInt()), y = UnpackLegacyPos(r.ReadInt()), z = UnpackLegacyPos(r.ReadInt());
                float yaw = r.ReadAngle(), pitch = r.ReadAngle();
                bool onGround = r.ReadBool();
                return new ClientboundTeleportEntityPacket(id, x, y, z, yaw, pitch, onGround, null, 0);
            });

    /// <summary>Modern entity teleport (1.21.2+): id, PositionMoveRotation, relative bitset, on-ground.</summary>
    public static readonly PacketCodec<ClientboundTeleportEntityPacket> TeleportEntityV1_21_2 =
        PacketCodec<ClientboundTeleportEntityPacket>.Of(
            static (ref PacketWriter w, ClientboundTeleportEntityPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                WritePositionMoveRotation(ref w, p.ModernValues!);
                w.WriteInt(p.Relatives);
                w.WriteBool(p.OnGround);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                PositionMoveRotation values = ReadPositionMoveRotation(ref r);
                int relatives = r.ReadInt();
                bool onGround = r.ReadBool();
                return new ClientboundTeleportEntityPacket(id, values.Position.X, values.Position.Y, values.Position.Z, values.YRot, values.XRot, onGround, values, relatives);
            });

    /// <summary>1.9-1.21.1 entity teleport: VarInt id, double x/y/z, angle-byte yRot/xRot, bool onGround. Routed for 764/765/766/767 (the modern PositionMoveRotation form arrives at proto 768).</summary>
    public static readonly PacketCodec<ClientboundTeleportEntityPacket> TeleportEntityV1_9 =
        PacketCodec<ClientboundTeleportEntityPacket>.Of(
            static (ref PacketWriter w, ClientboundTeleportEntityPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteDouble(p.X);
                w.WriteDouble(p.Y);
                w.WriteDouble(p.Z);
                w.WriteAngle(p.Yaw);
                w.WriteAngle(p.Pitch);
                w.WriteBool(p.OnGround);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                double x = r.ReadDouble(), y = r.ReadDouble(), z = r.ReadDouble();
                float yaw = r.ReadAngle(), pitch = r.ReadAngle();
                bool onGround = r.ReadBool();
                return new ClientboundTeleportEntityPacket(id, x, y, z, yaw, pitch, onGround, null, 0);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareTeleportEntity(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Clientbound.TeleportEntity)
            .From(JavaProtocols.V1_8, EntityMoveCodecs.TeleportEntityV1_8)
            .From(JavaProtocols.V1_9, EntityMoveCodecs.TeleportEntityV1_9)
            .From(JavaProtocols.V1_21_2, EntityMoveCodecs.TeleportEntityV1_21_2)
            .AliasedAs(Identifier.Minecraft("entity_teleport"));
    }
}
