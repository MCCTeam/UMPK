using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>Entity-metadata (set_entity_data) packet codecs, one per serializer-table era, plus the era-key resolver used by the registration bindings.</summary>
internal static partial class EntityDataCodecs
{
    private static readonly MetadataItemEra LegacyItems = new(
        ItemStackCodecs.ReadLegacyStack,
        ItemStackCodecs.WriteLegacyStack,
        "legacy");

    private static readonly MetadataItemEra ShortIdItems = new(
        ItemStackCodecs.ReadShortIdStack,
        ItemStackCodecs.WriteShortIdStack,
        "shortid");

    private static readonly MetadataItemEra PresentIdItems = new(
        ItemStackCodecs.ReadPresentIdStack,
        ItemStackCodecs.WritePresentIdStack,
        "presentid");

    private static readonly MetadataItemEra VarIntIdItems = new(
        ItemStackCodecs.ReadVarIntIdStack,
        ItemStackCodecs.WriteVarIntIdStack,
        "varintid");

    private static MetadataItemEra ComponentItems(ItemComponentTable table) => new(
        ItemPacketCodecShared.ComponentReader(table),
        ItemPacketCodecShared.ComponentWriter(table),
        "countfirst/" + table.ShapeToken);

    /// <summary>1.8 entity metadata.</summary>
    public static readonly PacketCodec<ClientboundSetEntityDataPacket> SetEntityDataV1_8 =
        PacketCodec<ClientboundSetEntityDataPacket>.Of(
            static (ref PacketWriter w, ClientboundSetEntityDataPacket p, PacketCodecContext context) =>
            {
                w.WriteVarInt(p.EntityId);
                EntityMetadataCodec.WriteLegacy(ref w, p.Metadata, context);
            },
            static (ref PacketReader r, PacketCodecContext context) =>
            {
                int id = r.ReadVarInt();
                return new ClientboundSetEntityDataPacket(id, EntityMetadataCodec.ReadLegacy(ref r, context));
            });

    /// <summary>770 (1.21.5) entity metadata: serializer table V1_21_5, 770 particle and item eras.</summary>
    public static readonly PacketCodec<ClientboundSetEntityDataPacket> SetEntityDataV1_21_5 =
        SetEntityDataModern(
            ModernMetadataTable.V1_21_5,
            ComponentWireEra.Modern,
            ComponentItems(ItemStackCodecs.ComponentsV1_21_5),
            new MetadataParticleEra(ParticleCodec.ModernV1_21_5, ItemStackCodecs.ComponentsV1_21_5));

    // Two independent axes move inside this band and do not move together:
    //
    //   * NBT root framing changes at 1.20.2 from a named root to an unnamed root whose first child is
    //     named (see NbtWireFormat).
    //   * Chat components change from GSON JSON strings to network NBT one release LATER, at 1.20.3.
    //     1.20.1 uses JSON component strings and 1.20.4 uses trusted network-NBT components. Their wire
    //     forms are:
    //       764: 02 06 01 15 7b 22 74 65 78 74 ...   OPTIONAL_COMPONENT, present, VarInt len 0x15,
    //                                                then the 21 ASCII bytes {"text":"TestZombie"}
    //       765: 02 06 01 08 00 0a 54 65 73 74 ...   OPTIONAL_COMPONENT, present, TAG_String(0x08),
    //                                                length 10, then TestZombie
    //     Reading the 764 form as NBT would consume the JSON string's length prefix as a tag id.
    //
    // So 764 is a hybrid era of its own: unnamed-root NBT with JSON components.
    //
    // The serializer TABLE also splits inside the band, and not at the same place again. The table has 28 entries with VILLAGER_DATA at id 18 through 1.20.4, then 31 entries with VILLAGER_DATA at id 19 from 1.20.5 through 1.21.4. Using the later table on 764-767 would interpret id 18 as PARTICLES and desynchronize the metadata list.

    /// <summary>764 (1.20.2) entity metadata: the 28-entry V1_19_4 serializer table, JSON-string chat components, but the 1.20.2 UNNAMED-root network NBT. See the band note above.</summary>
    public static readonly PacketCodec<ClientboundSetEntityDataPacket> SetEntityDataV1_20_2 =
        SetEntityDataComponentJson(ModernMetadataTable.V1_19_4, VarIntIdItems, nbt: NbtWireFormat.JavaUnnamedRoot);

    /// <summary>765 (1.20.3-1.20.4) entity metadata: network-NBT chat components (the 1.20.3 change) over the still-28-entry V1_19_4 serializer table. See the band note above.</summary>
    public static readonly PacketCodec<ClientboundSetEntityDataPacket> SetEntityDataV1_20_3 =
        SetEntityDataModern(ModernMetadataTable.V1_19_4, ComponentWireEra.Legacy, VarIntIdItems);

    /// <summary>775 (26.1) entity metadata: serializer table V26_1, 775 particle and item eras.</summary>
    public static readonly PacketCodec<ClientboundSetEntityDataPacket> SetEntityDataV26_1 =
        SetEntityDataModern(
            ModernMetadataTable.V26_1,
            ComponentWireEra.Modern,
            ComponentItems(ItemPacketCodecShared.Table775),
            new MetadataParticleEra(ParticleCodec.ModernV26_1, ItemPacketCodecShared.Table775));

    // Pre-1.20.2 entity metadata encodes COMPONENT and OPTIONAL_COMPONENT as JSON strings rather than network NBT, while compound tags use named-root NBT. The serializer id table is the 1.19-era order (no LONG before 1.19.3; OPTIONAL_BLOCK_STATE and the sniffer/vector3/quaternion tail arrive at 1.19.4).

    /// <summary>1.19-1.19.2 entity metadata (JSON-component era, serializer table V1_19).</summary>
    public static readonly PacketCodec<ClientboundSetEntityDataPacket> SetEntityDataV1_19 = SetEntityDataComponentJson(ModernMetadataTable.V1_19, PresentIdItems);

    /// <summary>1.19.3 entity metadata (serializer table V1_19_3).</summary>
    public static readonly PacketCodec<ClientboundSetEntityDataPacket> SetEntityDataV1_19_3 = SetEntityDataComponentJson(ModernMetadataTable.V1_19_3, PresentIdItems);

    /// <summary>1.19.4-1.20.1 entity metadata (serializer table V1_19_4).</summary>
    public static readonly PacketCodec<ClientboundSetEntityDataPacket> SetEntityDataV1_19_4 = SetEntityDataComponentJson(ModernMetadataTable.V1_19_4, PresentIdItems);

    /// <summary>477-578 (1.14-1.15.2) entity metadata: the 19-serializer flattening-era table, JSON-string chat components, named-root NBT, varintId item stacks (shared with the 1.19 componentJson path).</summary>
    public static readonly PacketCodec<ClientboundSetEntityDataPacket> SetEntityDataV1_14 =
        SetEntityDataComponentJson(ModernMetadataTable.V1_14, PresentIdItems);

    /// <summary>393-404 (1.13-1.13.2) entity metadata: the 16-serializer table ending at PARTICLE (no VILLAGER_DATA/OPTIONAL_UNSIGNED_INT/POSE), JSON-string chat components, named-root NBT. Shares the same componentJson decode loop as 1.14 (item stacks use the 1.13 short-id form).</summary>
    public static readonly PacketCodec<ClientboundSetEntityDataPacket> SetEntityDataV1_13 =
        SetEntityDataComponentJson(ModernMetadataTable.V1_13, ShortIdItems);

    /// <summary>404 (1.13.2) metadata uses the present-flag/VarInt-id item-stack form.</summary>
    public static readonly PacketCodec<ClientboundSetEntityDataPacket> SetEntityDataV1_13_2 =
        SetEntityDataComponentJson(ModernMetadataTable.V1_13, PresentIdItems);

    /// <summary>set_entity_data for 1.9-1.11.2 (protocols 107-316): the 13-serializer typed metadata table (<see cref="ModernMetadataTable.V1_9"/>). Same componentJson decode loop as 1.13 (JSON chat components, named-root NBT, decoded short-id item stacks), but block positions use the pre-1.14 y-in-the-middle packing (the 1.14 layout switch is later).</summary>
    public static readonly PacketCodec<ClientboundSetEntityDataPacket> SetEntityDataV1_9 =
        SetEntityDataComponentJson(ModernMetadataTable.V1_9, LegacyItems, BlockPosLayout.PrePacked114);

    /// <summary>set_entity_data for 1.12-1.12.2 (protocols 335-340): the 14-serializer table (<see cref="ModernMetadataTable.V1_12"/>, adds COMPOUND_TAG). Otherwise identical to <see cref="SetEntityDataV1_9"/> (pre-1.14 block-position packing).</summary>
    public static readonly PacketCodec<ClientboundSetEntityDataPacket> SetEntityDataV1_12 =
        SetEntityDataComponentJson(ModernMetadataTable.V1_12, LegacyItems, BlockPosLayout.PrePacked114);

    // The tail versions 768/769, 773, and 774 each carry a serializer registration order that mid-list-reorders relative to the V1_21_5 table (see ModernMetadataTable), so a version reading its own metadata stream with the V1_21_5 table would mis-map serializer ids. 771/772 reuse V1_21_5 and 775 reuses V26_1, so no member is needed for those. The codec body is identical to SetEntityDataModern; only the serializer table differs, resolved once at construction.

    // A raw particle tail stops the decode and swallows the rest of the metadata list. Structural decoding therefore needs the particle and item tables for the exact era.
    //
    // Decoding one needs the era's particle option-shape table and the era's item-component table (the "item" particle option carries a whole stack), and those move on their OWN schedule, independent of the metadata serializer table. The pairs below mirror the level_particles timeline exactly, which is the other consumer of the same two tables. PacketCodecContext deliberately exposes no protocol number, so this is per-instance data rather than an inline version branch.
    //
    // This begins at 1.21.2, the first protocol with both the PARTICLES serializer and a particle option-shape table. Protocols 764-767 keep particle data verbatim because no complete shape table is available for that band.

    /// <summary>766-767 (1.20.5-1.21.1) entity metadata: the 31-entry pre-1.21.5 serializer table with NO particle decoding. Vanilla has no PARTICLES serializer before 1.21.2 and no particle shape table exists for this band, so a particle value stays captured rather than being decoded through a neighbouring era's ids. 768 gains the wired form below.</summary>
    public static readonly PacketCodec<ClientboundSetEntityDataPacket> SetEntityDataV1_20_5 =
        SetEntityDataModern(
            ModernMetadataTable.V1_21_2,
            ComponentWireEra.Legacy,
            ComponentItems(ItemPacketCodecShared.Table766));

    /// <summary>768 (1.21.2/1.21.3) entity metadata: pre-1.21.5 serializer table, 768 particles.</summary>
    public static readonly PacketCodec<ClientboundSetEntityDataPacket> SetEntityDataV1_21_2 =
        SetEntityDataModern(
            ModernMetadataTable.V1_21_2,
            ComponentWireEra.Legacy,
            ComponentItems(ItemPacketCodecShared.Table768),
            new MetadataParticleEra(ParticleCodec.ModernV1_21_2, ItemPacketCodecShared.Table768));

    /// <summary>769 (1.21.4) entity metadata: the 768 serializer table with the 769 particle ids.</summary>
    public static readonly PacketCodec<ClientboundSetEntityDataPacket> SetEntityDataV1_21_4 =
        SetEntityDataModern(
            ModernMetadataTable.V1_21_2,
            ComponentWireEra.Legacy,
            ComponentItems(ItemPacketCodecShared.Table769),
            new MetadataParticleEra(ParticleCodec.ModernV1_21_4, ItemPacketCodecShared.Table769));

    /// <summary>771/772 (1.21.6-1.21.8) entity metadata: the 770 tables with the 771 item component era.</summary>
    public static readonly PacketCodec<ClientboundSetEntityDataPacket> SetEntityDataV1_21_6 =
        SetEntityDataModern(
            ModernMetadataTable.V1_21_5,
            ComponentWireEra.Modern,
            ComponentItems(ItemPacketCodecShared.Table771),
            new MetadataParticleEra(ParticleCodec.ModernV1_21_5, ItemPacketCodecShared.Table771));

    /// <summary>773 (1.21.9/10) entity metadata (37-entry table; resolvable profile at id 36).</summary>
    public static readonly PacketCodec<ClientboundSetEntityDataPacket> SetEntityDataV1_21_9 =
        SetEntityDataModern(
            ModernMetadataTable.V1_21_9,
            ComponentWireEra.Modern,
            ComponentItems(ItemPacketCodecShared.Table773),
            new MetadataParticleEra(ParticleCodec.ModernV1_21_9, ItemPacketCodecShared.Table773));

    /// <summary>774 (1.21.11) entity metadata (39-entry table; resolvable profile at id 37).</summary>
    public static readonly PacketCodec<ClientboundSetEntityDataPacket> SetEntityDataV1_21_11 =
        SetEntityDataModern(
            ModernMetadataTable.V1_21_11,
            ComponentWireEra.Modern,
            ComponentItems(ItemPacketCodecShared.Table774),
            new MetadataParticleEra(ParticleCodec.ModernV1_21_9, ItemPacketCodecShared.Table774));

    /// <summary>776 (26.2) entity metadata: the 26.1 serializer table with the 776 particle/item eras.</summary>
    public static readonly PacketCodec<ClientboundSetEntityDataPacket> SetEntityDataV26_2 =
        SetEntityDataModern(
            ModernMetadataTable.V26_1,
            ComponentWireEra.Modern,
            ComponentItems(ItemStackCodecs.ComponentsV26_2),
            new MetadataParticleEra(ParticleCodec.ModernV26_2, ItemStackCodecs.ComponentsV26_2));

    /// <summary>777 (26.3) entity metadata: the 777 serializer table (DYE_COLOR appended at id 43) with the 777 particle/item eras.</summary>
    public static readonly PacketCodec<ClientboundSetEntityDataPacket> SetEntityDataV26_3 =
        SetEntityDataModern(
            ModernMetadataTable.V777,
            ComponentWireEra.Modern,
            ComponentItems(ItemStackCodecs.ComponentsV26_3),
            new MetadataParticleEra(ParticleCodec.ModernV26_3, ItemStackCodecs.ComponentsV26_3));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetEntityData(PacketBindings bindings)
    {
        // Entity metadata is a per-era serializer table: the 13-entry 1.9 table, +NBT at 1.12, the 16-entry 1.13 table ending at PARTICLE, then the 19-entry 1.14 flattening table which runs unchanged all the way to 1.18.2. From 1.19 the modern-tail tables mid-list-reorder repeatedly (1.19/1.19.3/1.19.4, the 1.21.2 pre-1.21.5 table, 1.21.9/1.21.11 resolvable-profile tables, 26.1).
        //
        // The 19-entry table from BYTE through POSE is unchanged from 1.14.4 through 1.18.2. Components remain JSON strings, compound tags remain named-root NBT, and block positions retain the 1.14 X/Z/Y packing, so the 1.14 era codec covers that entire span.
        bindings.Packet(EntityPackets.Clientbound.SetEntityData)
            .From(JavaProtocols.V1_8, EntityDataCodecs.SetEntityDataV1_8)
            .From(JavaProtocols.V1_9, EntityDataCodecs.SetEntityDataV1_9)
            .From(JavaProtocols.V1_12, EntityDataCodecs.SetEntityDataV1_12)
            .From(JavaProtocols.V1_13, EntityDataCodecs.SetEntityDataV1_13)
            .From(JavaProtocols.V1_13_2, EntityDataCodecs.SetEntityDataV1_13_2)
            .From(JavaProtocols.V1_14, EntityDataCodecs.SetEntityDataV1_14)
            .From(JavaProtocols.V1_19, EntityDataCodecs.SetEntityDataV1_19)
            .From(JavaProtocols.V1_19_3, EntityDataCodecs.SetEntityDataV1_19_3)
            .From(JavaProtocols.V1_19_4, EntityDataCodecs.SetEntityDataV1_19_4)
            // Protocol 764 is a hybrid era with unnamed-root NBT and JSON components. Protocol 765 keeps the 28-entry table, while 766/767 share the 31-entry 1.21.2 table.
            .From(JavaProtocols.V1_20_2, EntityDataCodecs.SetEntityDataV1_20_2)
            .From(JavaProtocols.V1_20_3, EntityDataCodecs.SetEntityDataV1_20_3)
            .From(JavaProtocols.V1_20_5, EntityDataCodecs.SetEntityDataV1_20_5)
            .From(JavaProtocols.V1_21_2, EntityDataCodecs.SetEntityDataV1_21_2)
            // From 1.21.2 the steps also track the PARTICLE option-shape and item-component eras, which move on their own schedule: a metadata particle value is only decodable against the era's shape table; otherwise a raw capture takes the whole rest of the metadata list with it. The 768/769, 771/772 and 775/776 splits below exist for that reason alone and mirror the level_particles timeline, which consumes the same two tables. 764-767 keep the capture: no shape table exists below 766, and 764/765 predate vanilla's PARTICLES serializer.
            .From(JavaProtocols.V1_21_4, EntityDataCodecs.SetEntityDataV1_21_4)
            .From(JavaProtocols.V1_21_5, EntityDataCodecs.SetEntityDataV1_21_5)
            .From(JavaProtocols.V1_21_6, EntityDataCodecs.SetEntityDataV1_21_6)
            .From(JavaProtocols.V1_21_9, EntityDataCodecs.SetEntityDataV1_21_9)
            .From(JavaProtocols.V1_21_11, EntityDataCodecs.SetEntityDataV1_21_11)
            .From(JavaProtocols.V26_1, EntityDataCodecs.SetEntityDataV26_1)
            .From(JavaProtocols.V26_2, EntityDataCodecs.SetEntityDataV26_2)
            .From(JavaProtocols.V26_3, EntityDataCodecs.SetEntityDataV26_3);
    }
}
