using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntitySpawnCodecs
{
    /// <summary>1.8 mob spawn.</summary>
    public static readonly PacketCodec<ClientboundAddMobPacket> AddMobV1_8 =
        PacketCodec<ClientboundAddMobPacket>.Of(
            static (ref PacketWriter w, ClientboundAddMobPacket p, PacketCodecContext context) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteByte((byte)p.TypeId);
                w.WriteInt(PackLegacyPos(p.X));
                w.WriteInt(PackLegacyPos(p.Y));
                w.WriteInt(PackLegacyPos(p.Z));
                w.WriteAngle(p.Yaw);
                w.WriteAngle(p.Pitch);
                w.WriteAngle(p.HeadPitch);
                w.WriteShort(p.VelocityX);
                w.WriteShort(p.VelocityY);
                w.WriteShort(p.VelocityZ);
                EntityMetadataCodec.WriteLegacy(ref w, p.Metadata, context);
            },
            static (ref PacketReader r, PacketCodecContext context) =>
            {
                int id = r.ReadVarInt();
                byte type = r.ReadByte();
                double x = UnpackLegacyPos(r.ReadInt()), y = UnpackLegacyPos(r.ReadInt()), z = UnpackLegacyPos(r.ReadInt());
                float yaw = r.ReadAngle(), pitch = r.ReadAngle(), head = r.ReadAngle();
                short vx = r.ReadShort(), vy = r.ReadShort(), vz = r.ReadShort();
                var meta = EntityMetadataCodec.ReadLegacy(ref r, context);
                return new ClientboundAddMobPacket(id, type, x, y, z, yaw, pitch, head, vx, vy, vz, meta);
            });

    // The pre-flattening split-spawn family: unlike 1.8 these carry a UUID and double positions, and the mob/player metadata is the 1.9 typed format (JSON components, named-root NBT, pre-1.14 block position packing), byte-identical to the era's set_entity_data body. Mob type is a byte on 1.9-1.12 and a VarInt on 1.13+; the metadata serializer table is V1_9 (1.9-1.11), V1_12 (1.12-1.12.2), or V1_13 (1.13-1.13.2).

    /// <summary>1.9-1.11.2 spawn mob (byte type, V1_9 metadata table).</summary>
    public static readonly PacketCodec<ClientboundAddMobPacket> AddMobV1_9 = AddMobPre114(ModernMetadataTable.V1_9, AddMobWire.V1_9);

    /// <summary>1.12-1.12.2 spawn mob (byte type, V1_12 metadata table).</summary>
    public static readonly PacketCodec<ClientboundAddMobPacket> AddMobV1_12 = AddMobPre114(ModernMetadataTable.V1_12, AddMobWire.V1_9);

    /// <summary>1.13-1.13.2 spawn mob (VarInt type, V1_13 metadata table).</summary>
    public static readonly PacketCodec<ClientboundAddMobPacket> AddMobV1_13 = AddMobPre114(ModernMetadataTable.V1_13, AddMobWire.V1_13);

    /// <summary>1.14-1.14.4 spawn mob (protocols 477-498): VarInt type, then the trailing entity metadata in the V1_14 19-serializer table with the 1.14 packed block-position layout. The metadata rides at the end and must be consumed exactly or the following packet desyncs.</summary>
    public static readonly PacketCodec<ClientboundAddMobPacket> AddMobV1_14 =
        AddMobPre114(ModernMetadataTable.V1_14, AddMobWire.V1_14);

    /// <summary>1.15-1.18.2 spawn mob (protocols 573-758): VarInt type, then i16 velocity, and NO trailing metadata (the metadata block was removed from spawn_entity_living at 1.15; it now arrives via a separate set_entity_data). Verified vs minecraft-data 1.15.2-1.18.2 packet_spawn_entity_living.</summary>
    public static readonly PacketCodec<ClientboundAddMobPacket> AddMobV1_15 =
        PacketCodec<ClientboundAddMobPacket>.Of(
            static (ref PacketWriter w, ClientboundAddMobPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteUuid(p.Uuid);
                w.WriteVarInt(p.TypeId);
                w.WriteDouble(p.X);
                w.WriteDouble(p.Y);
                w.WriteDouble(p.Z);
                w.WriteAngle(p.Yaw);
                w.WriteAngle(p.Pitch);
                w.WriteAngle(p.HeadPitch);
                w.WriteShort(p.VelocityX);
                w.WriteShort(p.VelocityY);
                w.WriteShort(p.VelocityZ);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                Guid uuid = r.ReadUuid();
                int type = r.ReadVarInt();
                double x = r.ReadDouble(), y = r.ReadDouble(), z = r.ReadDouble();
                float yaw = r.ReadAngle(), pitch = r.ReadAngle(), head = r.ReadAngle();
                short vx = r.ReadShort(), vy = r.ReadShort(), vz = r.ReadShort();
                return new ClientboundAddMobPacket(id, type, x, y, z, yaw, pitch, head, vx, vy, vz,
                    new EntityMetadataList([], []), uuid);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareAddMob(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Clientbound.AddMob)
            .From(JavaProtocols.V1_8, EntitySpawnCodecs.AddMobV1_8)
            .From(JavaProtocols.V1_9, EntitySpawnCodecs.AddMobV1_9)
            .From(JavaProtocols.V1_12, EntitySpawnCodecs.AddMobV1_12)
            .From(JavaProtocols.V1_13, EntitySpawnCodecs.AddMobV1_13)
            .From(JavaProtocols.V1_14, EntitySpawnCodecs.AddMobV1_14)
            .From(JavaProtocols.V1_15, EntitySpawnCodecs.AddMobV1_15);
    }
}
