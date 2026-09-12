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
    /// <summary>1.8 window-items: ubyte window id, short count, legacy item array.</summary>
    public static PacketCodec<ClientboundContainerSetContentPacket> ContainerSetContentV1_8 { get; } =
        PacketCodec<ClientboundContainerSetContentPacket>.Of(
            static (ref PacketWriter w, ClientboundContainerSetContentPacket p, PacketCodecContext c) =>
            {
                w.WriteByte((byte)p.ContainerId);
                w.WriteShort((short)p.Items.Count);
                foreach (ItemStack stack in p.Items)
                    ItemStackCodecs.WriteLegacyStack(ref w, stack, c);

            },
            static (ref PacketReader r, PacketCodecContext c) =>
            {
                int id = r.ReadByte();
                int count = r.ReadShort();
                var items = new ItemStack[count];
                for (int i = 0; i < count; i++)
                    items[i] = ItemStackCodecs.ReadLegacyStack(ref r, c);

                return new ClientboundContainerSetContentPacket(id, 0, items, ItemStack.Empty);
            });

    /// <summary>Modern container-set-content: VarInt id, VarInt state, item list, carried item.</summary>
    public static PacketCodec<ClientboundContainerSetContentPacket> ContainerSetContent770 { get; } = MakeContainerSetContent(Table770);

    /// <summary>26.2 container-set-content.</summary>
    public static PacketCodec<ClientboundContainerSetContentPacket> ContainerSetContent776 { get; } = MakeContainerSetContent(Table776);

    /// <summary>393/401 (1.13/1.13.1) container-set-content: byte containerId + short count + short-id stacks.</summary>
    public static PacketCodec<ClientboundContainerSetContentPacket> ContainerSetContentV1_13 { get; } =
        PacketCodec<ClientboundContainerSetContentPacket>.Of(
            (ref PacketWriter w, ClientboundContainerSetContentPacket p, PacketCodecContext c) =>
            {
                w.WriteByte((byte)p.ContainerId);
                w.WriteShort((short)p.Items.Count);
                foreach (ItemStack stack in p.Items)
                    ItemStackCodecs.WriteShortIdStack(ref w, stack, c);

            },
            (ref PacketReader r, PacketCodecContext c) =>
            {
                int id = r.ReadByte();
                int count = r.ReadShort();
                var items = new ItemStack[count];
                for (int i = 0; i < count; i++)
                    items[i] = ItemStackCodecs.ReadShortIdStack(ref r, c);

                return new ClientboundContainerSetContentPacket(id, 0, items, ItemStack.Empty);
            });

    /// <summary>404 (1.13.2) container-set-content: byte containerId + short count + present-flag + VarInt-id stacks.</summary>
    public static PacketCodec<ClientboundContainerSetContentPacket> ContainerSetContentV1_13_2 { get; } =
        PacketCodec<ClientboundContainerSetContentPacket>.Of(
            (ref PacketWriter w, ClientboundContainerSetContentPacket p, PacketCodecContext c) =>
            {
                w.WriteByte((byte)p.ContainerId);
                w.WriteShort((short)p.Items.Count);
                foreach (ItemStack stack in p.Items)
                    ItemStackCodecs.WritePresentIdStack(ref w, stack, c);

            },
            (ref PacketReader r, PacketCodecContext c) =>
            {
                int id = r.ReadByte();
                int count = r.ReadShort();
                var items = new ItemStack[count];
                for (int i = 0; i < count; i++)
                    items[i] = ItemStackCodecs.ReadPresentIdStack(ref r, c);

                return new ClientboundContainerSetContentPacket(id, 0, items, ItemStack.Empty);
            });

    /// <summary>477-755 (1.14-1.17) container-set-content: byte containerId + SHORT count + present-id stacks. No state id and no trailing carried item (both added at 1.17.1, see <see cref="ContainerSetContentV1_17_1"/>). The 1.16-1.17 wire is byte-identical to 1.14. The stacks' NBT root is NAMED on this band (see <see cref="ItemStackCodecs.ReadPresentIdStack"/>).</summary>
    public static PacketCodec<ClientboundContainerSetContentPacket> ContainerSetContentV1_14 { get; } =
        PacketCodec<ClientboundContainerSetContentPacket>.Of(
            (ref PacketWriter w, ClientboundContainerSetContentPacket p, PacketCodecContext c) =>
            {
                w.WriteByte((byte)p.ContainerId);
                w.WriteShort((short)p.Items.Count);
                foreach (ItemStack stack in p.Items)
                    ItemStackCodecs.WritePresentIdStack(ref w, stack, c);

            },
            (ref PacketReader r, PacketCodecContext c) =>
            {
                int id = r.ReadByte();
                int count = r.ReadShort();
                var items = new ItemStack[count];
                for (int i = 0; i < count; i++)
                    items[i] = ItemStackCodecs.ReadPresentIdStack(ref r, c);

                return new ClientboundContainerSetContentPacket(id, 0, items, ItemStack.Empty);
            });

    /// <summary>756-763 (1.17.1-1.20.1) container-set-content: ubyte container id, VarInt state id, VarInt-prefixed item list, trailing carried stack, all present-id stacks with a NAMED NBT root. The state id, the VarInt list framing and the carried stack were all added at 1.17.1 (1.16-1.17 still use the short-count no-carried <see cref="ContainerSetContentV1_14"/> form). ClientboundContainerSetContentPacket (readUnsignedByte id, readVarInt stateId, readCollection(readItem), readItem carried). The field layout survives to 1.20.3, but 1.20.2 flips the stack's NBT root to the unnamed network form, so 764/765 take <see cref="ContainerSetContentV1_20_2"/>.</summary>
    public static PacketCodec<ClientboundContainerSetContentPacket> ContainerSetContentV1_17_1 { get; } =
        MakeSetContent(StackWire.PresentId);

    /// <summary>764/765 (1.20.2-1.20.3) container-set-content: identical fields to <see cref="ContainerSetContentV1_17_1"/>, but 1.20.2 dropped the NBT root name from the network form, so the stacks are varintId stacks with unnamed-root NBT.</summary>
    public static PacketCodec<ClientboundContainerSetContentPacket> ContainerSetContentV1_20_2 { get; } =
        MakeSetContent(StackWire.VarIntId);

    /// <summary>766 container-set-content.</summary>
    public static PacketCodec<ClientboundContainerSetContentPacket> ContainerSetContentV1_20_5 { get; } =
        MakeSetContent(StackWire.Components(Table766));

    /// <summary>767 container-set-content.</summary>
    public static PacketCodec<ClientboundContainerSetContentPacket> ContainerSetContentV1_21 { get; } =
        MakeSetContent(StackWire.Components(Table767));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareContainerSetContent(PacketBindings bindings)
    {
        // The flattened varintId slot carries 1.14 through 1.20.1 unchanged; 1.17.1 added the state id, the VarInt-prefixed list framing and the trailing carried stack. So 1.14-1.17 (477-755) use the short-count no-carried V1_14 form and 1.17.1-1.20.3 (756-765) use the state-id V1_17_1 form. The V1_8 form covers 1.8-1.12.2: the wire shape is identical and the slot keeps its damage short until the flattening, so the short-id V1_13 form starts at 1.13, not 1.9 (see container_set_slot). Through 1.20.1 the stack NBT root is named; 1.20.2 changes it to unnamed. An empty tag is a bare TAG_End byte under both forms, so only a stack carrying NBT distinguishes the eras. The split below therefore uses named roots for 756-763 and unnamed roots for 764/765.
        PacketTimelineBuilder<ClientboundContainerSetContentPacket> setContent =
            bindings.Packet(ItemPackets.Clientbound.ContainerSetContent)
                .From(JavaProtocols.V1_8, ContainerCodecs.ContainerSetContentV1_8)
                .From(JavaProtocols.V1_13, ContainerCodecs.ContainerSetContentV1_13)
                .From(JavaProtocols.V1_13_2, ContainerCodecs.ContainerSetContentV1_13_2)
                .From(JavaProtocols.V1_14, ContainerCodecs.ContainerSetContentV1_14)
                .From(JavaProtocols.V1_17_1, ContainerCodecs.ContainerSetContentV1_17_1)
                .From(JavaProtocols.V1_20_2, ContainerCodecs.ContainerSetContentV1_20_2)
                .From(JavaProtocols.V1_20_5, ContainerCodecs.ContainerSetContentV1_20_5)
                .From(JavaProtocols.V1_21, ContainerCodecs.ContainerSetContentV1_21);

        foreach ((int protocol, string era, ItemComponentTable table) in ItemPacketCodecShared.ComponentEras)
            setContent.From(protocol, ItemPacketCodecShared.MakeContainerSetContent(table), $"MakeContainerSetContent({era})");

    }
}
