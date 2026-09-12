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
    /// <summary>1.8 click-window: byte window id, short slot, byte button, short action, byte mode, legacy item.</summary>
    public static PacketCodec<ServerboundContainerClickPacket> ContainerClickV1_8 { get; } =
        PacketCodec<ServerboundContainerClickPacket>.Of(
            static (ref PacketWriter w, ServerboundContainerClickPacket p, PacketCodecContext c) =>
            {
                w.WriteByte((byte)p.ContainerId);
                w.WriteShort(p.Slot);
                w.WriteByte(p.Button);
                w.WriteShort(p.ActionNumber);
                w.WriteByte((byte)p.Mode);
                ItemStackCodecs.WriteLegacyStack(ref w, p.LegacyClickedItem ?? ItemStack.Empty, c);
            },
            static (ref PacketReader r, PacketCodecContext c) =>
            {
                int id = r.ReadByte();
                short slot = r.ReadShort();
                byte button = r.ReadByte();
                short action = r.ReadShort();
                byte mode = r.ReadByte();
                ItemStack item = ItemStackCodecs.ReadLegacyStack(ref r, c);
                return new ServerboundContainerClickPacket(id, 0, slot, button, mode, action, item, [], null);
            });

    /// <summary>1.21.5+ container-click: VarInt id/state, short slot, byte button, VarInt mode, changed-slots map, carried stack. The client sends raw predicted stacks; this codec hashes them per component (CRC32C over HashOps) at encode using the bound 770 table, keeping the client version-blind.</summary>
    public static PacketCodec<ServerboundContainerClickPacket> ContainerClick770 { get; } = MakeContainerClick(Table770);

    /// <summary>26.2 container-click (ContainerInput at the same wire ordinal as ClickType; identical shape, hashed with the bound 776 table).</summary>
    public static PacketCodec<ServerboundContainerClickPacket> ContainerClick776 { get; } = MakeContainerClick(Table776);

    /// <summary>1.13/1.13.1 container-click: byte window id, short slot, byte button, short action, byte mode, short-id item stack. 1.13.2 flips the item stack to bool-present + VarInt id (<see cref="ContainerClickV1_13_2"/>). The short-id stack begins at the flattening: 1.9-1.12.2 still carry the 1.8 damage short and use <see cref="ContainerClickV1_8"/>, whose wire shape is identical apart from the stack form.</summary>
    public static PacketCodec<ServerboundContainerClickPacket> ContainerClickV1_13 { get; } = MakeContainerClickPre114(presentIdStack: false);

    /// <summary>1.13.2 container-click: as 1.13 but the item stack is bool-present + VarInt id.</summary>
    public static PacketCodec<ServerboundContainerClickPacket> ContainerClickV1_13_2 { get; } = MakeContainerClickPre114(presentIdStack: true);

    /// <summary>768 (1.21.2/1.21.3) container-click: VarInt id/state, short slot, byte button, VarInt mode, changed-slots map with full component stacks, carried full stack.</summary>
    public static PacketCodec<ServerboundContainerClickPacket> ContainerClickV1_21_2 { get; } =
        MakeContainerClickFullStack(Table768);

    /// <summary>769 (1.21.4) container-click: the same frame under the 769 component era table.</summary>
    public static PacketCodec<ServerboundContainerClickPacket> ContainerClickV1_21_4 { get; } =
        MakeContainerClickFullStack(Table769);

    /// <summary>477-754 (1.14-1.16.5) container-click: byte id, short slot, byte button, short action number, VarInt mode (writeEnum of the ClickType ordinal), one varintId clicked stack. The pre-1.17 form: no changed-slots sync map, no state id. 1.14 moved the mode to a VarInt and the clicked stack to the flattened varintId form (1.8-1.13.2 wrote a byte mode and a legacy/short-id/present-id stack, see <see cref="ContainerClickV1_8"/> / <see cref="ContainerClickV1_13"/> / <see cref="ContainerClickV1_13_2"/>).</summary>
    public static PacketCodec<ServerboundContainerClickPacket> ContainerClickV1_14 { get; } =
        PacketCodec<ServerboundContainerClickPacket>.Of(
            static (ref PacketWriter w, ServerboundContainerClickPacket p, PacketCodecContext c) =>
            {
                w.WriteByte((byte)p.ContainerId);
                w.WriteShort(p.Slot);
                w.WriteByte(p.Button);
                w.WriteShort(p.ActionNumber);
                w.WriteVarInt(p.Mode);
                ItemStackCodecs.WritePresentIdStack(ref w, p.LegacyClickedItem ?? ItemStack.Empty, c);
            },
            static (ref PacketReader r, PacketCodecContext c) =>
            {
                int id = r.ReadByte();
                short slot = r.ReadShort();
                byte button = r.ReadByte();
                short action = r.ReadShort();
                int mode = r.ReadVarInt();
                ItemStack item = ItemStackCodecs.ReadPresentIdStack(ref r, c);
                return new ServerboundContainerClickPacket(id, 0, slot, button, mode, action, item, [], null);
            });

    /// <summary>755 (1.17) container-click: byte id, short slot, byte button, VarInt mode, changed-slots map (VarInt count, then short slot + varintId stack per entry), carried varintId stack. The 1.17 container-sync rework added the changed-slots map and carried stack and DROPPED the action number, but the state id only arrived at 1.17.1 (see <see cref="ContainerClickV1_17_1"/>).</summary>
    public static PacketCodec<ServerboundContainerClickPacket> ContainerClickV1_17 { get; } =
        MakeClick(StackWire.PresentId, withStateId: false);

    /// <summary>756-763 (1.17.1-1.20.1) container-click: byte id, VarInt state id, short slot, byte button, VarInt mode, changed-slots map, carried stack, all full present-id stacks with a NAMED NBT root (no hashing on this range). The state id was added at 1.17.1. The field layout survives to 1.20.3, but 1.20.2 flips the stack's NBT root to the unnamed network form, so 764/765 take <see cref="ContainerClickV1_20_2"/>.</summary>
    public static PacketCodec<ServerboundContainerClickPacket> ContainerClickV1_17_1 { get; } =
        MakeClick(StackWire.PresentId);

    /// <summary>764/765 (1.20.2-1.20.3) container-click: identical fields to <see cref="ContainerClickV1_17_1"/> with the unnamed-root (varintId) stacks 1.20.2 introduced.</summary>
    public static PacketCodec<ServerboundContainerClickPacket> ContainerClickV1_20_2 { get; } =
        MakeClick(StackWire.VarIntId);

    /// <summary>766 container-click (full component stacks).</summary>
    public static PacketCodec<ServerboundContainerClickPacket> ContainerClickV1_20_5 { get; } =
        MakeClick(StackWire.Components(Table766));

    /// <summary>767 container-click (full component stacks).</summary>
    public static PacketCodec<ServerboundContainerClickPacket> ContainerClickV1_21 { get; } =
        MakeClick(StackWire.Components(Table767));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareContainerClick(PacketBindings bindings)
    {
        // Serverbound click wire history: 1.8-1.12.2 send a byte mode + legacy (damage-short) stack, 1.13/1.13.1 the short-id stack and 1.13.2 the present-id stack; 1.14 flips the mode to a VarInt and the stack to varintId (V1_14, single clicked item + short action number, no sync map). 1.17 adds the changed-slots sync map and carried stack and drops the action number (V1_17, no state id); 1.17.1
        // adds the state id (V1_17_1, unchanged through 1.20.3). The full stack carries through 1.21.4;
        // hashed stacks arrive at 1.21.5 (770). Applying that form to the 1.14-1.20.1 band mis-encodes every click. Same NAMED-vs-unnamed NBT-root split as container_set_content above: 477-763 named, 764/765 unnamed. 768/769 keep the FULL-stack form; the hashed stack arrives at 1.21.5. Both halves are era tables: protocols 768 and 769 use their own component id orderings.
        PacketTimelineBuilder<ServerboundContainerClickPacket> click =
            bindings.Packet(ItemPackets.Serverbound.ContainerClick)
                .From(JavaProtocols.V1_8, ContainerCodecs.ContainerClickV1_8)
                .From(JavaProtocols.V1_13, ContainerCodecs.ContainerClickV1_13)
                .From(JavaProtocols.V1_13_2, ContainerCodecs.ContainerClickV1_13_2)
                .From(JavaProtocols.V1_14, ContainerCodecs.ContainerClickV1_14)
                .From(JavaProtocols.V1_17, ContainerCodecs.ContainerClickV1_17)
                .From(JavaProtocols.V1_17_1, ContainerCodecs.ContainerClickV1_17_1)
                .From(JavaProtocols.V1_20_2, ContainerCodecs.ContainerClickV1_20_2)
                .From(JavaProtocols.V1_20_5, ContainerCodecs.ContainerClickV1_20_5)
                .From(JavaProtocols.V1_21, ContainerCodecs.ContainerClickV1_21)
                .From(JavaProtocols.V1_21_2, ContainerCodecs.ContainerClickV1_21_2)
                .From(JavaProtocols.V1_21_4, ContainerCodecs.ContainerClickV1_21_4);

        foreach ((int protocol, string era, ItemComponentTable table) in ItemPacketCodecShared.HashedStackComponentEras)
            click.From(protocol, ItemPacketCodecShared.MakeContainerClick(table), $"MakeContainerClick({era})");

    }
}
