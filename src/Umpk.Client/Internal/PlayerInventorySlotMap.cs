namespace Umpk.Client.Internal;

/// <summary>Translates a player-inventory index into the 46-slot <c>InventoryMenu</c> index that <see cref="Umpk.Client.State.InventoryState.PlayerSlots"/> stores.</summary>
/// <remarks>
/// Minecraft addresses the player inventory through TWO different index spaces and they are not the same numbers. Both appear on the wire:
/// <list type="bullet">
/// <item>
/// The MENU space is what window id 0 uses. <c>InventoryMenu</c> adds its slots in a fixed order: 0 is the crafting result, 1-4 the 2x2 crafting grid, 5-8 armor (head first), 9-35 the main backpack, 36-44 the hotbar, 45 the offhand.
/// </item>
/// <item>
/// The INVENTORY space is what container id -2 and the 1.21.2+ <c>set_player_inventory</c> packet use. It is <c>inventory slot replacement</c>'s own indexing: 0-8 the hotbar, 9-35 the main backpack, 36-39 armor (FEET first, per <c>equipment-slot ordering</c>), 40 the offhand, 41 body armor and 42 saddle. Those last two are mount/body equipment with NO slot in the player window at all.
/// </item>
/// </list>
/// The two spaces agree only on the backpack, which is exactly why feeding one to the other looks correct in a test that only fills the backpack and silently misplaces every hotbar and armor write in practice: an inventory-space hotbar write to index 0 lands on the crafting RESULT slot. The armor blocks additionally run in OPPOSITE orders, so armor is not merely offset but reversed. The mapping is stable from 1.8 through 26.2; only the 41/42 tail is newer, and it maps to nothing.
/// </remarks>
internal static class PlayerInventorySlotMap
{
    /// <summary>The number of slots in the player window (<c>InventoryMenu</c>).</summary>
    internal const int MenuSlotCount = 46;

    /// <summary>The menu index of the offhand slot.</summary>
    private const int MenuOffhandSlot = 45;

    /// <summary>The inventory index of the offhand slot.</summary>
    private const int InventoryOffhandSlot = 40;

    /// <summary>The first inventory index of the armor block; feet are first.</summary>
    private const int InventoryArmorStart = 36;

    /// <summary>The first inventory index of the hotbar.</summary>
    private const int InventoryHotbarStart = 0;

    /// <summary>The first menu index of the hotbar.</summary>
    private const int MenuHotbarStart = 36;

    /// <summary>The number of hotbar slots.</summary>
    private const int HotbarSize = 9;

    /// <summary>The first inventory (and menu) index of the shared backpack block.</summary>
    private const int BackpackStart = 9;

    /// <summary>One past the last inventory (and menu) index of the shared backpack block.</summary>
    private const int BackpackEnd = 36;

    /// <summary>Maps an <c>Inventory</c>-space index to its <c>InventoryMenu</c>-space index. Returns false for an index with no slot in the player window (out of range, or the 1.21.9+ body-armor/saddle equipment indices), in which case the caller must drop the write rather than guess a slot.</summary>
    internal static bool TryToMenuSlot(int inventorySlot, out int menuSlot)
    {
        // Hotbar: inventory 0-8 sits at the END of the menu window, at 36-44.
        if (inventorySlot >= InventoryHotbarStart && inventorySlot < InventoryHotbarStart + HotbarSize)
        {
            menuSlot = MenuHotbarStart + (inventorySlot - InventoryHotbarStart);
            return true;
        }

        // Backpack: the one block where the two spaces agree.
        if (inventorySlot >= BackpackStart && inventorySlot < BackpackEnd)
        {
            menuSlot = inventorySlot;
            return true;
        }

        // Armor: inventory runs FEET -> HEAD, the menu runs HEAD -> FEET, so the block is reversed, not shifted: menu = 44 - inventory.
        if (inventorySlot >= InventoryArmorStart && inventorySlot < InventoryOffhandSlot)
        {
            menuSlot = (MenuHotbarStart + HotbarSize - 1) - inventorySlot;
            return true;
        }

        if (inventorySlot == InventoryOffhandSlot)
        {
            menuSlot = MenuOffhandSlot;
            return true;
        }

        // 41 (body armor) and 42 (saddle) are equipment with no player-window slot, as is anything negative or past the table.
        menuSlot = -1;
        return false;
    }
}
