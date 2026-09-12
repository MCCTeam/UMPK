using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class ItemPackets
{
    public static partial class Serverbound
    {
        /// <summary>Change recipe-book settings (<c>minecraft:recipe_book_change_settings</c>).</summary>
        public static readonly PacketType<ServerboundRecipeBookChangeSettingsPacket> RecipeBookChangeSettings =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("recipe_book_change_settings"));
    }
}

/// <summary>Change one recipe-book's settings.</summary>
/// <param name="BookType">The book ordinal (crafting, furnace, blast furnace, smoker).</param>
/// <param name="Open">Whether the book is open.</param>
/// <param name="Filtering">Whether filtering is on.</param>
public sealed record ServerboundRecipeBookChangeSettingsPacket(int BookType, bool Open, bool Filtering) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Serverbound.RecipeBookChangeSettings;
}
