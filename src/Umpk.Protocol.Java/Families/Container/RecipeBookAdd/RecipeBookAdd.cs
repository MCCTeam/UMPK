using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class ItemPackets
{
    public static partial class Clientbound
    {
        /// <summary>Add recipe-book entries (<c>minecraft:recipe_book_add</c>).</summary>
        public static readonly PacketType<ClientboundRecipeBookAddPacket> RecipeBookAdd =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("recipe_book_add"));
    }
}

/// <summary>Add entries to the recipe book (1.21.2+).</summary>
/// <param name="Entries">The decoded entries.</param>
/// <param name="Replace">Whether these entries replace the existing set rather than adding to it.</param>
/// <param name="UndecodedEntries">Non-zero when the display tree could not be read and the entries are incomplete. A caller must not present <see cref="Entries"/> as the whole book while this is set: the book is larger than what was decoded, and saying otherwise is worse than saying nothing.</param>
public sealed record ClientboundRecipeBookAddPacket(
    IReadOnlyList<RecipeBookEntry> Entries,
    bool Replace,
    int UndecodedEntries = 0) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Clientbound.RecipeBookAdd;
}
