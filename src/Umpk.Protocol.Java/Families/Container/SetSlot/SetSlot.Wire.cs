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
    /// <summary>1.8 set-slot: SIGNED byte window id, short slot, legacy item. The window id MUST be read signed: the two negative ids it carries are the whole point. -1 is the carried/cursor stack and -2 is "a slot in the player inventory, addressed in Inventory index space", which is how a server-side give or a creative resync reaches the client outside any open window. Unsigned decoding would turn them into 255 and 254, bypassing the negative-id routing. This differs from container_set_content, whose window id is unsigned.</summary>
    public static PacketCodec<ClientboundContainerSetSlotPacket> SetSlotV1_8 { get; } =
        PacketCodec<ClientboundContainerSetSlotPacket>.Of(
            static (ref PacketWriter w, ClientboundContainerSetSlotPacket p, PacketCodecContext c) =>
                WriteSetSlot(ref w, p, c, StackWire.Legacy, containerIdIsVarInt: false, withStateId: false),
            static (ref PacketReader r, PacketCodecContext c) =>
                ReadSetSlot(ref r, c, StackWire.Legacy, containerIdIsVarInt: false, withStateId: false, isLegacy: true));

    /// <summary>Modern container-set-slot: VarInt container id, VarInt state id, short slot, item stack.</summary>
    public static PacketCodec<ClientboundContainerSetSlotPacket> ContainerSetSlot770 { get; } = MakeContainerSetSlot(Table770);

    /// <summary>26.2 container-set-slot.</summary>
    public static PacketCodec<ClientboundContainerSetSlotPacket> ContainerSetSlot776 { get; } = MakeContainerSetSlot(Table776);

    /// <summary>756-763 (1.17.1-1.20.1) container-set-slot: sbyte container id, VarInt state id, short slot, present-id stack with a NAMED NBT root. The state id was added at 1.17.1. The field layout survives to 1.20.3, but 1.20.2 flips the stack's NBT root to the unnamed network form, so 764/765 take <see cref="ContainerSetSlotV1_20_2"/>.</summary>
    public static PacketCodec<ClientboundContainerSetSlotPacket> ContainerSetSlotV1_17_1 { get; } =
        MakeSetSlot(StackWire.PresentId);

    /// <summary>764/765 (1.20.2-1.20.3) container-set-slot: identical fields to <see cref="ContainerSetSlotV1_17_1"/> with the unnamed-root (varintId) stack 1.20.2 introduced.</summary>
    public static PacketCodec<ClientboundContainerSetSlotPacket> ContainerSetSlotV1_20_2 { get; } =
        MakeSetSlot(StackWire.VarIntId);

    /// <summary>766 container-set-slot (component stack, 1.20.5 table).</summary>
    public static PacketCodec<ClientboundContainerSetSlotPacket> ContainerSetSlotV1_20_5 { get; } =
        MakeSetSlot(StackWire.Components(Table766));

    /// <summary>767 container-set-slot (component stack, 1.21 table).</summary>
    public static PacketCodec<ClientboundContainerSetSlotPacket> ContainerSetSlotV1_21 { get; } =
        MakeSetSlot(StackWire.Components(Table767));

    /// <summary>1.9-1.12.2 container-set-slot: SIGNED byte containerId + short slot + LEGACY (id, count, damage, NBT) stack. The damage short survives until the flattening drops it at 1.13, so this band shares the 1.8 slot form and only the packet identity changed (1.8 sends <c>minecraft:set_slot</c>, 1.9+ <c>minecraft:container_set_slot</c>). The 107-340 datasets declare composite <c>(id &lt;&lt; 16) | damage</c> item identities. The container id is signed: -1 addresses the carried stack and -2 writes the player inventory.</summary>
    public static PacketCodec<ClientboundContainerSetSlotPacket> ContainerSetSlotV1_9 { get; } =
        PacketCodec<ClientboundContainerSetSlotPacket>.Of(
            static (ref PacketWriter w, ClientboundContainerSetSlotPacket p, PacketCodecContext c) =>
                WriteSetSlot(ref w, p, c, StackWire.Legacy, containerIdIsVarInt: false, withStateId: false),
            static (ref PacketReader r, PacketCodecContext c) =>
                ReadSetSlot(ref r, c, StackWire.Legacy, containerIdIsVarInt: false, withStateId: false));

    /// <summary>393/401 (1.13/1.13.1) container-set-slot: byte containerId + short slot + short-id stack. No state id.</summary>
    public static PacketCodec<ClientboundContainerSetSlotPacket> ContainerSetSlotV1_13 { get; } =
        PacketCodec<ClientboundContainerSetSlotPacket>.Of(
            static (ref PacketWriter w, ClientboundContainerSetSlotPacket p, PacketCodecContext c) =>
                WriteSetSlot(ref w, p, c, StackWire.ShortId, containerIdIsVarInt: false, withStateId: false),
            static (ref PacketReader r, PacketCodecContext c) =>
                ReadSetSlot(ref r, c, StackWire.ShortId, containerIdIsVarInt: false, withStateId: false));

    /// <summary>404 (1.13.2) container-set-slot: byte containerId + short slot + present-flag + VarInt-id stack (the 1.13.2 slot format change). No state id.</summary>
    public static PacketCodec<ClientboundContainerSetSlotPacket> ContainerSetSlotV1_13_2 { get; } =
        PacketCodec<ClientboundContainerSetSlotPacket>.Of(
            static (ref PacketWriter w, ClientboundContainerSetSlotPacket p, PacketCodecContext c) =>
                WriteSetSlot(ref w, p, c, StackWire.PresentId, containerIdIsVarInt: false, withStateId: false),
            static (ref PacketReader r, PacketCodecContext c) =>
                ReadSetSlot(ref r, c, StackWire.PresentId, containerIdIsVarInt: false, withStateId: false));

    /// <summary>477-755 (1.14-1.17) container-set-slot: byte containerId + short slot + present-id stack. No state id (that arrived at 1.17.1, see <see cref="ContainerSetSlotV1_17_1"/>). The 1.16-1.17 wire is byte-identical to 1.14 (the flattened slot did not change across this range). The stack's NBT root is NAMED on this band (see <see cref="ItemStackCodecs.ReadPresentIdStack"/>).</summary>
    public static PacketCodec<ClientboundContainerSetSlotPacket> ContainerSetSlotV1_14 { get; } =
        PacketCodec<ClientboundContainerSetSlotPacket>.Of(
            static (ref PacketWriter w, ClientboundContainerSetSlotPacket p, PacketCodecContext c) =>
                WriteSetSlot(ref w, p, c, StackWire.PresentId, containerIdIsVarInt: false, withStateId: false),
            static (ref PacketReader r, PacketCodecContext c) =>
                ReadSetSlot(ref r, c, StackWire.PresentId, containerIdIsVarInt: false, withStateId: false));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetSlot(PacketBindings bindings)
    {
        bindings.Packet(ItemPackets.Clientbound.LegacySetSlot)
            .From(JavaProtocols.V1_8, ContainerCodecs.SetSlotV1_8);
    }
}
