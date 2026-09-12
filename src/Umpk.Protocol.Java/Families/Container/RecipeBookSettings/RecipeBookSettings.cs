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
        /// <summary>Recipe-book settings (<c>minecraft:recipe_book_settings</c>).</summary>
        public static readonly PacketType<ClientboundRecipeBookSettingsPacket> RecipeBookSettings =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("recipe_book_settings"));
    }
}

/// <summary>Recipe-book settings: an open/filtering pair for each of the four books.</summary>
/// <param name="Books">The four (open, filtering) pairs (crafting, furnace, blast furnace, smoker).</param>
public sealed record ClientboundRecipeBookSettingsPacket(IReadOnlyList<RecipeBookSetting> Books) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Clientbound.RecipeBookSettings;
}
