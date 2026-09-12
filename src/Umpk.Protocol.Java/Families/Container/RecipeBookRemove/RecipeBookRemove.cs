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
        /// <summary>Remove recipe-book entries (<c>minecraft:recipe_book_remove</c>).</summary>
        public static readonly PacketType<ClientboundRecipeBookRemovePacket> RecipeBookRemove =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("recipe_book_remove"));
    }
}

/// <summary>Remove recipe-book entries by their display ids.</summary>
/// <param name="RecipeIds">The recipe display ids to remove.</param>
public sealed record ClientboundRecipeBookRemovePacket(IReadOnlyList<int> RecipeIds) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Clientbound.RecipeBookRemove;
}
