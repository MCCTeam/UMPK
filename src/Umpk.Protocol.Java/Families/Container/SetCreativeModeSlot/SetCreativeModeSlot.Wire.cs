using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.ItemPacketCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class ContainerCodecs
{
    /// <summary>1.8 creative-set-slot: short slot, legacy item.</summary>
    public static PacketCodec<ServerboundSetCreativeModeSlotPacket> CreativeSlotV1_8 { get; } =
        PacketCodec<ServerboundSetCreativeModeSlotPacket>.Of(
            static (ref PacketWriter w, ServerboundSetCreativeModeSlotPacket p, PacketCodecContext c) =>
            {
                w.WriteShort(p.Slot);
                ItemStackCodecs.WriteLegacyStack(ref w, p.Item, c);
            },
            static (ref PacketReader r, PacketCodecContext c) =>
            {
                short slot = r.ReadShort();
                ItemStack item = ItemStackCodecs.ReadLegacyStack(ref r, c);
                return new ServerboundSetCreativeModeSlotPacket(slot, item, IsLegacy: true);
            });

    /// <summary>Modern set-creative-mode-slot: short slot, DELIMITED (untrusted) item stack.</summary>
    public static PacketCodec<ServerboundSetCreativeModeSlotPacket> CreativeSlot770 { get; } = MakeCreativeSlot(Table770);

    /// <summary>26.2 set-creative-mode-slot.</summary>
    public static PacketCodec<ServerboundSetCreativeModeSlotPacket> CreativeSlot776 { get; } = MakeCreativeSlot(Table776);

    /// <summary>1.9-1.12.2 set-creative-mode-slot: short slot, LEGACY (id, count, damage, NBT) item stack. Same wire shape as the 1.8 <c>creative_inventory_action</c> (<see cref="CreativeSlotV1_8"/>); the 1.8 dataset carries it under its own packet identity, so only the identity differs, not the bytes.</summary>
    public static PacketCodec<ServerboundSetCreativeModeSlotPacket> CreativeSlotV1_9 { get; } =
        PacketCodec<ServerboundSetCreativeModeSlotPacket>.Of(
            static (ref PacketWriter w, ServerboundSetCreativeModeSlotPacket p, PacketCodecContext c) =>
            {
                w.WriteShort(p.Slot);
                ItemStackCodecs.WriteLegacyStack(ref w, p.Item, c);
            },
            static (ref PacketReader r, PacketCodecContext c) =>
            {
                short slot = r.ReadShort();
                ItemStack item = ItemStackCodecs.ReadLegacyStack(ref r, c);
                return new ServerboundSetCreativeModeSlotPacket(slot, item);
            });

    /// <summary>1.13/1.13.1 set-creative-mode-slot: short slot, short-id item stack.</summary>
    public static PacketCodec<ServerboundSetCreativeModeSlotPacket> CreativeSlotV1_13 { get; } = MakeCreativeSlotPre114(present: false);

    /// <summary>1.13.2 set-creative-mode-slot: short slot, bool-present + VarInt id item stack.</summary>
    public static PacketCodec<ServerboundSetCreativeModeSlotPacket> CreativeSlotV1_13_2 { get; } = MakeCreativeSlotPre114(present: true);

    /// <summary>477-763 (1.14-1.20.1) set-creative-mode-slot: short slot + present-id stack with a NAMED NBT root. The packet's field layout never changes across the pre-component eras, so the only thing that moves is the stack form underneath: 1.13/1.13.1 short-id, 1.13.2 present-id, and the unnamed-root varintId stack from 1.20.2 (<see cref="CreativeSlotV1_20_2"/>). Empty present-id and component stacks both encode as one zero byte, so a non-empty stack is required to distinguish these wire forms.</summary>
    public static PacketCodec<ServerboundSetCreativeModeSlotPacket> CreativeSlotV1_14 { get; } =
        MakeCreativeSlot(StackWire.PresentId);

    /// <summary>764/765 creative slot: short slot + varintId stack.</summary>
    public static PacketCodec<ServerboundSetCreativeModeSlotPacket> CreativeSlotV1_20_2 { get; } =
        MakeCreativeSlot(StackWire.VarIntId);

    /// <summary>766 creative slot: short slot + FULL (non-delimited) component stack.</summary>
    public static PacketCodec<ServerboundSetCreativeModeSlotPacket> CreativeSlotV1_20_5 { get; } =
        MakeCreativeSlot(StackWire.Components(Table766));

    /// <summary>767 creative slot.</summary>
    public static PacketCodec<ServerboundSetCreativeModeSlotPacket> CreativeSlotV1_21 { get; } =
        MakeCreativeSlot(StackWire.Components(Table767));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetCreativeModeSlot(PacketBindings bindings)
    {
        // The packet fields do not change, so its eras are purely the item-stack form: 1.9-1.12.2 legacy stack, 1.13/1.13.1 short-id, 1.13.2-1.20.1 present-id with a NAMED NBT root, 1.20.2-1.20.3 the unnamed-root varintId stack, then structured components.
        PacketTimelineBuilder<ServerboundSetCreativeModeSlotPacket> creativeSlot =
            bindings.Packet(ItemPackets.Serverbound.SetCreativeModeSlot)
                .From(JavaProtocols.V1_9, ContainerCodecs.CreativeSlotV1_9)
                .From(JavaProtocols.V1_13, ContainerCodecs.CreativeSlotV1_13)
                .From(JavaProtocols.V1_13_2, ContainerCodecs.CreativeSlotV1_13_2)
                .From(JavaProtocols.V1_14, ContainerCodecs.CreativeSlotV1_14)
                .From(JavaProtocols.V1_20_2, ContainerCodecs.CreativeSlotV1_20_2)
                .From(JavaProtocols.V1_20_5, ContainerCodecs.CreativeSlotV1_20_5)
                .From(JavaProtocols.V1_21, ContainerCodecs.CreativeSlotV1_21);

        foreach ((int protocol, string era, ItemComponentTable table) in ItemPacketCodecShared.ComponentEras)
            creativeSlot.From(protocol, ItemPacketCodecShared.MakeCreativeSlot(table), $"MakeCreativeSlot({era})");

    }
}
