using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>The pre-1.15 spawn-mob layout: the entity type id widened from a byte to a VarInt at 1.13, and the BLOCK_POS packing inside the trailing metadata block moved at 1.14. The two boundaries are one release apart, so a codec that takes them as separate defaulted parameters can be given the 1.13 type width with the 1.9 position packing without anything saying so.</summary>
/// <param name="TypeIsVarInt">1.13+: the entity type id is a VarInt rather than a byte.</param>
/// <param name="BlockPos">The BLOCK_POS packing the trailing metadata block uses.</param>
internal readonly record struct AddMobWire(bool TypeIsVarInt, BlockPosLayout BlockPos)
{
    /// <summary>107-316 (1.9-1.11.2) and 335-340 (1.12-1.12.2): byte type, pre-1.14 packing.</summary>
    internal static AddMobWire V1_9 { get; } = new(TypeIsVarInt: false, BlockPosLayout.PrePacked114);

    /// <summary>393-404 (1.13-1.13.2): the type becomes a VarInt.</summary>
    internal static AddMobWire V1_13 { get; } = V1_9 with { TypeIsVarInt = true };

    /// <summary>477-498 (1.14-1.14.4): the 1.14 block-position packing.</summary>
    internal static AddMobWire V1_14 { get; } = V1_13 with { BlockPos = BlockPosLayout.Packed114 };

    /// <inheritdoc />
    public override string ToString() => $"varinttype={(TypeIsVarInt ? 1 : 0)},{BlockPos}";
}

/// <summary>Shared low-level helpers for the entity-family codecs: legacy fixed-point position packing, low-precision vec3, position-move-rotation, the pre-1.14 spawn factories, and the modern / component-json set-entity-data builders.</summary>
internal static class EntityCodecShared
{
    /// <summary>Modern position fixed-point scale is not used; 1.8 positions are coord*32 as ints.</summary>
    internal const double LegacyPosScale = 32.0;

    internal static int PackLegacyPos(double coord) => (int)Math.Round(coord * LegacyPosScale);

    internal static double UnpackLegacyPos(int fixedPoint) => fixedPoint / LegacyPosScale;

    // Reads one low-precision vector without interpreting it: a leading byte; 0 means the zero vector; otherwise a second byte and a 4-byte int (6 bytes total), and a trailing VarInt scale iff the continuation bit (0x04) of the first byte is set. TODO(lpvec3-decode): decode to a Vec3d when a lossy value model is acceptable.
    internal static byte[] ReadLpVec3(ref PacketReader r)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        byte lowest = r.ReadByte();
        w.WriteByte(lowest);
        if (lowest == 0)
            return buffer.WrittenSpan.ToArray();

        w.WriteByte(r.ReadByte());     // middle
        w.WriteBytes(r.ReadBytes(4));  // highest (unsigned int)
        if ((lowest & 0x04) == 0x04)
            w.WriteVarInt(r.ReadVarInt());

        return buffer.WrittenSpan.ToArray();
    }

    internal static PacketCodec<ClientboundAddMobPacket> AddMobPre114(
        ModernMetadataTable table, AddMobWire era) =>
        PacketCodec<ClientboundAddMobPacket>.Of(
            (ref PacketWriter w, ClientboundAddMobPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteUuid(p.Uuid);
                if (era.TypeIsVarInt) w.WriteVarInt(p.TypeId); else w.WriteByte((byte)p.TypeId);
                w.WriteDouble(p.X);
                w.WriteDouble(p.Y);
                w.WriteDouble(p.Z);
                w.WriteAngle(p.Yaw);
                w.WriteAngle(p.Pitch);
                w.WriteAngle(p.HeadPitch);
                w.WriteShort(p.VelocityX);
                w.WriteShort(p.VelocityY);
                w.WriteShort(p.VelocityZ);
                EntityMetadataCodec.WriteModern(ref w, p.Metadata, table, NbtWireFormat.JavaNamedRoot, ComponentWireEra.Legacy, componentJson: true, blockPos: era.BlockPos);
            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                Guid uuid = r.ReadUuid();
                int type = era.TypeIsVarInt ? r.ReadVarInt() : r.ReadByte();
                double x = r.ReadDouble(), y = r.ReadDouble(), z = r.ReadDouble();
                float yaw = r.ReadAngle(), pitch = r.ReadAngle(), head = r.ReadAngle();
                short vx = r.ReadShort(), vy = r.ReadShort(), vz = r.ReadShort();
                var meta = EntityMetadataCodec.ReadModern(ref r, table, NbtWireFormat.JavaNamedRoot, ComponentWireEra.Legacy, componentJson: true, blockPos: era.BlockPos);
                return new ClientboundAddMobPacket(id, type, x, y, z, yaw, pitch, head, vx, vy, vz, meta, uuid);
            },
            WireShape.Of(
                era.TypeIsVarInt
                    ? "varint,uuid,varint,3*double,3*angle,3*short,metadata"
                    : "varint,uuid,byte,3*double,3*angle,3*short,metadata",
                $"{era},{table.ShapeToken}"));

    /// <summary>The 1.9-1.14.4 spawn-player form: VarInt id, uuid, double x/y/z, angle-byte yRot/xRot, then the packed entity-metadata block. The metadata block leaves at 1.15, which is why this stops being the right shape there rather than at 1.14.</summary>
    internal static PacketCodec<ClientboundAddPlayerPacket> AddPlayerWithMetadata(
        ModernMetadataTable table, BlockPosLayout blockPos = BlockPosLayout.PrePacked114) =>
        PacketCodec<ClientboundAddPlayerPacket>.Of(
            (ref PacketWriter w, ClientboundAddPlayerPacket p, PacketCodecContext _) =>
            {
                // 1.9+ spawn player dropped the held-item short (CurrentItem is unused here).
                w.WriteVarInt(p.EntityId);
                w.WriteUuid(p.Uuid);
                w.WriteDouble(p.X);
                w.WriteDouble(p.Y);
                w.WriteDouble(p.Z);
                w.WriteAngle(p.Yaw);
                w.WriteAngle(p.Pitch);
                EntityMetadataCodec.WriteModern(ref w, p.Metadata, table, NbtWireFormat.JavaNamedRoot, ComponentWireEra.Legacy, componentJson: true, blockPos: blockPos);
            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                Guid uuid = r.ReadUuid();
                double x = r.ReadDouble(), y = r.ReadDouble(), z = r.ReadDouble();
                float yaw = r.ReadAngle(), pitch = r.ReadAngle();
                var meta = EntityMetadataCodec.ReadModern(ref r, table, NbtWireFormat.JavaNamedRoot, ComponentWireEra.Legacy, componentJson: true, blockPos: blockPos);
                return new ClientboundAddPlayerPacket(id, uuid, x, y, z, yaw, pitch, 0, meta);
            },
            WireShape.Of("varint,uuid,3*double,2*angle,metadata", $"{blockPos},{table.ShapeToken}"));

    internal static PositionMoveRotation ReadPositionMoveRotation(ref PacketReader r)
    {
        var pos = new Vec3d(r.ReadDouble(), r.ReadDouble(), r.ReadDouble());
        var delta = new Vec3d(r.ReadDouble(), r.ReadDouble(), r.ReadDouble());
        float yRot = r.ReadFloat(), xRot = r.ReadFloat();
        return new PositionMoveRotation(pos, delta, yRot, xRot);
    }

    internal static void WritePositionMoveRotation(ref PacketWriter w, PositionMoveRotation v)
    {
        w.WriteDouble(v.Position.X); w.WriteDouble(v.Position.Y); w.WriteDouble(v.Position.Z);
        w.WriteDouble(v.DeltaMovement.X); w.WriteDouble(v.DeltaMovement.Y); w.WriteDouble(v.DeltaMovement.Z);
        w.WriteFloat(v.YRot); w.WriteFloat(v.XRot);
    }

    /// <summary>The 1.20.3+ set-entity-data codec (network-NBT COMPONENT values). <paramref name="particles"/> supplies the era's particle option-shape and item-component tables; passing it is what makes a PARTICLE / PARTICLES metadata value decode structurally instead of collapsing the rest of the list into a raw tail.</summary>
    /// <remarks><paramref name="era"/> is mandatory because the interaction dialect changes independently of the surrounding metadata framing. Through 769 the style fields are <c>clickEvent</c>/<c>hoverEvent</c>, and <c>show_entity</c> is nested under <c>contents</c> as <c>type</c>/<c>id</c>, while 1.21.5 renames them <c>click_event</c>/<c>hover_event</c> and inlines <c>show_entity</c> as <c>id</c>/<c>uuid</c>. Reading a legacy frame with the modern dialect silently loses the hovered entity's UUID, and writing one emits field names a 765-769 client does not know.</remarks>
    /// <param name="table">The era's metadata serializer table.</param>
    /// <param name="era">The era's component interaction dialect: legacy through 769, modern from 770.</param>
    /// <param name="items">The era's item-stack wire representation.</param>
    /// <param name="particles">The era's particle decode inputs, or null to capture particles verbatim.</param>
    /// <returns>The codec.</returns>
    internal static PacketCodec<ClientboundSetEntityDataPacket> SetEntityDataModern(
        ModernMetadataTable table,
        ComponentWireEra era,
        MetadataItemEra items,
        MetadataParticleEra? particles = null) =>
        PacketCodec<ClientboundSetEntityDataPacket>.Of(
            (ref PacketWriter w, ClientboundSetEntityDataPacket p, PacketCodecContext ctx) =>
            {
                w.WriteVarInt(p.EntityId);
                EntityMetadataCodec.WriteModern(
                    ref w, p.Metadata, table, NbtWireFormat.JavaRootTagOrString, era,
                    particles: particles, items: items, context: ctx);
            },
            (ref PacketReader r, PacketCodecContext ctx) =>
            {
                int id = r.ReadVarInt();
                var meta = EntityMetadataCodec.ReadModern(
                    ref r, table, NbtWireFormat.JavaRootTagOrString, era,
                    particles: particles, items: items, context: ctx);
                return new ClientboundSetEntityDataPacket(id, meta);
            },
            WireShape.Of(
                "varint,metadata",
                $"{table.ShapeToken},{era},{items.Form},particles={particles?.Icons.ShapeToken ?? "none"}"));

    /// <summary>A set_entity_data codec whose COMPONENT/OPTIONAL_COMPONENT values are JSON strings rather than network NBT.</summary>
    /// <param name="table">The era's serializer-id table.</param>
    /// <param name="items">The era's item-stack wire representation.</param>
    /// <param name="blockPos">The BLOCK_POS packing layout for the era.</param>
    /// <param name="nbt">The root framing for COMPOUND_TAG values. Defaults to the pre-1.20.2 named root. 1.20.2 (protocol 764) is a hybrid era: network NBT already lost its root name there, but chat components were still JSON strings until 1.20.3, so 764 passes <see cref="NbtWireFormat.JavaUnnamedRoot"/> alongside <c>componentJson: true</c>.</param>
    internal static PacketCodec<ClientboundSetEntityDataPacket> SetEntityDataComponentJson(
        ModernMetadataTable table,
        MetadataItemEra items,
        BlockPosLayout blockPos = BlockPosLayout.Packed114,
        NbtWireFormat nbt = NbtWireFormat.JavaNamedRoot) =>
        PacketCodec<ClientboundSetEntityDataPacket>.Of(
            (ref PacketWriter w, ClientboundSetEntityDataPacket p, PacketCodecContext ctx) =>
            {
                w.WriteVarInt(p.EntityId);
                EntityMetadataCodec.WriteModern(ref w, p.Metadata, table, nbt, ComponentWireEra.Legacy, componentJson: true, blockPos: blockPos, items: items, context: ctx);
            },
            (ref PacketReader r, PacketCodecContext ctx) =>
            {
                int id = r.ReadVarInt();
                var meta = EntityMetadataCodec.ReadModern(ref r, table, nbt, ComponentWireEra.Legacy, componentJson: true, blockPos: blockPos, items: items, context: ctx);
                return new ClientboundSetEntityDataPacket(id, meta);
            },
            WireShape.Of("varint,metadata", $"{table.ShapeToken},json,{items.Form},{blockPos},{nbt}"));
}
