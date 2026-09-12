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
        /// <summary>Mark a recipe seen (<c>minecraft:recipe_book_seen_recipe</c>, 1.21.2+ display-id form).</summary>
        public static readonly PacketType<ServerboundRecipeBookSeenRecipePacket> RecipeBookSeenRecipe =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("recipe_book_seen_recipe"));

        /// <summary>Mark a recipe seen by resource location (<c>minecraft:recipe_book_seen_recipe</c>, pre-1.21.2 form).</summary>
        public static readonly PacketType<ServerboundRecipeBookSeenRecipeByNamePacket> RecipeBookSeenRecipeByName =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("recipe_book_seen_recipe"));
    }
}

/// <summary>Mark a recipe as seen (1.21.2+ network display-id form).</summary>
/// <param name="RecipeId">The recipe display id.</param>
public sealed record ServerboundRecipeBookSeenRecipePacket(int RecipeId) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Serverbound.RecipeBookSeenRecipe;
}

/// <summary>Mark a recipe as seen using the pre-1.21.2 wire form, where the recipe is a resource-location identifier rather than a network display id. Shares the <c>minecraft:recipe_book_seen_recipe</c> timeline; the era timeline selects this record for 1.16.2-1.21.1 and the display-id record for 1.21.2+.</summary>
/// <param name="Recipe">The recipe resource-location identifier.</param>
public sealed record ServerboundRecipeBookSeenRecipeByNamePacket(Identifier Recipe) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Serverbound.RecipeBookSeenRecipeByName;
}
