using Microsoft.Extensions.Logging;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Client.State;
using Umpk.Data.Java;
using Umpk.Game.Inventory;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Actions;

/// <summary>Inventory and container actions. Clicks run through the local click simulator for prediction, then send a container-click packet carrying the predicted changed slots and cursor; the version-bound click codec hashes those stacks per component at encode time, so this layer stays version-blind. The server's set-slot/set-content packets reconcile the prediction. Modern pre-click revisions are rebased without erasing the optimistic delta; a causally newer contradiction raises <see cref="PredictionCorrected"/> through the inventory applier.</summary>
public sealed class InventoryActions
{
    /// <summary>The literal channel the pre-1.13 anvil rename travels on. Not an <see cref="Identifier"/>: it predates the namespaced-channel convention and the <c>|</c> and upper-case letters are both outside an identifier's grammar. Vanilla reads the channel with a 20-character cap which this 11-character name is inside.</summary>
    private const string LegacyItemNameChannel = "MC|ItemName";

    private static readonly Identifier CustomPayloadId = Identifier.Minecraft("custom_payload");

    private readonly IPacketSink _sink;
    private readonly ClientSessionServices _services;
    private short _actionNumber;

    internal InventoryActions(IPacketSink sink, ClientSessionServices services)
    {
        _sink = sink;
        _services = services;
    }

    private InventoryState Inventory => _services.State.Inventory;

    /// <summary>Selects the active hotbar slot, from 0 to 8, and optimistically updates self held-slot.</summary>
    public async Task SelectHeldSlotAsync(int slot, CancellationToken ct = default)
    {
        if (slot is < 0 or > 8)
            throw new ArgumentOutOfRangeException(nameof(slot), "The hotbar slot must be 0-8.");

        await _sink.SendAsync(new ServerboundSetCarriedItemPacket((short)slot), ct).ConfigureAwait(false);
        _services.State.Self.HeldSlot = slot;
    }

    /// <summary>Performs a container click through the prediction pipeline.</summary>
    public async Task<ClickResult> ClickAsync(ClickAction action, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!_services.State.Features.Inventory)
            throw new FeatureDisabledException("Inventory");

        ContainerSnapshot before = Inventory.CaptureActiveSnapshot();
        SlotLayout layout = GenericLayouts.For(Inventory.ActiveSlotCount, Inventory.HasOpenContainer);
        ClickResult result = ClickSimulator.Apply(before, action, layout);

        int windowId = Inventory.OpenWindowId;
        int sentStateId = Inventory.StateId;

        // Optimistically apply the prediction and record it for causal reconciliation. Rapid clicks at the same server revision compose into one bounded pending snapshot.
        Inventory.ApplyPredictedSnapshot(result.State);
        Inventory.RecordPrediction(
            windowId,
            sentStateId,
            before,
            usesStateIds: _services.Version.Version.Protocol >= JavaVersions.V1_17_1.Version.Protocol);

        (short slot, byte button, int mode) = Encode(action);

        // Send the raw predicted changed slots and cursor; the version-bound codec hashes them at encode time. This layer never touches component tables (version-blindness).
        var changed = new PredictedSlot[result.ChangedSlots.Count];
        for (int i = 0; i < changed.Length; i++)
        {
            SlotChange change = result.ChangedSlots[i];
            changed[i] = new PredictedSlot((short)change.Slot, change.Item);
        }

        // The pre-1.17 click wire carries the CLICKED stack, which the server compares against its own click result to accept or reject the transaction (a mismatch is rejected and resynced). Vanilla reports the original stack of the clicked slot, which the pre-click snapshot still holds. The 1.17+ codecs ignore this field (they carry the changed-slots map instead).
        Umpk.Game.Items.ItemStack legacyClicked = slot >= 0 && slot < before.SlotCount
            ? before.GetSlot(slot)
            : Umpk.Game.Items.ItemStack.Empty;

        await _sink.SendAsync(
            new ServerboundContainerClickPacket(
                windowId,
                sentStateId,
                slot,
                button,
                mode,
                _actionNumber++,
                LegacyClickedItem: legacyClicked,
                ChangedSlots: changed,
                CarriedItem: result.Cursor),
            ct).ConfigureAwait(false);

        return result;
    }

    /// <summary>Shift-clicks (quick-move) a slot.</summary>
    public Task<ClickResult> QuickMoveAsync(int slot, CancellationToken ct = default)
        => ClickAsync(new ClickAction.QuickMove(slot, MouseButton.Left), ct);

    /// <summary>Closes the active container.</summary>
    public async Task CloseAsync(CancellationToken ct = default)
    {
        int windowId = Inventory.OpenWindowId;
        await _sink.SendAsync(new ServerboundContainerClosePacket(windowId), ct).ConfigureAwait(false);
        Inventory.CloseContainer();
    }

    /// <summary>Sets a creative-mode slot directly and applies it to the local player-inventory snapshot.</summary>
    /// <param name="slot">The slot in PLAYER-WINDOW (menu) index space, the same space <see cref="InventoryState.PlayerSlots"/> uses: 1-4 the crafting grid, 5-8 armor (head first), 9-35 the backpack, 36-44 the hotbar, 45 the offhand. Vanilla accepts only 1-45 here because the receiver indexes <c>inventoryMenu.getSlot</c> directly; slot 0 is the crafting RESULT and is ignored by the server, and a NEGATIVE slot means "drop this stack into the world" rather than store it. Passing a hotbar number here targets the crafting area, not the hotbar.</param>
    /// <param name="item">The stack to place.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <remarks>The local write is optimistic, like <see cref="ClickAsync"/>: the server echoes the accepted result on window 0 and that authoritative value overwrites this one. Only a storable slot is applied locally, so a drop (negative slot) and the server-ignored slot 0 change nothing here, which keeps the snapshot matching what the server will actually do.</remarks>
    /// <exception cref="FeatureDisabledException">The inventory feature is disabled.</exception>
    public async Task CreativeSetSlotAsync(int slot, Umpk.Game.Items.ItemStack item, CancellationToken ct = default)
    {
        if (!_services.State.Features.Inventory)
            throw new FeatureDisabledException("Inventory");

        // The 1.8 creative-slot packet is a separate wire identity (minecraft:creative_inventory_action);
        // without this flag the packet resolves to the 1.9+ identity, which is unbound on 47, and the send throws instead of reaching the server.
        bool legacy = _services.Version.Version.Protocol < CreativeSlotModernIdentityMin;
        await _sink.SendAsync(new ServerboundSetCreativeModeSlotPacket((short)slot, item, IsLegacy: legacy), ct).ConfigureAwait(false);

        if (slot >= CreativeSlotStorableMin && slot <= CreativeSlotStorableMax)
            Inventory.SetSlot(InventoryState.PlayerWindowId, slot, item);

    }

    /// <summary>Selects a merchant trade by index.</summary>
    public Task SelectTradeAsync(int tradeIndex, CancellationToken ct = default)
        => _sink.SendAsync(new ServerboundSelectTradePacket(tradeIndex), ct).AsTask();

    /// <summary>Clicks a button in a window (the enchant-table option select, lectern page turn, and other window buttons). <paramref name="windowId"/> defaults to the open window.</summary>
    /// <exception cref="ActionNotSupportedException">The negotiated version cannot carry <c>container_button_click</c>; ask <see cref="ClientActionCapabilities.CanClickContainerButton"/> to branch instead of catching.</exception>
    public Task ClickContainerButtonAsync(int buttonId, int? windowId = null, CancellationToken ct = default)
    {
        Require(
            _services.Capabilities.CanClickContainerButton,
            nameof(ClickContainerButtonAsync),
            ItemPackets.Serverbound.ContainerButtonClick.Id);

        return _sink.SendAsync(
            new ServerboundContainerButtonClickPacket(windowId ?? Inventory.OpenWindowId, buttonId), ct).AsTask();
    }

    /// <summary>
    /// Sends an anvil rename for the item in the open anvil, on either side of the 1.13 boundary: the dedicated <c>rename_item</c> packet from 1.13, and the <c>MC|ItemName</c> plugin channel on 1.8-1.12.2, which is the only route those versions have.
    /// <para>The legacy payload is exactly one Minecraft string: a VarInt byte length followed by UTF-8 bytes. The server filters disallowed chat characters before applying the item name.</para>
    /// <para>The server SILENTLY IGNORES a name longer than its era's cap (30 characters on older protocols and 35 characters on newer legacy protocols; this method does not truncate, so a caller that needs the rename to land must keep within that. An empty buffer means "clear the name" in vanilla, and an empty <paramref name="name"/> reproduces it because a zero-length string still writes its one-byte length.</para>
    /// </summary>
    /// <exception cref="ActionNotSupportedException">The negotiated version can carry NEITHER route; ask <see cref="ClientActionCapabilities.CanRenameItem"/> to branch instead of catching.</exception>
    public Task RenameItemAsync(string name, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ClientActionCapabilities capabilities = _services.Capabilities;
        if (capabilities.CanRenameItemDirect)
            return _sink.SendAsync(new ServerboundRenameItemPacket(name), ct).AsTask();

        Require(
            capabilities.CanRenameItemLegacy,
            nameof(RenameItemAsync),
            ItemPackets.Serverbound.RenameItem.Id,
            "This version has neither the 1.13+ rename_item packet nor a serverbound play custom_payload "
            + "wire id for the pre-1.13 MC|ItemName channel.");

        return SendLegacyItemNameAsync(name, ct).AsTask();
    }

    /// <summary>Writes the pre-1.13 <c>MC|ItemName</c> rename as a raw serverbound <c>custom_payload</c> frame: the channel name, then the new name, both as Minecraft Strings. Built here rather than through <c>PluginChannelManager.SendAsync</c> because that surface is keyed by <see cref="Identifier"/> and <c>MC|ItemName</c> is not a well-formed identifier: it predates the namespaced-channel convention, so no <c>Identifier</c> can represent it and the raw frame is the only honest way to put it on the wire.</summary>
    private ValueTask SendLegacyItemNameAsync(string name, CancellationToken ct)
    {
        int wireId = _services.Wire.ServerboundPlay(CustomPayloadId);
        var body = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new PacketWriter(body);
        writer.WriteString(LegacyItemNameChannel);
        writer.WriteString(name);
        return _sink.SendFrameAsync(wireId, body.WrittenMemory, ct);
    }

    /// <summary>Sends a book edit (the 1.17+ page-list packet): the page strings and an optional title (present when the book is being signed). <paramref name="slot"/> is the hotbar slot of the writable book.</summary>
    /// <exception cref="ActionNotSupportedException">The negotiated version cannot carry this book-edit form; ask <see cref="ClientActionCapabilities.CanEditBook"/> to branch instead of catching. Note that 393-753 DO register <c>edit_book</c>, under the legacy item-stack record this method does not construct, so the capability is false there while the identifier is present.</exception>
    public Task EditBookAsync(int slot, IReadOnlyList<string> pages, string? title = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pages);
        Require(
            _services.Capabilities.CanEditBook,
            nameof(EditBookAsync),
            UiPackets.Serverbound.EditBook.Id,
            "1.13-1.16.5 send the book as an item stack and pre-1.13 uses the MC|BEdit plugin channel; neither is modeled.");

        return _sink.SendAsync(new ServerboundEditBookPacket(slot, pages, title), ct).AsTask();
    }

    /// <summary>The first protocol whose creative-slot packet uses the 1.9+ wire identity.</summary>
    private const int CreativeSlotModernIdentityMin = 107;

    /// <summary>The lowest player-window slot a creative set can store into (vanilla accepts 1-45).</summary>
    private const int CreativeSlotStorableMin = 1;

    /// <summary>The highest player-window slot a creative set can store into (the offhand).</summary>
    private const int CreativeSlotStorableMax = 45;

    /// <summary>Requests the recipe book place a recipe into the open crafting-style container using the 1.21.2+ network recipe-display id form. <paramref name="windowId"/> defaults to the open window; <paramref name="makeAll"/> requests the maximum stack (shift-click). Use <see cref="PlaceRecipeByNameAsync"/> below 1.21.2, where the recipe is identified by a resource-location string instead.</summary>
    /// <exception cref="ActionNotSupportedException">The network-id form is not the active wire form; ask <see cref="ClientActionCapabilities.CanPlaceRecipe"/> to branch instead of catching.</exception>
    public Task PlaceRecipeAsync(int recipeNetworkId, bool makeAll = false, int? windowId = null, CancellationToken ct = default)
    {
        Require(
            _services.Capabilities.CanPlaceRecipe,
            nameof(PlaceRecipeAsync),
            ItemPackets.Serverbound.PlaceRecipe.Id,
            "Use PlaceRecipeByNameAsync below 1.21.2, where the recipe travels as a resource location.");

        return _sink.SendAsync(
            new ServerboundPlaceRecipePacket(windowId ?? Inventory.OpenWindowId, recipeNetworkId, makeAll), ct).AsTask();
    }

    /// <summary>Requests the recipe book place a recipe into the open crafting-style container using the pre-1.21.2 resource-location recipe identifier form. <paramref name="windowId"/> defaults to the open window; <paramref name="makeAll"/> requests the maximum stack.</summary>
    /// <remarks>The window this covers is whatever the binding table binds for the by-name record, currently 1.13-1.21.1. Reading it from the outbound table instead of protocol constants keeps the action aligned with the codec.</remarks>
    /// <exception cref="ActionNotSupportedException">The identifier form is not the active wire form; ask <see cref="ClientActionCapabilities.CanPlaceRecipeByName"/> to branch instead of catching.</exception>
    public Task PlaceRecipeByNameAsync(Identifier recipe, bool makeAll = false, int? windowId = null, CancellationToken ct = default)
    {
        Require(
            _services.Capabilities.CanPlaceRecipeByName,
            nameof(PlaceRecipeByNameAsync),
            ItemPackets.Serverbound.PlaceRecipeByName.Id,
            "Use PlaceRecipeAsync on 1.21.2+, where the recipe travels as a network display id.");

        return _sink.SendAsync(
            new ServerboundPlaceRecipeByNamePacket(windowId ?? Inventory.OpenWindowId, recipe, makeAll), ct).AsTask();
    }

    /// <summary>Renames the item in an open anvil, the same as <see cref="RenameItemAsync"/>, but reports FALSE instead of throwing when the negotiated version can carry neither wire route.</summary>
    /// <remarks>The <c>Try</c> prefix signals that a false result is an ordinary version-dependent outcome, not a swallowed error. See <see cref="ClientActionCapabilities.CanRenameItem"/> to branch before sending. <see cref="ActionNotSupportedException"/> remains the contract for other unsupported actions in this class.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public async Task<bool> TryRenameItemAsync(string name, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!_services.Capabilities.CanRenameItem)
            return false;

        await RenameItemAsync(name, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>Writes a book and quill, the same as <see cref="EditBookAsync"/>, but reports FALSE instead of throwing when the negotiated version cannot carry the dedicated 1.17+ book-edit packet.</summary>
    /// <remarks>See the remarks on <see cref="TryRenameItemAsync"/>: false here is a version answer, not a swallowed error.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="pages"/> is null.</exception>
    public async Task<bool> TryEditBookAsync(
        int slot, IReadOnlyList<string> pages, string? title = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (!_services.Capabilities.CanEditBook)
            return false;

        await EditBookAsync(slot, pages, title, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>Places a recipe by network id, the same as <see cref="PlaceRecipeAsync"/>, but reports whether anything actually left the client instead of throwing when the network-id form is not this version's live form (see <see cref="RecipePlacementSupport"/> to branch on the form beforehand).</summary>
    /// <remarks>See the remarks on <see cref="TryRenameItemAsync"/>: false here is a version answer, not a swallowed error.</remarks>
    public async Task<bool> TryPlaceRecipeAsync(
        int recipeNetworkId, bool makeAll = false, int? windowId = null, CancellationToken ct = default)
    {
        if (!_services.Capabilities.CanPlaceRecipe)
            return false;

        await PlaceRecipeAsync(recipeNetworkId, makeAll, windowId, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>Places a recipe by resource location, the same as <see cref="PlaceRecipeByNameAsync"/>, but reports whether anything actually left the client instead of throwing when the by-name form is not this version's live form (see <see cref="RecipePlacementSupport"/> to branch on the form beforehand).</summary>
    /// <remarks>See the remarks on <see cref="TryRenameItemAsync"/>: false here is a version answer, not a swallowed error.</remarks>
    public async Task<bool> TryPlaceRecipeByNameAsync(
        Identifier recipe, bool makeAll = false, int? windowId = null, CancellationToken ct = default)
    {
        if (!_services.Capabilities.CanPlaceRecipeByName)
            return false;

        await PlaceRecipeByNameAsync(recipe, makeAll, windowId, ct).ConfigureAwait(false);
        return true;
    }

    private void Require(bool capable, string action, Identifier packet, string? detail = null)
    {
        if (!capable)
            throw new ActionNotSupportedException(action, packet, _services.Version.Version.Protocol, detail);

    }

    private static (short Slot, byte Button, int Mode) Encode(ClickAction action) => action switch
    {
        ClickAction.Pickup p => ((short)p.Slot, (byte)p.Button, 0),
        ClickAction.QuickMove q => ((short)q.Slot, (byte)q.Button, 1),
        ClickAction.Swap s => ((short)s.Slot, (byte)s.HotbarButton, 2),
        ClickAction.CloneSlot c => ((short)c.Slot, (byte)2, 3),
        ClickAction.Throw t => ((short)t.Slot, (byte)(t.WholeStack ? 1 : 0), 4),
        ClickAction.Drag d => ((short)d.Slot, (byte)d.Button, 5),
        ClickAction.PickupAll pa => ((short)pa.Slot, (byte)pa.Button, 6),
        _ => ((short)-999, (byte)0, 0),
    };
}
