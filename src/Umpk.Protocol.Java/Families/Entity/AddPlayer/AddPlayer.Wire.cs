using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntitySpawnCodecs
{
    /// <summary>1.8 player spawn: entity id, UUID, fixed-point position, byte rotations, held item, and metadata.</summary>
    public static readonly PacketCodec<ClientboundAddPlayerPacket> AddPlayerV1_8 =
        PacketCodec<ClientboundAddPlayerPacket>.Of(
            static (ref PacketWriter w, ClientboundAddPlayerPacket p, PacketCodecContext context) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteUuid(p.Uuid);
                w.WriteInt(PackLegacyPos(p.X));
                w.WriteInt(PackLegacyPos(p.Y));
                w.WriteInt(PackLegacyPos(p.Z));
                w.WriteAngle(p.Yaw);
                w.WriteAngle(p.Pitch);
                w.WriteShort(p.CurrentItem);
                EntityMetadataCodec.WriteLegacy(ref w, p.Metadata, context);
            },
            static (ref PacketReader r, PacketCodecContext context) =>
            {
                int id = r.ReadVarInt();
                Guid uuid = r.ReadUuid();
                double x = UnpackLegacyPos(r.ReadInt()), y = UnpackLegacyPos(r.ReadInt()), z = UnpackLegacyPos(r.ReadInt());
                float yaw = r.ReadAngle(), pitch = r.ReadAngle();
                short item = r.ReadShort();
                var meta = EntityMetadataCodec.ReadLegacy(ref r, context);
                return new ClientboundAddPlayerPacket(id, uuid, x, y, z, yaw, pitch, item, meta);
            });

    /// <summary>1.9-1.11.2 spawn player (V1_9 metadata table).</summary>
    public static readonly PacketCodec<ClientboundAddPlayerPacket> AddPlayerV1_9 = AddPlayerWithMetadata(ModernMetadataTable.V1_9);

    /// <summary>1.12-1.12.2 spawn player (V1_12 metadata table).</summary>
    public static readonly PacketCodec<ClientboundAddPlayerPacket> AddPlayerV1_12 = AddPlayerWithMetadata(ModernMetadataTable.V1_12);

    /// <summary>1.13-1.13.2 spawn player (V1_13 metadata table).</summary>
    public static readonly PacketCodec<ClientboundAddPlayerPacket> AddPlayerV1_13 = AddPlayerWithMetadata(ModernMetadataTable.V1_13);

    /// <summary>1.14-1.14.4 spawn player (protocols 477-498): the same shape as 1.13 but with the V1_14 metadata table and the 1.14 packed block-position layout, matching the sibling <see cref="AddMobV1_14"/>.</summary>
    public static readonly PacketCodec<ClientboundAddPlayerPacket> AddPlayerV1_14 =
        AddPlayerWithMetadata(ModernMetadataTable.V1_14, BlockPosLayout.Packed114);

    /// <summary>1.15-1.20.1 spawn player (protocols 573-763): VarInt id, uuid, double x/y/z, angle-byte yRot/xRot, and NO trailing metadata (the metadata block was removed from the spawn packets at 1.15; it now arrives via a separate set_entity_data). The packet leaves the protocol entirely at 1.20.2 (764), where players spawn through add_entity instead.</summary>
    public static readonly PacketCodec<ClientboundAddPlayerPacket> AddPlayerV1_15 =
        PacketCodec<ClientboundAddPlayerPacket>.Of(
            static (ref PacketWriter w, ClientboundAddPlayerPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteUuid(p.Uuid);
                w.WriteDouble(p.X);
                w.WriteDouble(p.Y);
                w.WriteDouble(p.Z);
                w.WriteAngle(p.Yaw);
                w.WriteAngle(p.Pitch);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                Guid uuid = r.ReadUuid();
                double x = r.ReadDouble(), y = r.ReadDouble(), z = r.ReadDouble();
                float yaw = r.ReadAngle(), pitch = r.ReadAngle();
                return new ClientboundAddPlayerPacket(id, uuid, x, y, z, yaw, pitch, 0, new EntityMetadataList([], []));
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareAddPlayer(PacketBindings bindings)
    {
        // The metadata block leaves add_player at 1.15; the packet itself leaves the protocol at 1.20.2 (764), where players arrive through add_entity with the player entity type.
        bindings.Packet(EntityPackets.Clientbound.AddPlayer)
            .From(JavaProtocols.V1_8, EntitySpawnCodecs.AddPlayerV1_8)
            .From(JavaProtocols.V1_9, EntitySpawnCodecs.AddPlayerV1_9)
            .From(JavaProtocols.V1_12, EntitySpawnCodecs.AddPlayerV1_12)
            .From(JavaProtocols.V1_13, EntitySpawnCodecs.AddPlayerV1_13)
            .From(JavaProtocols.V1_14, EntitySpawnCodecs.AddPlayerV1_14)
            .From(JavaProtocols.V1_15, EntitySpawnCodecs.AddPlayerV1_15);
    }
}
