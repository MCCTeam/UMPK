using Umpk.Client.Events;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class InventoryPipelineTests
{
    private static JavaVersion Version => JavaVersions.V1_21_5;

    [Fact]
    public async Task SetContent_Sync_Is_Authoritative()
    {
        var harness = new ApplierHarness(Version);
        var items = new ItemStack[46];
        Array.Fill(items, ItemStack.Empty);
        items[5] = TestItems.Stone(3);
        ItemStack cursor = TestItems.DiamondSword();

        await harness.ApplyAsync(new ClientboundContainerSetContentPacket(
            ContainerId: 0, StateId: 3, Items: items, CarriedItem: cursor));

        // B-3 hardening: assert the full authoritative payload, not just StateId.
        InventoryState inv = harness.State.Inventory;
        Assert.Equal(3, inv.StateId);
        Assert.Equal(46, inv.PlayerSlots.Count);
        Assert.Equal(TestItems.Stone(3), inv.PlayerSlots[5]);
        Assert.True(inv.PlayerSlots[0].IsEmpty);
        Assert.Equal(cursor, inv.Cursor);
    }

    [Fact]
    public async Task PlayerWindow_SetSlot_WhileContainerOpen_TargetsPlayerArray()
    {
        // a set-slot for window 0 while a chest is open updates the player array, not the container.
        var harness = new ApplierHarness(Version);
        await harness.ApplyAsync(OpenChest(3, 27));

        await harness.ApplyAsync(new ClientboundContainerSetSlotPacket(
            ContainerId: 0, StateId: 0, Slot: 5, Item: TestItems.Stone(1)));

        InventoryState inv = harness.State.Inventory;
        Assert.True(inv.HasOpenContainer);
        Assert.Equal(27, inv.ContainerSlots!.Count);
        Assert.True(inv.ContainerSlots.All(s => s.IsEmpty)); // chest untouched
        Assert.Equal(TestItems.Stone(1), inv.PlayerSlots[5]);
    }

    [Fact]
    public async Task SetPlayerInventory_AlwaysTargetsPlayerArray_EvenWithContainerOpen()
    {
        // set_player_inventory writes the player array regardless of an open container, and its slot is in INVENTORY index space, not the menu space PlayerSlots stores. Vanilla applies it with The client and server use the same inventory slot. Inventory 40 is the offhand, which lives at menu index 45.
        var harness = new ApplierHarness(Version);
        await harness.ApplyAsync(OpenChest(4, 27));

        await harness.ApplyAsync(new ClientboundSetPlayerInventoryPacket(Slot: 40, Item: TestItems.DiamondSword()));

        InventoryState inv = harness.State.Inventory;
        Assert.True(inv.HasOpenContainer);
        Assert.Equal(TestItems.DiamondSword(), inv.PlayerSlots[45]);
        Assert.True(inv.ContainerSlots!.All(s => s.IsEmpty));
    }

    [Fact]
    public async Task SetPlayerInventory_MapsInventorySpaceToMenuSpace()
    {
        // The two index spaces agree only on the backpack. The hotbar is the case that made the old raw-index behaviour visibly wrong: inventory 0 is hotbar slot 0, which is menu 36, but writing it raw landed on menu 0, the crafting RESULT slot. Armor is reversed rather than merely shifted: inventory runs FEET..HEAD while the menu runs HEAD..FEET.
        var harness = new ApplierHarness(Version);
        InventoryState inv = harness.State.Inventory;

        await harness.ApplyAsync(new ClientboundSetPlayerInventoryPacket(Slot: 0, Item: TestItems.DiamondSword()));
        await harness.ApplyAsync(new ClientboundSetPlayerInventoryPacket(Slot: 9, Item: TestItems.Stone(7)));
        await harness.ApplyAsync(new ClientboundSetPlayerInventoryPacket(Slot: 36, Item: TestItems.Stone(3)));

        Assert.Equal(TestItems.DiamondSword(), inv.PlayerSlots[36]); // hotbar 0 -> menu 36
        Assert.Equal(TestItems.Stone(7), inv.PlayerSlots[9]);        // backpack is identity
        Assert.Equal(TestItems.Stone(3), inv.PlayerSlots[8]);        // FEET -> menu 8, not menu 5
        Assert.True(inv.PlayerSlots[0].IsEmpty);                     // crafting result untouched
        Assert.True(inv.PlayerSlots[5].IsEmpty);                     // HEAD untouched
    }

    [Fact]
    public async Task SetPlayerInventory_EquipmentIndexWithNoWindowSlot_IsDropped()
    {
        // 41 (body armor) and 42 (saddle) are 1.21.9+ equipment indices with no slot in the player window at all. They must be dropped, not folded onto whatever slot the arithmetic happens to land on.
        var harness = new ApplierHarness(Version);
        InventoryState inv = harness.State.Inventory;

        await harness.ApplyAsync(new ClientboundSetPlayerInventoryPacket(Slot: 41, Item: TestItems.DiamondSword()));
        await harness.ApplyAsync(new ClientboundSetPlayerInventoryPacket(Slot: 42, Item: TestItems.DiamondSword()));

        Assert.True(inv.PlayerSlots.All(s => s.IsEmpty));
    }

    [Fact]
    public async Task ClickPrediction_ContradictingSetSlot_ContentMismatch_RollsBackAndCorrects()
    {
        // an authoritative set-slot whose CONTENT differs from the prediction (not just the state id) rolls the optimistic snapshot back and raises PredictionCorrected.
        var harness = new ApplierHarness(Version);
        InventoryState inv = harness.State.Inventory;
        inv.StateId = 5;

        // Optimistic prediction: slot 5 becomes stone; the pre-click snapshot had it empty.
        ContainerSnapshot before = inv.CaptureActiveSnapshot();
        inv.SetSlot(InventoryState.PlayerWindowId, 5, TestItems.Stone());
        inv.RecordPrediction(InventoryState.PlayerWindowId, sentStateId: 5, before);

        bool corrected = false;
        harness.Events.Subscribe<PredictionCorrected>(_ => corrected = true);

        // Server rejects: it reports slot 5 as empty again with a fresh (not stale) state id.
        await harness.ApplyAsync(new ClientboundContainerSetSlotPacket(
            ContainerId: 0, StateId: 6, Slot: 5, Item: ItemStack.Empty));

        Assert.True(corrected);
        Assert.True(inv.PlayerSlots[5].IsEmpty); // rolled back + server value applied
        Assert.False(inv.HasPendingPrediction);
    }

    [Fact]
    public async Task ClickPrediction_ConfirmingSetSlot_DoesNotCorrect()
    {
        var harness = new ApplierHarness(Version);
        InventoryState inv = harness.State.Inventory;
        inv.StateId = 5;

        ContainerSnapshot before = inv.CaptureActiveSnapshot();
        inv.SetSlot(InventoryState.PlayerWindowId, 5, TestItems.Stone());
        inv.RecordPrediction(InventoryState.PlayerWindowId, sentStateId: 5, before);

        bool corrected = false;
        harness.Events.Subscribe<PredictionCorrected>(_ => corrected = true);

        // Server confirms the prediction: same content, advancing state id.
        await harness.ApplyAsync(new ClientboundContainerSetSlotPacket(
            ContainerId: 0, StateId: 6, Slot: 5, Item: TestItems.Stone()));

        Assert.False(corrected);
        Assert.Equal(TestItems.Stone(), inv.PlayerSlots[5]);
    }

    [Fact]
    public async Task ClickPrediction_OlderStateId_WithPending_PreservesPrediction()
    {
        // An older state-bearing update causally predates the click; it is not evidence of rejection and must not consume the pending prediction.
        var harness = new ApplierHarness(Version);
        InventoryState inv = harness.State.Inventory;
        inv.StateId = 10;

        ContainerSnapshot before = inv.CaptureActiveSnapshot();
        inv.SetSlot(InventoryState.PlayerWindowId, 5, TestItems.Stone());
        inv.RecordPrediction(InventoryState.PlayerWindowId, sentStateId: 10, before);

        bool corrected = false;
        harness.Events.Subscribe<PredictionCorrected>(_ => corrected = true);

        await harness.ApplyAsync(new ClientboundContainerSetSlotPacket(
            ContainerId: 0, StateId: 4, Slot: 5, Item: TestItems.Stone()));

        Assert.False(corrected);
        Assert.True(inv.HasPendingPrediction);
        Assert.Equal(10, inv.StateId);
    }

    [Fact]
    public async Task ClickPrediction_SameRevisionSlotTraffic_RebasesWithoutDiscardingPrediction()
    {
        // A merchant selection can enqueue slot traffic at revision N immediately before a click sent with revision N. The inbound and action queues are independent, so that pre-click packet may be applied after the optimistic click. It is not a rejection: preserve the predicted slot and keep the prediction pending until a causally newer revision arrives.
        var harness = new ApplierHarness(Version);
        await harness.ApplyAsync(OpenChest(7, 39));
        InventoryState inv = harness.State.Inventory;
        inv.StateId = 17;
        inv.SetSlot(7, 0, TestItems.Stone(11));

        ContainerSnapshot before = inv.CaptureActiveSnapshot();
        inv.SetSlot(7, 0, ItemStack.Empty);
        inv.SetSlot(7, 3, TestItems.Stone(11));
        inv.RecordPrediction(7, sentStateId: 17, before);

        bool corrected = false;
        harness.Events.Subscribe<PredictionCorrected>(_ => corrected = true);

        await harness.ApplyAsync(new ClientboundContainerSetSlotPacket(
            ContainerId: 7, StateId: 17, Slot: 0, Item: TestItems.Stone(11)));

        Assert.False(corrected);
        Assert.True(inv.ContainerSlots![0].IsEmpty);
        Assert.Equal(TestItems.Stone(11), inv.ContainerSlots[3]);
        Assert.Equal(TestItems.Stone(11), inv.PlayerSlots[9]);
        Assert.True(inv.HasPendingPrediction);
        Assert.Equal(17, inv.StateId);

        await harness.ApplyAsync(new ClientboundContainerSetSlotPacket(
            ContainerId: 7, StateId: 18, Slot: 0, Item: ItemStack.Empty));

        Assert.False(corrected);
        Assert.False(inv.HasPendingPrediction);
        Assert.Equal(18, inv.StateId);
    }

    [Fact]
    public async Task ClickPrediction_SameRevisionFullContent_RebasesPredictionDelta()
    {
        // A full pre-click snapshot is useful for slots the click did not change, but it must not erase the optimistic delta or consume the prediction. The first newer full snapshot is the authoritative acknowledgement and converges the state atomically.
        var harness = new ApplierHarness(Version);
        await harness.ApplyAsync(OpenChest(7, 39));
        InventoryState inv = harness.State.Inventory;
        inv.StateId = 23;
        inv.SetSlot(7, 0, TestItems.Stone(11));

        ContainerSnapshot before = inv.CaptureActiveSnapshot();
        inv.SetSlot(7, 0, ItemStack.Empty);
        inv.SetSlot(7, 3, TestItems.Stone(11));
        inv.RecordPrediction(7, sentStateId: 23, before);

        bool corrected = false;
        harness.Events.Subscribe<PredictionCorrected>(_ => corrected = true);

        var preClick = new ItemStack[39];
        Array.Fill(preClick, ItemStack.Empty);
        preClick[0] = TestItems.Stone(11);
        preClick[1] = TestItems.DiamondSword();
        await harness.ApplyAsync(new ClientboundContainerSetContentPacket(
            ContainerId: 7, StateId: 23, Items: preClick, CarriedItem: ItemStack.Empty));

        Assert.False(corrected);
        Assert.True(inv.ContainerSlots![0].IsEmpty);
        Assert.Equal(TestItems.DiamondSword(), inv.ContainerSlots[1]);
        Assert.Equal(TestItems.Stone(11), inv.ContainerSlots[3]);
        Assert.True(inv.HasPendingPrediction);
        Assert.Equal(23, inv.StateId);

        var accepted = new ItemStack[39];
        Array.Fill(accepted, ItemStack.Empty);
        accepted[1] = TestItems.DiamondSword();
        accepted[3] = TestItems.Stone(11);
        await harness.ApplyAsync(new ClientboundContainerSetContentPacket(
            ContainerId: 7, StateId: 24, Items: accepted, CarriedItem: ItemStack.Empty));

        Assert.False(corrected);
        Assert.False(inv.HasPendingPrediction);
        Assert.Equal(24, inv.StateId);
        Assert.Equal(TestItems.Stone(11), inv.PlayerSlots[9]);
    }

    [Fact]
    public async Task ClickPrediction_PreClickCursorEcho_DoesNotErasePredictedCursor()
    {
        var harness = new ApplierHarness(Version);
        InventoryState inv = harness.State.Inventory;
        inv.StateId = 31;
        inv.SetSlot(InventoryState.PlayerWindowId, 5, TestItems.DiamondSword());

        ContainerSnapshot before = inv.CaptureActiveSnapshot();
        inv.SetSlot(InventoryState.PlayerWindowId, 5, ItemStack.Empty);
        inv.Cursor = TestItems.DiamondSword();
        inv.RecordPrediction(InventoryState.PlayerWindowId, sentStateId: 31, before);

        await harness.ApplyAsync(new ClientboundSetCursorItemPacket(ItemStack.Empty));

        Assert.Equal(TestItems.DiamondSword(), inv.Cursor);
        Assert.True(inv.HasPendingPrediction);

        await harness.ApplyAsync(new ClientboundContainerSetSlotPacket(
            ContainerId: 0, StateId: 32, Slot: 5, Item: ItemStack.Empty));

        Assert.False(inv.HasPendingPrediction);
        Assert.Equal(TestItems.DiamondSword(), inv.Cursor);
    }

    [Fact]
    public async Task ClickPrediction_RapidSameRevisionClicks_ComposeOneBoundedPrediction()
    {
        var harness = new ApplierHarness(Version);
        await harness.ApplyAsync(OpenChest(7, 39));
        InventoryState inv = harness.State.Inventory;
        inv.StateId = 41;
        inv.SetSlot(7, 0, TestItems.Stone(11));

        ContainerSnapshot beforeFirst = inv.CaptureActiveSnapshot();
        inv.SetSlot(7, 0, ItemStack.Empty);
        inv.SetSlot(7, 3, TestItems.Stone(11));
        inv.RecordPrediction(7, sentStateId: 41, beforeFirst);

        ContainerSnapshot beforeSecond = inv.CaptureActiveSnapshot();
        inv.SetSlot(7, 3, ItemStack.Empty);
        inv.Cursor = TestItems.Stone(11);
        inv.RecordPrediction(7, sentStateId: 41, beforeSecond);

        var preClick = new ItemStack[39];
        Array.Fill(preClick, ItemStack.Empty);
        preClick[0] = TestItems.Stone(11);
        await harness.ApplyAsync(new ClientboundContainerSetContentPacket(
            ContainerId: 7, StateId: 41, Items: preClick, CarriedItem: ItemStack.Empty));

        Assert.True(inv.ContainerSlots![0].IsEmpty);
        Assert.True(inv.ContainerSlots[3].IsEmpty);
        Assert.Equal(TestItems.Stone(11), inv.Cursor);
        Assert.True(inv.HasPendingPrediction);
    }

    [Fact]
    public async Task ClickPrediction_StateIdWrap_TreatsZeroAsCausallyNewer()
    {
        var harness = new ApplierHarness(Version);
        InventoryState inv = harness.State.Inventory;
        inv.StateId = 0x7FFF;

        ContainerSnapshot before = inv.CaptureActiveSnapshot();
        inv.SetSlot(InventoryState.PlayerWindowId, 5, TestItems.Stone());
        inv.RecordPrediction(InventoryState.PlayerWindowId, sentStateId: 0x7FFF, before);

        await harness.ApplyAsync(new ClientboundContainerSetSlotPacket(
            ContainerId: 0, StateId: 0, Slot: 5, Item: TestItems.Stone()));

        Assert.False(inv.HasPendingPrediction);
        Assert.Equal(0, inv.StateId);
        Assert.Equal(TestItems.Stone(), inv.PlayerSlots[5]);
    }

    [Fact]
    public async Task LegacyPrediction_WithoutStateIds_StillAcceptsAuthoritativeCorrection()
    {
        var harness = new ApplierHarness(Version);
        InventoryState inv = harness.State.Inventory;

        ContainerSnapshot before = inv.CaptureActiveSnapshot();
        inv.SetSlot(InventoryState.PlayerWindowId, 5, TestItems.Stone());
        inv.RecordPrediction(
            InventoryState.PlayerWindowId,
            sentStateId: 0,
            before,
            usesStateIds: false);

        bool corrected = false;
        harness.Events.Subscribe<PredictionCorrected>(_ => corrected = true);
        await harness.ApplyAsync(new ClientboundContainerSetSlotPacket(
            ContainerId: 0, StateId: 0, Slot: 5, Item: ItemStack.Empty));

        Assert.True(corrected);
        Assert.False(inv.HasPendingPrediction);
        Assert.True(inv.PlayerSlots[5].IsEmpty);
    }

    [Fact]
    public async Task ClickPrediction_FullContentContradiction_Corrects_FromSetContentPath()
    {
        // the full-content resync path also reconciles: an authoritative content that differs from the predicted contents raises PredictionCorrected through the full-content path as well.
        var harness = new ApplierHarness(Version);
        InventoryState inv = harness.State.Inventory;

        ContainerSnapshot before = inv.CaptureActiveSnapshot();
        inv.SetSlot(InventoryState.PlayerWindowId, 5, TestItems.Stone());
        inv.RecordPrediction(InventoryState.PlayerWindowId, sentStateId: 0, before);

        bool corrected = false;
        harness.Events.Subscribe<PredictionCorrected>(_ => corrected = true);

        var items = new ItemStack[46];
        Array.Fill(items, ItemStack.Empty); // server disagrees: slot 5 is empty
        await harness.ApplyAsync(new ClientboundContainerSetContentPacket(
            ContainerId: 0, StateId: 7, Items: items, CarriedItem: ItemStack.Empty));

        Assert.True(corrected);
        Assert.True(inv.PlayerSlots[5].IsEmpty);
        Assert.False(inv.HasPendingPrediction);
    }

    [Fact]
    public async Task Open_And_Close_Container_Tracks_Window()
    {
        var harness = new ApplierHarness(Version);
        bool opened = false;
        bool closed = false;
        harness.Events.Subscribe<ContainerOpened>(_ => opened = true);
        harness.Events.Subscribe<ContainerClosed>(_ => closed = true);

        await harness.ApplyAsync(OpenChest(3, 27));
        Assert.True(harness.State.Inventory.HasOpenContainer);
        Assert.Equal(3, harness.State.Inventory.OpenWindowId);

        await harness.ApplyAsync(new ClientboundContainerClosePacket(3));
        Assert.False(harness.State.Inventory.HasOpenContainer);
        Assert.True(opened);
        Assert.True(closed);
    }

    [Theory]
    [InlineData(63, 27)]
    [InlineData(90, 54)]
    public async Task OpenContainerContent_MirrorsItsPlayerTail_ToPlayerInventory(int totalSlots, int containerSlots)
    {
        var harness = new ApplierHarness(Version);
        await harness.ApplyAsync(OpenChest(3, containerSlots));

        var contents = new ItemStack[totalSlots];
        Array.Fill(contents, ItemStack.Empty);
        contents[containerSlots] = TestItems.Stone(12); // menu player slot 9
        contents[^1] = TestItems.DiamondSword();        // menu player slot 44

        await harness.ApplyAsync(new ClientboundContainerSetContentPacket(
            ContainerId: 3, StateId: 5, Items: contents, CarriedItem: ItemStack.Empty));

        InventoryState inv = harness.State.Inventory;
        Assert.Equal(TestItems.Stone(12), inv.PlayerSlots[9]);
        Assert.Equal(TestItems.DiamondSword(), inv.PlayerSlots[44]);

        await harness.ApplyAsync(new ClientboundContainerSetSlotPacket(
            ContainerId: 3, StateId: 6, Slot: containerSlots, Item: TestItems.Stone(7)));

        Assert.Equal(TestItems.Stone(7), inv.PlayerSlots[9]);
    }

    private static ClientboundOpenScreenPacket OpenChest(int id, int slots) => new(
        ContainerId: id, MenuTypeId: 0, Title: Umpk.Text.Component.Text("Chest"),
        LegacyType: null, LegacySlotCount: slots, LegacyEntityId: null);
}
