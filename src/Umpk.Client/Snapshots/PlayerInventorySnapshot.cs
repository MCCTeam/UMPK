using Umpk.Client.State;
using Umpk.Game.Items;

namespace Umpk.Client.Snapshots;

/// <summary>An immutable snapshot of the player inventory (window 0): all 46 slots, the held hotbar slot, the cursor stack, and the authoritative window state id the click pipeline uses for prediction and reconciliation. Item stacks are carried directly rather than flattened into a display DTO.</summary>
/// <param name="Slots">The 46 player-inventory slots (crafting/armor/main/hotbar/offhand), in wire slot order.</param>
/// <param name="HeldSlot">The selected hotbar slot, from 0 through 8.</param>
/// <param name="Cursor">The cursor (carried) stack.</param>
/// <param name="StateId">The last authoritative window state id.</param>
public sealed record PlayerInventorySnapshot(
    IReadOnlyList<ItemStack> Slots, int HeldSlot, ItemStack Cursor, int StateId)
{
    /// <summary>The stack in the selected hotbar slot, or <see cref="ItemStack.Empty"/> when the resolved index falls outside <see cref="Slots"/> (for example a legacy player window with no hotbar space at all).</summary>
    public ItemStack HeldItem => ResolveHeldItem(Slots, HeldSlot);

    /// <summary>The player-window hotbar arithmetic (slots 36-44 are the hotbar, so the selected slot is 36 + <paramref name="heldSlot"/>), kept as the one place it lives in this package: both <see cref="HeldItem"/> and <see cref="Actions.InteractionActions"/>'s held-item lookup for the legacy block-place packet call through here instead of each carrying their own copy.</summary>
    internal static ItemStack ResolveHeldItem(IReadOnlyList<ItemStack> slots, int heldSlot)
    {
        int index = 36 + heldSlot;
        return index >= 0 && index < slots.Count ? slots[index] : ItemStack.Empty;
    }

    /// <summary>Projects a <see cref="PlayerInventorySnapshot"/> from live tracked state. Pure; no session loop involved.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
    /// <exception cref="FeatureDisabledException">The Inventory feature is disabled for this session.</exception>
    public static PlayerInventorySnapshot Project(ClientState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        InventoryState inventory = state.Inventory;
        return new PlayerInventorySnapshot(inventory.PlayerSlots, state.Self.HeldSlot, inventory.Cursor, inventory.StateId);
    }
}
