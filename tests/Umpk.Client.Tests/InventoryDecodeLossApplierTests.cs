using Umpk.Client.Events;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Constructed loss-scope ownership tests over the real applier chain. These records are model inputs, not native wire captures or live-session proof.</summary>
public sealed class InventoryDecodeLossApplierTests
{
    private const int KnownRevision = 7;
    private const int LostRevision = 31;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ScopedHeldLossUsesItsHeaderIndexSpaceWithoutApplyingRevision(int route)
    {
        ApplierHarness harness = await SeedAsync(version: RouteVersion(route));
        InventoryState inventory = harness.State.Inventory;
        InventoryUpdateLossScope scope = route switch
        {
            0 => new(InventoryUpdateLossKind.ContainerSlot, 0, LostRevision, 36, null),
            1 => new(InventoryUpdateLossKind.ContainerSlot, -2, LostRevision, 0, null),
            2 => new(InventoryUpdateLossKind.PlayerInventorySlot, null, null, 0, null),
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };
        List<IClientEvent> events = ObservePacketEffects(harness);

        Assert.False((object)scope is IPacket);
        bool handled = await harness.TryApplyAsync(scope);

        Assert.True(handled);
        Assert.False(inventory.IsPlayerSlotAvailable(36));
        Assert.True(inventory.IsPlayerSlotAvailable(0));
        Assert.True(inventory.IsPlayerSlotAvailable(9));
        Assert.True(inventory.IsCursorAvailable);
        Assert.Equal(TestItems.Stone(), inventory.PlayerSlots[36]);
        Assert.Equal(TestItems.Stone(3), inventory.Cursor);
        Assert.Equal(KnownRevision, inventory.StateId);
        AssertNoPacketEffects(harness, events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScopedCursorLossIgnoresLegacySlotIndexAndPreservesPlayerKnowledge(bool legacy)
    {
        ApplierHarness harness = await SeedAsync(version: legacy ? JavaVersions.V1_21_1 : JavaVersions.V1_21_5);
        InventoryState inventory = harness.State.Inventory;
        InventoryUpdateLossScope scope = legacy
            ? new(InventoryUpdateLossKind.ContainerSlot, -1, LostRevision, 123, null)
            : new(InventoryUpdateLossKind.Cursor, null, null, null, null);
        List<IClientEvent> events = ObservePacketEffects(harness);

        Assert.False((object)scope is IPacket);
        bool handled = await harness.TryApplyAsync(scope);

        Assert.True(handled);
        Assert.False(inventory.IsCursorAvailable);
        Assert.True(inventory.IsPlayerSlotAvailable(36));
        Assert.Equal(TestItems.Stone(), inventory.PlayerSlots[36]);
        Assert.Equal(TestItems.Stone(3), inventory.Cursor);
        Assert.Equal(KnownRevision, inventory.StateId);
        AssertNoPacketEffects(harness, events);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ScopedFullLossPreservesCachedValuesAndCoversHeaderShape(int mode)
    {
        ApplierHarness harness = await SeedAsync(openContainer: mode != 0, provisional: mode == 2);
        InventoryState inventory = harness.State.Inventory;
        ItemStack[] playerBefore = inventory.PlayerSlots.ToArray();
        ItemStack[]? containerBefore = inventory.ContainerSlots?.ToArray();
        int window = mode == 0 ? 0 : 3;
        int count = mode == 0 ? 46 : 63;
        var scope = new InventoryUpdateLossScope(
            InventoryUpdateLossKind.ContainerContents, window, LostRevision, null, count);
        List<IClientEvent> events = ObservePacketEffects(harness);

        Assert.False((object)scope is IPacket);
        bool handled = await harness.TryApplyAsync(scope);

        Assert.True(handled);
        Assert.Equal(playerBefore, inventory.PlayerSlots.ToArray());
        if (containerBefore is not null)
            Assert.Equal(containerBefore, inventory.ContainerSlots!.ToArray());
        Assert.All(Enumerable.Range(9, 36), slot => Assert.False(inventory.IsPlayerSlotAvailable(slot)));
        Assert.Equal(mode != 0, inventory.IsPlayerSlotAvailable(5));
        Assert.Equal(mode != 0, inventory.IsPlayerSlotAvailable(45));
        Assert.False(inventory.IsCursorAvailable);
        Assert.False(inventory.IsActiveSnapshotAvailable);
        Assert.Throws<InvalidOperationException>(() => inventory.CaptureActiveSnapshot());
        Assert.Equal(TestItems.Stone(3), inventory.Cursor);
        Assert.Equal(KnownRevision, inventory.StateId);
        AssertNoPacketEffects(harness, events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LossForLateWindowLeavesTrackedInventoryUntouched(bool fullContent)
    {
        ApplierHarness harness = await SeedAsync(openContainer: true);
        InventoryState inventory = harness.State.Inventory;
        ItemStack[] playerBefore = inventory.PlayerSlots.ToArray();
        ItemStack[] containerBefore = inventory.ContainerSlots!.ToArray();
        InventoryUpdateLossScope scope = fullContent
            ? new(InventoryUpdateLossKind.ContainerContents, 2, LostRevision, null, 63)
            : new(InventoryUpdateLossKind.ContainerSlot, 2, LostRevision, 54, null);
        List<IClientEvent> events = ObservePacketEffects(harness);

        await harness.ApplyAsync(scope);

        Assert.Equal(playerBefore, inventory.PlayerSlots.ToArray());
        Assert.Equal(containerBefore, inventory.ContainerSlots!.ToArray());
        Assert.All(Enumerable.Range(0, 46), slot => Assert.True(inventory.IsPlayerSlotAvailable(slot)));
        Assert.True(inventory.IsActiveSnapshotAvailable);
        Assert.Equal(TestItems.Stone(3), inventory.Cursor);
        Assert.Equal(KnownRevision, inventory.StateId);
        AssertNoPacketEffects(harness, events);
    }

    [Theory]
    [InlineData(false, 41)]
    [InlineData(false, 42)]
    [InlineData(true, 41)]
    [InlineData(true, 42)]
    public async Task EquipmentLossWithoutPlayerMenuSlotDoesNotGuessAnAlias(bool legacy, int inventorySlot)
    {
        ApplierHarness harness = await SeedAsync(openContainer: true);
        InventoryState inventory = harness.State.Inventory;
        ItemStack[] playerBefore = inventory.PlayerSlots.ToArray();
        ItemStack[] containerBefore = inventory.ContainerSlots!.ToArray();
        InventoryUpdateLossScope scope = legacy
            ? new(InventoryUpdateLossKind.ContainerSlot, -2, LostRevision, inventorySlot, null)
            : new(InventoryUpdateLossKind.PlayerInventorySlot, null, null, inventorySlot, null);
        List<IClientEvent> events = ObservePacketEffects(harness);

        await harness.ApplyAsync(scope);

        Assert.Equal(playerBefore, inventory.PlayerSlots.ToArray());
        Assert.Equal(containerBefore, inventory.ContainerSlots!.ToArray());
        Assert.All(Enumerable.Range(0, 46), slot => Assert.True(inventory.IsPlayerSlotAvailable(slot)));
        Assert.True(inventory.IsActiveSnapshotAvailable);
        Assert.Equal(KnownRevision, inventory.StateId);
        AssertNoPacketEffects(harness, events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectInventoryPacketRecoveryRestoresHeldAndActiveAliasKnowledge(bool legacy)
    {
        ApplierHarness harness = await SeedAsync(openContainer: true,
            version: legacy ? JavaVersions.V1_21_1 : JavaVersions.V1_21_5);
        InventoryState inventory = harness.State.Inventory;
        inventory.MarkSlotUnavailable(0, 36);
        Assert.False(inventory.IsPlayerSlotAvailable(36));
        Assert.False(inventory.IsSlotAvailable(3, 54));
        object recovery = legacy
            ? new ClientboundContainerSetSlotPacket(-2, LostRevision, 0, TestItems.DiamondSword())
            : new ClientboundSetPlayerInventoryPacket(0, TestItems.DiamondSword());

        Assert.True(await harness.TryApplyAsync(recovery));

        Assert.Equal(TestItems.DiamondSword(), inventory.PlayerSlots[36]);
        Assert.Equal(TestItems.DiamondSword(), inventory.ContainerSlots![54]);
        Assert.True(inventory.IsPlayerSlotAvailable(36));
        Assert.True(inventory.IsSlotAvailable(3, 54));
        Assert.True(inventory.IsActiveSnapshotAvailable);
        Assert.Equal(TestItems.Stone(3), inventory.Cursor);
        Assert.Equal(KnownRevision, inventory.StateId);
        Assert.Empty(harness.Recorder.Packets);
        Assert.Empty(harness.Recorder.Frames);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinaryWindowAndCursorPacketsAlreadyRestoreTheirAcceptedFields(bool cursor)
    {
        ApplierHarness harness = await SeedAsync();
        InventoryState inventory = harness.State.Inventory;
        object recovery;
        if (cursor)
        {
            inventory.MarkCursorUnavailable();
            recovery = new ClientboundSetCursorItemPacket(TestItems.DiamondSword());
        }
        else
        {
            inventory.MarkSlotUnavailable(0, 36);
            recovery = new ClientboundContainerSetSlotPacket(0, 0, 36, TestItems.DiamondSword());
        }

        Assert.True(await harness.TryApplyAsync(recovery));

        Assert.True(inventory.IsActiveSnapshotAvailable);
        Assert.True(inventory.IsPlayerSlotAvailable(36));
        Assert.True(inventory.IsCursorAvailable);
        Assert.Equal(cursor ? TestItems.Stone() : TestItems.DiamondSword(), inventory.PlayerSlots[36]);
        Assert.Equal(cursor ? TestItems.DiamondSword() : TestItems.Stone(3), inventory.Cursor);
        Assert.Equal(cursor ? KnownRevision : 0, inventory.StateId);
        Assert.Empty(harness.Recorder.Packets);
        Assert.Empty(harness.Recorder.Frames);
    }

    [Fact]
    public async Task DisabledInventoryDoesNotRegisterLossScopeOwnership()
    {
        var harness = new ApplierHarness(JavaVersions.V1_21_5, new ClientFeatures { Inventory = false });
        var scope = new InventoryUpdateLossScope(InventoryUpdateLossKind.ContainerSlot, 0, LostRevision, 36, null);
        List<IClientEvent> events = ObservePacketEffects(harness);

        Assert.False(await harness.TryApplyAsync(scope));

        Assert.Throws<FeatureDisabledException>(() => harness.State.Inventory);
        AssertNoPacketEffects(harness, events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnscopedObjectsDoNotInvalidateInventoryOrInventPackets(bool declaredLoss)
    {
        ApplierHarness harness = await SeedAsync();
        InventoryState inventory = harness.State.Inventory;
        ItemStack[] before = inventory.PlayerSlots.ToArray();
        object unscoped = declaredLoss
            ? new InboundDecodeLoss(ProtocolPhase.Play, PacketFlow.Clientbound, default,
                26, Identifier.Minecraft("weapon"), InventoryScope: null)
            : new object();
        List<IClientEvent> events = ObservePacketEffects(harness);

        Assert.False(unscoped is IPacket);
        Assert.False(await harness.TryApplyAsync(unscoped));

        Assert.Equal(before, inventory.PlayerSlots.ToArray());
        Assert.True(inventory.IsActiveSnapshotAvailable);
        Assert.Equal(TestItems.Stone(3), inventory.Cursor);
        Assert.Equal(KnownRevision, inventory.StateId);
        AssertNoPacketEffects(harness, events);
    }

    private static JavaVersion RouteVersion(int route) =>
        route == 1 ? JavaVersions.V1_21_1 : JavaVersions.V1_21_5;

    private static async Task<ApplierHarness> SeedAsync(
        bool openContainer = false,
        bool provisional = false,
        JavaVersion? version = null)
    {
        JavaVersion chosenVersion = version ?? JavaVersions.V1_21_5;
        var harness = new ApplierHarness(chosenVersion);
        harness.State.Registries = JavaGameData.Registries(chosenVersion.Version.Protocol);
        ItemStack[] player = Empty(46);
        player[5] = TestItems.Stone(2);
        player[36] = TestItems.Stone();
        player[45] = TestItems.DiamondSword();
        await harness.ApplyAsync(new ClientboundContainerSetContentPacket(
            0, KnownRevision, player, TestItems.Stone(3)));
        if (openContainer)
        {
            var menus = harness.State.Registries!.MenuTypes;
            Assert.True(menus.TryGet(Identifier.Minecraft("generic_9x3"), out var menu));
            await harness.ApplyAsync(new ClientboundOpenScreenPacket(
                3, menu.NetworkId, Umpk.Text.Component.Text("Model scope test"), null, 0, null));
            Assert.NotNull(harness.State.Inventory.OpenContainerMenuType);
            Assert.Equal(Identifier.Minecraft("generic_9x3"), harness.State.Inventory.OpenContainerMenuType!.Value.Id);
            Assert.Equal(menu.NetworkId, harness.State.Inventory.OpenContainerMenuTypeId);
            if (provisional)
                Assert.Equal(1, harness.State.Inventory.ActiveSlotCount);
            else
            {
                ItemStack[] container = Empty(63);
                container[0] = TestItems.Stone(4);
                container[54] = TestItems.Stone();
                await harness.ApplyAsync(new ClientboundContainerSetContentPacket(
                    3, KnownRevision, container, TestItems.Stone(3)));
            }
        }
        return harness;
    }

    private static List<IClientEvent> ObservePacketEffects(ApplierHarness harness)
    {
        var events = new List<IClientEvent>();
        harness.Events.Subscribe<PacketReceived>(e => events.Add(e));
        harness.Events.Subscribe<PredictionCorrected>(e => events.Add(e));
        return events;
    }

    private static void AssertNoPacketEffects(ApplierHarness harness, List<IClientEvent> events)
    {
        Assert.Empty(events);
        Assert.Empty(harness.Recorder.Packets);
        Assert.Empty(harness.Recorder.Frames);
    }

    private static ItemStack[] Empty(int count)
    {
        var slots = new ItemStack[count];
        Array.Fill(slots, ItemStack.Empty);
        return slots;
    }
}
