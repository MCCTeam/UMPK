using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityEquipmentCodecs
{
    /// <summary>1.8 entity equipment: entity id, slot short, and one item.</summary>
    /// <remarks>The slot short carries 1.8's OWN numbering, not the modern <c>EnumItemSlot</c> ordinal, so it has to be translated rather than cast. The 1.8 numbering is 0 held, 1 boots, 2 leggings, 3 chestplate, and 4 helmet, one short of the modern ordinals, which insert the off-hand at 1. Casting the raw short onto the modern enum read a boot as an off-hand item, a legging as a boot, a chestplate as a legging and a helmet as a chestplate, on every protocol-47 session.</remarks>
    public static readonly PacketCodec<ClientboundSetEquipmentPacket> SetEquipmentV1_8 =
        PacketCodec<ClientboundSetEquipmentPacket>.Of(
            static (ref PacketWriter w, ClientboundSetEquipmentPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteShort(ToLegacyEquipmentSlot(p.LegacySlot));
                EntityItemSlotCodec.WriteLegacy(ref w, p.LegacyItem ?? EntityItemSlot.Empty);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                Umpk.Game.Entities.EquipmentSlot slot = FromLegacyEquipmentSlot(r.ReadShort());
                var item = EntityItemSlotCodec.ReadLegacy(ref r);
                return new ClientboundSetEquipmentPacket(id, slot, item, null);
            });

    /// <summary>Maps a 1.8 equipment slot number (0 held, 1 boots ... 4 helmet) onto the modern slot.</summary>
    private static Umpk.Game.Entities.EquipmentSlot FromLegacyEquipmentSlot(short wire) => wire switch
    {
        0 => Umpk.Game.Entities.EquipmentSlot.MainHand,
        1 => Umpk.Game.Entities.EquipmentSlot.Feet,
        2 => Umpk.Game.Entities.EquipmentSlot.Legs,
        3 => Umpk.Game.Entities.EquipmentSlot.Chest,
        4 => Umpk.Game.Entities.EquipmentSlot.Head,
        _ => throw new ProtocolViolationException($"1.8 equipment slot {wire} is outside the 0-4 range."),
    };

    /// <summary>The inverse of <see cref="FromLegacyEquipmentSlot"/>.</summary>
    private static short ToLegacyEquipmentSlot(Umpk.Game.Entities.EquipmentSlot slot) => slot switch
    {
        Umpk.Game.Entities.EquipmentSlot.MainHand => 0,
        Umpk.Game.Entities.EquipmentSlot.Feet => 1,
        Umpk.Game.Entities.EquipmentSlot.Legs => 2,
        Umpk.Game.Entities.EquipmentSlot.Chest => 3,
        Umpk.Game.Entities.EquipmentSlot.Head => 4,

        // 1.8 has no off-hand and no body/saddle slot: there is no number to send.
        _ => throw new ProtocolViolationException($"Equipment slot {slot} does not exist on protocol 47."),
    };

    /// <summary>1.9 through 1.12.2 entity equipment: entity id, then the equipment slot as a VarInt ENUM ordinal (not the 1.8 short), then one legacy short-id item stack.</summary>
    /// <remarks>The slot widening is what makes this a real era rather than an alias of the 1.8 codec. Binding the 1.8 codec here would read the VarInt slot as the high half of a short and desynchronise the frame.</remarks>
    public static readonly PacketCodec<ClientboundSetEquipmentPacket> SetEquipmentV1_9 =
        MakeLegacySetEquipment(EntityItemSlotCodec.ReadLegacy, EntityItemSlotCodec.WriteLegacy);

    /// <summary>1.13/1.13.1 entity equipment: the same head as <see cref="SetEquipmentV1_9"/> with the flattening's short-id item stack (the damage short is gone).</summary>
    public static readonly PacketCodec<ClientboundSetEquipmentPacket> SetEquipmentV1_13 =
        MakeLegacySetEquipment(EntityItemSlotCodec.ReadShortId, EntityItemSlotCodec.WriteShortId);

    /// <summary>1.13.2 through 1.15.2 entity equipment: the same head with the present-flag/VarInt-id item stack the 1.13.2 Slot flip introduced. 1.16 replaces the single slot with the grouped list below.</summary>
    public static readonly PacketCodec<ClientboundSetEquipmentPacket> SetEquipmentV1_13_2 =
        MakeLegacySetEquipment(EntityItemSlotCodec.ReadPresentId, EntityItemSlotCodec.WritePresentId);

    /// <summary>Modern set equipment: entity id then a grouped slot list (top-bit continuation) of modern item stacks. No modern item codec is available yet, so the payload after the entity id is captured raw. TODO(inventory-item-codec).</summary>
    public static readonly PacketCodec<ClientboundSetEquipmentPacket> SetEquipmentV1_16 =
        PacketCodec<ClientboundSetEquipmentPacket>.Of(
            static (ref PacketWriter w, ClientboundSetEquipmentPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteBytes(p.ModernRaw ?? []);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                byte[] raw = r.ReadRemaining().ToArray();
                return new ClientboundSetEquipmentPacket(id, Umpk.Game.Entities.EquipmentSlot.MainHand, null, raw);
            });

    /// <summary>Builds a 1.9 through 1.15.2 equipment codec: VarInt entity id, VarInt equipment-slot ordinal, then one item stack in the era's own slot form. Only the item form moves across the three eras, so the head is written once and the slot codec is the parameter.</summary>
    private static PacketCodec<ClientboundSetEquipmentPacket> MakeLegacySetEquipment(
        ReaderFunc<EntityItemSlot> readSlot,
        WriterAction<EntityItemSlot> writeSlot) =>
        PacketCodec<ClientboundSetEquipmentPacket>.Of(
            (ref PacketWriter w, ClientboundSetEquipmentPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteVarInt((int)p.LegacySlot);
                writeSlot(ref w, p.LegacyItem ?? EntityItemSlot.Empty);
            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                var slot = (Umpk.Game.Entities.EquipmentSlot)r.ReadVarInt();
                EntityItemSlot item = readSlot(ref r);
                return new ClientboundSetEquipmentPacket(id, slot, item, null);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetEquipment(PacketBindings bindings)
    {
        // 107-578 spell this set_equipped_item and were one 21-protocol marker, so no other entity's held or worn item was ever observed there. It is NOT an alias of either neighbour: 1.9 widened the slot from the 1.8 SHORT to a VarInt enum ordinal, and 1.16 replaced the single slot with a top-bit-continuation group list. So the band needs its own codecs, and it splits again on the ITEM form alone: 107-340 legacy short-id/damage, 393-401 the flattening's short-id (damage gone), 404-578 the 1.13.2 present-flag/VarInt-id Slot flip. Only the last of those three is the head the 1.16 codec shares, which is why binding the modern codec across the band would have silently reported every slot as main-hand.
        bindings.Packet(EntityPackets.Clientbound.SetEquipment)
            .From(JavaProtocols.V1_8, EntityEquipmentCodecs.SetEquipmentV1_8)
            .From(JavaProtocols.V1_9, EntityEquipmentCodecs.SetEquipmentV1_9)
            .From(JavaProtocols.V1_13, EntityEquipmentCodecs.SetEquipmentV1_13)
            .From(JavaProtocols.V1_13_2, EntityEquipmentCodecs.SetEquipmentV1_13_2)
            .From(JavaProtocols.V1_16, EntityEquipmentCodecs.SetEquipmentV1_16)
            .AliasedAs(Identifier.Minecraft("entity_equipment"))
            .AliasedAs(Identifier.Minecraft("set_equipped_item"), JavaProtocols.V1_9, JavaProtocols.V1_15_2);
    }
}
