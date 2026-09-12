using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntitySpawnCodecs
{
    /// <summary>1.8 painting spawn: entity id, title, packed block position, and facing byte.</summary>
    public static readonly PacketCodec<ClientboundAddPaintingPacket> AddPaintingV1_8 =
        PacketCodec<ClientboundAddPaintingPacket>.Of(
            static (ref PacketWriter w, ClientboundAddPaintingPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteString(p.Title, 64);
                w.WriteBlockPos(p.Position, BlockPosLayout.PrePacked114);
                w.WriteByte(p.Facing);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundAddPaintingPacket(r.ReadVarInt(), r.ReadString(64), r.ReadBlockPos(BlockPosLayout.PrePacked114), r.ReadByte(), Uuid: null, MotiveId: null));

    /// <summary>Spawn painting, 107-340 (1.9-1.12.2): the 1.8 body with the entity UUID inserted after the entity id. The motive is still the painting's registry NAME as a UTF string.</summary>
    /// <remarks>1.9-1.12.1 add a UUID ahead of the title, while 1.8.4 has no UUID. The 1.8 codec would take the first byte of the UUID as the title's length prefix.</remarks>
    public static readonly PacketCodec<ClientboundAddPaintingPacket> AddPaintingV1_9 =
        PacketCodec<ClientboundAddPaintingPacket>.Of(
            static (ref PacketWriter w, ClientboundAddPaintingPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteUuid(p.Uuid ?? throw new ProtocolViolationException("A 1.9+ spawn-painting requires the entity uuid."));
                w.WriteString(p.Title, 64);
                w.WriteBlockPos(p.Position, BlockPosLayout.PrePacked114);
                w.WriteByte(p.Facing);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                Guid uuid = r.ReadUuid();
                string title = r.ReadString(64);
                BlockPos pos = r.ReadBlockPos(BlockPosLayout.PrePacked114);
                return new ClientboundAddPaintingPacket(id, title, pos, r.ReadByte(), uuid, MotiveId: null);
            });

    /// <summary>Spawn painting, 393-404 (1.13-1.13.2): the flattening replaced the motive NAME with its VarInt registry id; everything else keeps the 1.9 layout.</summary>
    /// <remarks>A VarInt id and a short UTF string can have the same encoded length, so binding the name form to this era may silently shift the block position rather than faulting.</remarks>
    public static readonly PacketCodec<ClientboundAddPaintingPacket> AddPaintingV1_13 = MakeAddPaintingModern(BlockPosLayout.PrePacked114);

    /// <summary>Spawn painting, 477-758 (1.14-1.18.2): the 1.13 body with the 1.14 block-position packing. The packet leaves the protocol at 1.19, where paintings spawn through <c>add_entity</c>.</summary>
    /// <remarks>1.14.4-1.18.2 use the same VarInt id, UUID, VarInt motive, block position, and unsigned direction layout, identical to 1.13.2's apart from the block-pos packing that moved for every packet at 1.14.</remarks>
    public static readonly PacketCodec<ClientboundAddPaintingPacket> AddPaintingV1_14 = MakeAddPaintingModern(BlockPosLayout.Packed114);

    private static PacketCodec<ClientboundAddPaintingPacket> MakeAddPaintingModern(BlockPosLayout layout) =>
        PacketCodec<ClientboundAddPaintingPacket>.Of(
            (ref PacketWriter w, ClientboundAddPaintingPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteUuid(p.Uuid ?? throw new ProtocolViolationException("A 1.9+ spawn-painting requires the entity uuid."));
                w.WriteVarInt(p.MotiveId ?? throw new ProtocolViolationException("A 1.13+ spawn-painting requires the motive registry id."));
                w.WriteBlockPos(p.Position, layout);
                w.WriteByte(p.Facing);
            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                Guid uuid = r.ReadUuid();
                int motive = r.ReadVarInt();
                BlockPos pos = r.ReadBlockPos(layout);
                return new ClientboundAddPaintingPacket(id, string.Empty, pos, r.ReadByte(), uuid, motive);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareAddPainting(PacketBindings bindings)
    {
        // Four era forms: 47 (no uuid), 107-340 (uuid added, motive still a name string), 393-404 (motive becomes a VarInt registry id) and 477-758 (the same body with the 1.14 block-pos packing). The packet leaves the protocol at 1.19, where paintings spawn through add_entity. 107-758 was one long marker, so no painting was ever tracked on any of those 24 protocols.
        bindings.Packet(EntityPackets.Clientbound.AddPainting)
            .From(JavaProtocols.V1_8, EntitySpawnCodecs.AddPaintingV1_8)
            .From(JavaProtocols.V1_9, EntitySpawnCodecs.AddPaintingV1_9)
            .From(JavaProtocols.V1_13, EntitySpawnCodecs.AddPaintingV1_13)
            .From(JavaProtocols.V1_14, EntitySpawnCodecs.AddPaintingV1_14);
    }
}
