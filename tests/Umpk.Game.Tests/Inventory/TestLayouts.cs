using Umpk.Game.Inventory;

namespace Umpk.Game.Tests.Inventory;

/// <summary>Builds <see cref="SlotLayout"/> instances for the click-simulator scenarios, arranged exactly like the vanilla menus they model so the simulator's shift-click and swap resolution can be exercised.</summary>
internal static class TestLayouts
{
    /// <summary>A generic 9x3 chest: slots 0..26 storage, 27..53 player main, 54..62 player hotbar (63 slots). Shift-click moves storage to player (main then hotbar), and player to storage.</summary>
    public static SlotLayout Chest9x3()
    {
        const int storageEnd = 27;   // [0,27)
        const int playerEnd = 63;    // [27,63)
        const int hotbarStart = 54;  // [54,63)

        var roles = new SlotRole[playerEnd];
        var targets = new IReadOnlyList<SlotRange>[playerEnd];

        for (int i = 0; i < playerEnd; i++)
        {
            if (i < storageEnd)
            {
                roles[i] = SlotRole.Storage;
                // Storage -> player inventory [27,63).
                targets[i] = [new SlotRange(storageEnd, playerEnd)];
            }
            else
            {
                roles[i] = i >= hotbarStart ? SlotRole.PlayerHotbar : SlotRole.PlayerMain;
                // Player -> storage [0,27).
                targets[i] = [new SlotRange(0, storageEnd)];
            }
        }

        return new SlotLayout(roles, targets);
    }

    /// <summary>A 2x2 crafting menu (the survival inventory craft grid modeled standalone): slot 0 result (output), 1..4 craft input, 5..31 player main, 32..40 player hotbar. Shift-click from the result pushes into the player inventory.</summary>
    public static SlotLayout CraftingResult()
    {
        const int total = 41;
        var roles = new SlotRole[total];
        var targets = new IReadOnlyList<SlotRange>[total];

        roles[0] = SlotRole.Output;
        targets[0] = [new SlotRange(5, total, Reverse: true)]; // result -> player, filled backwards

        for (int i = 1; i <= 4; i++)
        {
            roles[i] = SlotRole.CraftingInput;
            targets[i] = [new SlotRange(5, total)];
        }

        for (int i = 5; i < total; i++)
        {
            roles[i] = i >= 32 ? SlotRole.PlayerHotbar : SlotRole.PlayerMain;
            targets[i] = [new SlotRange(1, 5)]; // player -> craft grid
        }

        return new SlotLayout(roles, targets);
    }

    /// <summary>The player inventory window: 0 result, 1..4 craft, 5..8 armor, 9..35 main, 36..44 hotbar, 45 offhand. Used for swap-to-hotbar and offhand-swap (button 40) scenarios.</summary>
    public static SlotLayout PlayerInventory()
    {
        const int total = 46;
        var roles = new SlotRole[total];
        var targets = new IReadOnlyList<SlotRange>[total];

        roles[0] = SlotRole.Output;
        for (int i = 1; i <= 4; i++)
            roles[i] = SlotRole.CraftingInput;

        for (int i = 5; i <= 8; i++)
            roles[i] = SlotRole.Armor;

        for (int i = 9; i <= 35; i++)
            roles[i] = SlotRole.PlayerMain;

        for (int i = 36; i <= 44; i++)
            roles[i] = SlotRole.PlayerHotbar;

        roles[45] = SlotRole.Offhand;

        for (int i = 0; i < total; i++)
            targets[i] ??= [];

        return new SlotLayout(roles, targets);
    }

    /// <summary>A 9x3 chest shape whose slot 0 carries a per-slot stack limit of 1, modeling a beacon-payment-style capped slot. Exercises the per-slot limit plumbing.</summary>
    public static SlotLayout CappedFirstSlot()
    {
        const int storageEnd = 27;
        const int playerEnd = 63;
        const int hotbarStart = 54;

        var roles = new SlotRole[playerEnd];
        var targets = new IReadOnlyList<SlotRange>[playerEnd];
        var limits = new int[playerEnd];

        for (int i = 0; i < playerEnd; i++)
        {
            limits[i] = SlotLayout.DefaultSlotStackLimit;
            if (i < storageEnd)
            {
                roles[i] = SlotRole.Storage;
                targets[i] = [new SlotRange(storageEnd, playerEnd)];
            }
            else
            {
                roles[i] = i >= hotbarStart ? SlotRole.PlayerHotbar : SlotRole.PlayerMain;
                targets[i] = [new SlotRange(0, storageEnd)];
            }
        }

        limits[0] = 1;
        return new SlotLayout(roles, targets, limits);
    }
}
