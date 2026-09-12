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
        /// <summary>Recipe-book unlock init/add/remove (<c>minecraft:recipe</c>, 1.12-1.21.1).</summary>
        public static readonly PacketType<ClientboundRecipePacket> Recipe =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("recipe"));
    }
}

/// <summary>The pre-1.21.2 recipe-book unlock packet (<c>minecraft:recipe</c>). Carries a book state (init / add / remove), the recipe-book open/filtering pairs for the books that era knows about, the affected recipes, and, on <see cref="RecipeBookState.Init"/> only, a second "newly unlocked, highlight these" list. The recipe identity is era-specific: 1.12-1.12.2 sends numeric crafting-manager ids (<see cref="LegacyRecipeIds"/> / <see cref="LegacyToHighlightIds"/>) and 1.13+ sends resource locations (<see cref="Recipes"/> / <see cref="ToHighlight"/>). Each era codec reads and writes only the fields its wire form carries, so the unused pair stays empty.</summary>
/// <param name="State">The book state: 0 init, 1 add, 2 remove (see <see cref="RecipeBookState"/>).</param>
/// <param name="Books">The per-book (open, filtering) pairs in <c>RecipeBookType</c> order (crafting, furnace, blast furnace, smoker). One pair on 1.12-1.12.2, two on 1.13-1.16.1, four on 1.16.2+.</param>
/// <param name="Recipes">The affected recipe identifiers (1.13+); empty on 1.12-1.12.2.</param>
/// <param name="ToHighlight">The identifiers to highlight (1.13+, init only); empty otherwise.</param>
/// <param name="LegacyRecipeIds">The affected numeric recipe ids (1.12-1.12.2); empty on 1.13+.</param>
/// <param name="LegacyToHighlightIds">The numeric ids to highlight (1.12-1.12.2, init only); empty otherwise.</param>
public sealed record ClientboundRecipePacket(
    int State,
    IReadOnlyList<RecipeBookSetting> Books,
    IReadOnlyList<Identifier> Recipes,
    IReadOnlyList<Identifier> ToHighlight,
    IReadOnlyList<int> LegacyRecipeIds,
    IReadOnlyList<int> LegacyToHighlightIds) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Clientbound.Recipe;
}

/// <summary>The <c>minecraft:recipe</c> book states in wire-ordinal order. The ordinal is encoded as a VarInt.</summary>
public static class RecipeBookState
{
    /// <summary>Replace the unlocked set with the packet's recipes (login / respawn sync).</summary>
    public const int Init = 0;

    /// <summary>Add the packet's recipes to the unlocked set.</summary>
    public const int Add = 1;

    /// <summary>Remove the packet's recipes from the unlocked set.</summary>
    public const int Remove = 2;
}
