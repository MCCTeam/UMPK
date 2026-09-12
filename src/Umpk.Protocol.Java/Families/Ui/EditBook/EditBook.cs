using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class UiPackets
{
    public static partial class Serverbound
    {
        /// <summary>Edit book (<c>minecraft:edit_book</c>, 770/776).</summary>
        public static readonly PacketType<ServerboundEditBookPacket> EditBook =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("edit_book"));

        /// <summary>Pre-1.17 edit book (<c>minecraft:edit_book</c>, 393-754), which sends the book as an ITEM STACK rather than a page list. Same identifier as <see cref="EditBook"/> and a different record, the same way serverbound <c>chat</c> carries two records across its signing boundary.</summary>
        public static readonly PacketType<ServerboundLegacyEditBookPacket> LegacyEditBook =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("edit_book"));
    }
}

/// <summary>Edit book (770/776): the hotbar slot of the book, up to 100 page strings (each up to 1024 chars), and an optional title (up to 32 chars) sent when signing the book.</summary>
public sealed record ServerboundEditBookPacket(int Slot, IReadOnlyList<string> Pages, string? Title) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Serverbound.EditBook;
}

/// <summary>Pre-1.17 edit book (393-754): the whole written book as an ITEM STACK, a signing flag, and a trailing VarInt. <paramref name="HandOrSlot"/> is the interaction hand on 401-753, the inventory slot on 754, and null on 393, which sends no trailing value at all.</summary>
public sealed record ServerboundLegacyEditBookPacket(ItemStack Book, bool Signing, int? HandOrSlot) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Serverbound.LegacyEditBook;
}
