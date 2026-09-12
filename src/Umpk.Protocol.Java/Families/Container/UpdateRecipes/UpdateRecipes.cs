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
        /// <summary>Update the recipe definitions (<c>minecraft:update_recipes</c>).</summary>
        public static readonly PacketType<ClientboundUpdateRecipesPacket> UpdateRecipes =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("update_recipes"));
    }
}

/// <summary>Update the recipe definitions; the recipe payload is retained opaquely.</summary>
/// <param name="Payload">The raw update-recipes payload.</param>
public sealed record ClientboundUpdateRecipesPacket(byte[] Payload) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Clientbound.UpdateRecipes;
}
