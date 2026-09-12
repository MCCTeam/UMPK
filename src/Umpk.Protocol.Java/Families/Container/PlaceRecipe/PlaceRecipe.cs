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
        /// <summary>Place a recipe into a container (<c>minecraft:place_recipe</c>, 1.21.2+ network-id form).</summary>
        public static readonly PacketType<ServerboundPlaceRecipePacket> PlaceRecipe =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("place_recipe"));

        /// <summary>Place a recipe by resource-location identifier (<c>minecraft:place_recipe</c>, pre-1.21.2 form).</summary>
        public static readonly PacketType<ServerboundPlaceRecipeByNamePacket> PlaceRecipeByName =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("place_recipe"));
    }
}

/// <summary>Place a recipe into a container (1.21.2+: the recipe is a network recipe-display id).</summary>
/// <param name="ContainerId">The window id.</param>
/// <param name="RecipeId">The recipe display id.</param>
/// <param name="UseMaxItems">Whether to place the maximum stack.</param>
public sealed record ServerboundPlaceRecipePacket(int ContainerId, int RecipeId, bool UseMaxItems) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Serverbound.PlaceRecipe;
}

/// <summary>Place a recipe into a container using the pre-1.21.2 wire form, where the recipe is carried as a resource-location identifier (byte window id + identifier + bool) rather than a network display id. Shares the <c>minecraft:place_recipe</c> timeline; the era timeline selects this record for 1.14-1.20.1 and the network-id record for 1.21.2+.</summary>
/// <param name="ContainerId">The window id.</param>
/// <param name="Recipe">The recipe resource-location identifier.</param>
/// <param name="UseMaxItems">Whether to place the maximum stack.</param>
public sealed record ServerboundPlaceRecipeByNamePacket(int ContainerId, Identifier Recipe, bool UseMaxItems) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Serverbound.PlaceRecipeByName;
}
