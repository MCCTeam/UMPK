using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

/// <summary>Which shape of recipe a display entry describes.</summary>
public enum RecipeDisplayKind
{
    /// <summary>A shapeless crafting recipe.</summary>
    CraftingShapeless,

    /// <summary>A shaped crafting recipe.</summary>
    CraftingShaped,

    /// <summary>A smelting, blasting, smoking or campfire recipe.</summary>
    Furnace,

    /// <summary>A stonecutter recipe.</summary>
    Stonecutter,

    /// <summary>A smithing-table recipe.</summary>
    Smithing,
}

/// <summary>One decoded recipe-book entry from 1.21.2: a recipe display entry followed by a flags byte.</summary>
/// <remarks><see cref="Display"/> and <see cref="CraftingRequirements"/> are the FULL wire content and are the only members the encoder reads. <see cref="Kind"/> and the three <c>Result*</c> members are decode-time summaries derived from <see cref="Display"/> (the result slot is what names a recipe, and resolving its identifier needs the connection's item registry, so it cannot be a derived property here). They exist for callers that only want to list a book; a hand-built entry whose summary disagrees with its display still encodes from the display, so the wire stays correct.</remarks>
/// <param name="DisplayId">The recipe display id. This is the handle the serverbound place-recipe packet takes, so it is what a client needs in order to ACT on the recipe, not merely to list it.</param>
/// <param name="Kind">Which shape of recipe this is.</param>
/// <param name="ResultItemId">The registry id of the item the recipe produces, or -1 when the result is not a single item (an empty slot, or a tag standing for many). A recipe is named after its result, so this is what makes a listing readable.</param>
/// <param name="ResultId">The result's identifier when the wire carried one. A bare <c>item</c> slot display sends only the numeric id, so this is default there and the caller resolves the name through the item registry.</param>
/// <param name="ResultCount">How many the recipe produces.</param>
/// <param name="Group">The recipe-book group id, when the server sent one.</param>
/// <param name="Category">The recipe-book category id.</param>
/// <param name="Notification">The server wants this unlock announced to the player.</param>
/// <param name="Highlight">The server wants this entry highlighted in the book.</param>
/// <param name="Display">The whole recipe display tree, exactly as the wire carried it.</param>
/// <param name="CraftingRequirements">The optional crafting-requirements list: each ingredient sets the "can I craft this" highlight needs. Null when the optional was absent, which is distinct from an empty list and encodes differently.</param>
public sealed record RecipeBookEntry(
    int DisplayId,
    RecipeDisplayKind Kind,
    int ResultItemId,
    Identifier ResultId,
    int ResultCount,
    int? Group,
    int Category,
    bool Notification,
    bool Highlight,
    RecipeDisplay Display,
    IReadOnlyList<RecipeIngredient>? CraftingRequirements);

/// <summary>One recipe-book's open/filtering pair.</summary>
/// <param name="Open">Whether the book is open.</param>
/// <param name="Filtering">Whether crafting-only filtering is on.</param>
public sealed record RecipeBookSetting(bool Open, bool Filtering);

/// <summary>One predicted changed slot a client sends with a modern container click. The stack is the raw predicted contents; the version-bound click codec hashes it (per-component CRC32C over the HashOps structural encoding) at encode time, so the client layer stays version-blind.</summary>
/// <param name="Slot">The slot index.</param>
/// <param name="Stack">The raw stack the client predicts is in the slot after the click.</param>
public sealed record PredictedSlot(short Slot, ItemStack Stack);
