namespace Umpk.Game.Inventory;

/// <summary>The semantic role of a container slot. Roles let the click simulator and consumers reason about slots without per-container-type switches. A slot is one role; the <see cref="Output"/> role marks server-managed result slots (crafting result, anvil output, ...) that a client must not predict writes into.</summary>
public enum SlotRole
{
    /// <summary>A generic storage slot inside the container's own inventory.</summary>
    Storage,

    /// <summary>A server-managed output/result slot; the client predicts takes but never places.</summary>
    Output,

    /// <summary>A crafting-grid input slot.</summary>
    CraftingInput,

    /// <summary>A furnace/brewing fuel slot.</summary>
    Fuel,

    /// <summary>A furnace/blast/smoker ingredient (cook input) slot.</summary>
    Ingredient,

    /// <summary>An armor slot in the player inventory.</summary>
    Armor,

    /// <summary>The offhand slot.</summary>
    Offhand,

    /// <summary>A player main-inventory (backpack) slot mirrored into the window.</summary>
    PlayerMain,

    /// <summary>A player hotbar slot mirrored into the window.</summary>
    PlayerHotbar,

    /// <summary>A special-purpose input slot with no more specific role (loom, anvil, enchant, ...).</summary>
    Special,
}
