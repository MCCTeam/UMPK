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
        /// <summary>Place a ghost recipe in a container (<c>minecraft:place_ghost_recipe</c>).</summary>
        public static readonly PacketType<ClientboundPlaceGhostRecipePacket> PlaceGhostRecipe =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("place_ghost_recipe"));
    }
}

/// <summary>Place a ghost recipe in a container; the recipe display payload is retained opaquely.</summary>
/// <param name="ContainerId">The window id.</param>
/// <param name="RecipeDisplay">The raw recipe-display payload.</param>
public sealed record ClientboundPlaceGhostRecipePacket(int ContainerId, byte[] RecipeDisplay) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Clientbound.PlaceGhostRecipe;
}
