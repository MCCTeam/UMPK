using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Client.State;
using Umpk.Game.Items;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Appliers;

/// <summary>Applies inventory/container state: open/close, set-slot, full-content sync, cursor, container properties, and merchant offers. Authoritative set-slot/set-content packets reconcile any optimistic click prediction; a contradiction raises <see cref="PredictionCorrected"/>. Requires the Inventory feature.</summary>
internal sealed class InventoryApplier : IApplier
{
    /// <summary>The container id that addresses the carried (cursor) stack rather than a window.</summary>
    private const int CursorContainerId = -1;

    /// <summary>The container id that addresses the player inventory in INVENTORY index space rather than a window. Used through 1.21.1; replaced by <c>set_player_inventory</c> from 1.21.2.</summary>
    private const int PlayerInventoryContainerId = -2;

    public async ValueTask<bool> TryApplyAsync(object packet, ApplierContext context, CancellationToken ct)
    {
        InventoryState inventory = context.State.Inventory;
        switch (packet)
        {
            case ClientboundOpenScreenPacket open:
                // Resolve the window KIND here, the one point where both eras' keys are in hand: the numeric menu id from 1.14, the window-type string before that. Consumers then read a single semantic type instead of inferring it from slot count and property presence.
                inventory.OpenContainer(
                    open.ContainerId,
                    Math.Max(1, open.LegacySlotCount),
                    open.MenuTypeId,
                    open.Title,
                    open.LegacyType,
                    MenuTypeResolver.Resolve(context.State.Registries, open.MenuTypeId, open.LegacyType));
                await context.PublishAsync(new ContainerOpened(open.ContainerId)).ConfigureAwait(false);
                return true;

            case ClientboundContainerClosePacket close:
                inventory.CloseContainer();
                await context.PublishAsync(new ContainerClosed(close.ContainerId)).ConfigureAwait(false);
                return true;

            case ClientboundContainerSetContentPacket content:
                await ApplyContentAsync(content, inventory, context).ConfigureAwait(false);
                return true;

            case ClientboundContainerSetSlotPacket slot:
                await ApplySlotAsync(slot, inventory, context).ConfigureAwait(false);
                return true;

            case ClientboundSetCursorItemPacket cursor:
                inventory.ApplyAuthoritativeCursor(cursor.Item);
                await context.PublishAsync(new ContainerContentChanged(inventory.OpenWindowId)).ConfigureAwait(false);
                return true;

            case ClientboundSetPlayerInventoryPacket playerSlot:
                // A player-inventory slot update always targets the player array (window 0), regardless of whether a container is open. Its index is in INVENTORY space, not the menu space the player array stores: it uses inventory slot replacement, the same call container id -2 uses. Writing the raw index put every hotbar update on the crafting grid and reversed armor.
                await ApplyInventorySpaceSlotAsync(playerSlot.Slot, playerSlot.Item, inventory, context).ConfigureAwait(false);
                return true;

            case ClientboundMerchantOffersPacket merchant:
                // retain the offers instead of dropping them (ContainerView.Trades).
                inventory.Trades = merchant.Offers;
                await context.PublishAsync(new TradeOffersReceived(merchant.ContainerId)).ConfigureAwait(false);
                return true;

            case ClientboundContainerSetDataPacket data:
                // store the typed property instead of swallowing it (typed property views).
                inventory.SetProperty(data.PropertyId, data.Value);
                await context.PublishAsync(new ContainerPropertyChanged(data.ContainerId, data.PropertyId, data.Value)).ConfigureAwait(false);
                return true;

            // The recipe surface (update-recipes and the recipe-book unlock traffic) belongs to RecipeApplier: RecipeState is always present, so it must not be gated on this feature.
            default:
                return false;
        }
    }

    /// <summary>Applies a player-inventory write whose slot index is in INVENTORY space (container id -2 through 1.21.1, <c>set_player_inventory</c> from 1.21.2) by translating it to the menu-space index the player array stores. An index with no player-window slot (the 1.21.9+ body-armor and saddle equipment indices) is dropped without an event: nothing in the tracked window changed.</summary>
    private static async ValueTask ApplyInventorySpaceSlotAsync(int inventorySlot, ItemStack item, InventoryState inventory, ApplierContext context)
    {
        if (!PlayerInventorySlotMap.TryToMenuSlot(inventorySlot, out int menuSlot))
            return;

        inventory.SetSlot(InventoryState.PlayerWindowId, menuSlot, item);
        await context.PublishAsync(new ContainerContentChanged(InventoryState.PlayerWindowId)).ConfigureAwait(false);
    }

    private static async ValueTask ApplyContentAsync(ClientboundContainerSetContentPacket content, InventoryState inventory, ApplierContext context)
    {
        // Route by window id: a full-content sync overwrites the window it names, never "the active window". A player-inventory sync arriving while a container is open updates only the player array. Contents for a window that is neither open nor the player inventory are dropped.
        int windowId = content.ContainerId;
        if (windowId != inventory.OpenWindowId && windowId != InventoryState.PlayerWindowId)
            return;

        // Reconciliation and mutation are one state-loop operation. A pre-click modern revision rebases under the optimistic delta; a causally newer full snapshot atomically replaces it.
        bool contradiction = inventory.ApplyAuthoritativeContent(
            windowId,
            content.Items,
            content.CarriedItem,
            content.StateId);

        if (contradiction)
            await context.PublishAsync(new PredictionCorrected(windowId, content.StateId)).ConfigureAwait(false);

        await context.PublishAsync(new ContainerContentChanged(windowId)).ConfigureAwait(false);
    }

    private static async ValueTask ApplySlotAsync(ClientboundContainerSetSlotPacket slot, InventoryState inventory, ApplierContext context)
    {
        int windowId = slot.ContainerId;

        // Container id -1 is the carried (cursor) stack. It is keyed on the CONTAINER ID ALONE and ignores the slot index; requiring slot == -1 as well dropped every cursor update a server sent with any other index.
        if (windowId == CursorContainerId)
        {
            inventory.ApplyAuthoritativeCursor(slot.Item);
            await context.PublishAsync(new ContainerContentChanged(inventory.OpenWindowId)).ConfigureAwait(false);
            return;
        }

        // Container id -2 is "write this slot of the player inventory", addressed in INVENTORY space and valid whether or not a window is open. It is how a server reports results that belong to no menu: pick-item and give/pickup overflow. Dropping it was why a server-side give never reached the local snapshot. Superseded by set_player_inventory at 1.21.2, which carries the same index space.
        if (windowId == PlayerInventoryContainerId)
        {
            await ApplyInventorySpaceSlotAsync(slot.Slot, slot.Item, inventory, context).ConfigureAwait(false);
            return;
        }

        // Route by window id: a player-window set-slot targets the player array even while a container is open; a set-slot for an unrelated window is dropped.
        if (windowId != inventory.OpenWindowId && windowId != InventoryState.PlayerWindowId)
            return;

        // A same/older modern revision predates the click and is rebased without consuming its prediction. A newer revision acknowledges it and corrects only this slot when the content differs.
        bool contradiction = inventory.ApplyAuthoritativeSlot(windowId, slot.Slot, slot.Item, slot.StateId);

        if (contradiction)
            await context.PublishAsync(new PredictionCorrected(windowId, slot.StateId)).ConfigureAwait(false);

        await context.PublishAsync(new ContainerContentChanged(windowId)).ConfigureAwait(false);
    }
}
