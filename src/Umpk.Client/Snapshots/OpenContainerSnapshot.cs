using Umpk.Client.State;
using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Text;

namespace Umpk.Client.Snapshots;

/// <summary>An immutable snapshot of the currently open (non-player) container: its slots, typed properties, the cursor stack, merchant trades when the window is a merchant, and its semantic menu type resolved on either wire era.</summary>
/// <param name="WindowId">The open window id.</param>
/// <param name="Slots">The container's slot contents.</param>
/// <param name="Properties">The window's typed properties (furnace progress, enchant levels, ...) keyed by id.</param>
/// <param name="Cursor">The cursor (carried) stack.</param>
/// <param name="StateId">The last authoritative window state id.</param>
/// <param name="Trades">Merchant/villager trade offers attached to the window, carried whole (the adjusted and base costs, uses/max-uses, sold-out and xp are already modelled on <see cref="MerchantOffers"/>), or null when the window is not a merchant.</param>
/// <param name="MenuTypeId">The container's menu-type network id, or -1 when the version predates the menu registry (1.8 through 1.13.2, which name the window with a string instead). Version-specific; prefer <paramref name="MenuType"/>.</param>
/// <param name="Title">The container's rendered title.</param>
/// <param name="MenuType">The container's SEMANTIC type: the resolved <c>minecraft:menu</c> registry key (<c>minecraft:furnace</c>, <c>minecraft:generic_9x3</c>, ...), which means the same thing on every supported version. Null when the window kind could not be named. Resolved from the numeric menu id on 1.14+ and from the window-type string before that, so a consumer can switch on the kind instead of guessing it from the slot count.</param>
/// <param name="LegacyWindowType">The raw pre-1.14 window-type string (<c>"minecraft:chest"</c>, <c>"EntityHorse"</c>, ...), or null on 1.14+. It is the authoritative wire key on that era and survives even when <paramref name="MenuType"/> could not name the window.</param>
public sealed record OpenContainerSnapshot(
    int WindowId,
    IReadOnlyList<ItemStack> Slots,
    IReadOnlyDictionary<int, int> Properties,
    ItemStack Cursor,
    int StateId,
    MerchantOffers? Trades,
    int MenuTypeId,
    Component? Title,
    Identifier? MenuType,
    string? LegacyWindowType)
{
    /// <summary>Projects an <see cref="OpenContainerSnapshot"/> from live tracked state, or null when only the player inventory is open. Pure; no session loop involved.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
    /// <exception cref="FeatureDisabledException">The Inventory feature is disabled for this session.</exception>
    public static OpenContainerSnapshot? Project(ClientState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        InventoryState inventory = state.Inventory;
        if (!inventory.HasOpenContainer || inventory.ContainerSlots is not { } slots)
            return null;

        return new OpenContainerSnapshot(
            inventory.OpenWindowId,
            slots,
            inventory.Properties,
            inventory.Cursor,
            inventory.StateId,
            inventory.Trades,
            inventory.OpenContainerMenuTypeId,
            inventory.OpenContainerTitle,
            inventory.OpenContainerMenuType?.Id,
            inventory.OpenContainerLegacyWindowType);
    }
}
