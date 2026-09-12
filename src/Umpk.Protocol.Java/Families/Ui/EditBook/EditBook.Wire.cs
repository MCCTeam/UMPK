using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class UiMiscCodecs
{
    /// <summary>Edit book (770/776): slot, page strings, and an optional title.</summary>
    public static readonly PacketCodec<ServerboundEditBookPacket> EditBookV1_17 =
        PacketCodec<ServerboundEditBookPacket>.Of(
            static (ref PacketWriter w, ServerboundEditBookPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.Slot);
                w.WriteList(p.Pages, static (ref PacketWriter sw, string s) => sw.WriteString(s, 1024));
                w.WriteOptional(p.Title, static (ref PacketWriter sw, string s) => sw.WriteString(s, 32));
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int slot = r.ReadVarInt();
                string[] pages = r.ReadList(static (ref PacketReader sr) => sr.ReadString(1024));
                string? title = r.ReadOptional(static (ref PacketReader sr) => sr.ReadString(32));
                return new ServerboundEditBookPacket(slot, pages, title);
            });

    /// <summary>Edit book, 393 (1.13): the written book as an ITEM STACK plus the signing bool, and nothing else. The trailing field the next release adds is genuinely absent here.</summary>
    /// <remarks>The 1.13/1.13.1 stack form uses a short id; the present-id form arrives at 1.13.2.</remarks>
    public static readonly PacketCodec<ServerboundLegacyEditBookPacket> EditBookV1_13 =
        MakeLegacyEditBook(ItemPacketCodecShared.StackWire.ShortId, hasTrailingVarInt: false);

    /// <summary>Edit book, 401 (1.13.1): the short-id book stack, the signing bool, and the interaction hand as a VarInt enum ordinal.</summary>
    public static readonly PacketCodec<ServerboundLegacyEditBookPacket> EditBookV1_13_1 =
        MakeLegacyEditBook(ItemPacketCodecShared.StackWire.ShortId, hasTrailingVarInt: true);

    /// <summary>Edit book, 404-754 (1.13.2-1.16.5): the PRESENT-id book stack, the signing bool, and a trailing VarInt. That VarInt is the interaction hand through 1.16.3 and the inventory slot from 1.16.4, but the two are the same VarInt on the wire, so one codec covers the whole band.</summary>
    /// <remarks>The trailing VarInt changes meaning from interaction hand through 1.16.3 to inventory slot at 1.16.4; the wire shape stays the same. Version 1.17 replaces the packet with the slot, page-list, and optional-title form.</remarks>
    public static readonly PacketCodec<ServerboundLegacyEditBookPacket> EditBookV1_13_2 =
        MakeLegacyEditBook(ItemPacketCodecShared.StackWire.PresentId, hasTrailingVarInt: true);

    private static PacketCodec<ServerboundLegacyEditBookPacket> MakeLegacyEditBook(
        ItemPacketCodecShared.StackWire stacks, bool hasTrailingVarInt) =>
        PacketCodec<ServerboundLegacyEditBookPacket>.Of(
            (ref PacketWriter w, ServerboundLegacyEditBookPacket p, PacketCodecContext c) =>
            {
                stacks.Write(ref w, p.Book, c);
                w.WriteBool(p.Signing);
                if (hasTrailingVarInt)
                    w.WriteVarInt(p.HandOrSlot ?? throw new ProtocolViolationException("A 1.13.1-1.16.5 edit_book requires the hand or slot value."));

            },
            (ref PacketReader r, PacketCodecContext c) =>
            {
                ItemStack book = stacks.Read(ref r, c);
                bool signing = r.ReadBool();
                return new ServerboundLegacyEditBookPacket(book, signing, hasTrailingVarInt ? r.ReadVarInt() : null);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareEditBook(PacketBindings bindings)
    {
        // edit_book has four wire generations. 1.13 sends an item stack and a signing bool. 1.13.1 appends the interaction hand as a VarInt enum, still on the SHORT-id stack. 1.13.2 moves to the present-id stack and keeps stack + bool + hand through 1.16.3; 1.16.4 reinterprets that trailing VarInt as an inventory slot without changing the wire. Only from 1.17 is it the slot + page-list + optional-title form (1.17.1 / 1.20.4 / 1.21.5).
        bindings.Packet(UiPackets.Serverbound.EditBook)
            .FromAs(JavaProtocols.V1_13, UiPackets.Serverbound.LegacyEditBook, UiMiscCodecs.EditBookV1_13)
            .FromAs(JavaProtocols.V1_13_1, UiPackets.Serverbound.LegacyEditBook, UiMiscCodecs.EditBookV1_13_1)
            .FromAs(JavaProtocols.V1_13_2, UiPackets.Serverbound.LegacyEditBook, UiMiscCodecs.EditBookV1_13_2)
            .From(JavaProtocols.V1_17, UiMiscCodecs.EditBookV1_17);
    }
}
