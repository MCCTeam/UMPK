namespace Umpk.Game.Inventory;

/// <summary>Which wire form a version uses to ask the recipe book to fill a crafting menu. The wire-form taxonomy is a game fact, not a client-runtime detail: both forms travel under the one <c>minecraft:place_recipe</c> identifier and a version binds exactly one of the two records, so this is usable wherever that fact matters, including a future server role.</summary>
public enum RecipePlacementForm
{
    /// <summary>The version has no sendable place-recipe packet at all. True below 1.13: 1.12-1.12.2 identify a recipe by a numeric crafting-manager id neither modelled record can carry, and the packet does not exist before 1.12.</summary>
    None,

    /// <summary>The 1.13 through 1.21.1 form: the recipe travels as a resource location.</summary>
    ResourceName,

    /// <summary>The 1.21.2+ form: the recipe travels as a numeric network recipe-display id.</summary>
    NetworkId,
}
