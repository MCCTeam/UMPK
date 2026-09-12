using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>client-send coverage: <see cref="InventoryActions.ClickAsync"/> emits the raw predicted changed slots and cursor (later hashed by the codec), not the old empty-shortcut, and records a pending prediction. Pins that the hashing shortcut is gone from the send path.</summary>
public sealed class InventoryClickSendTests
{
    [Theory]
    [InlineData(63, 27)] // single chest / barrel: 27 container + 36 player slots
    [InlineData(90, 54)] // double chest: 54 container + 36 player slots
    public void GenericOpenContainerLayout_UsesAllThirtySixPlayerSlots(int slotCount, int containerSlots)
    {
        SlotLayout layout = GenericLayouts.For(slotCount, hasOpenContainer: true);

        Assert.Equal(new SlotRange(0, containerSlots), Assert.Single(layout.QuickMoveTargets(containerSlots)));
        Assert.Equal(new SlotRange(containerSlots, slotCount), Assert.Single(layout.QuickMoveTargets(0)));
    }

    [Fact]
    public async Task ClickAsync_Pickup_Emits_NonEmpty_ChangedSlots_And_Cursor()
    {
        var recorder = new RecordingSink();
        var state = new ClientState(new ClientFeatures().Normalized());
        InventoryState inv = state.Inventory;
        inv.SetSlot(InventoryState.PlayerWindowId, 5, TestItems.Stone(4));

        var services = new ClientSessionServices
        {
            Version = JavaVersions.V1_21_5,
            Options = new ClientOptions(),
            Policies = new ClientPolicies(),
            State = state,
            Wire = new WireIndex(JavaVersions.V1_21_5),
            Logger = NullLogger.Instance,
            Scheduler = new Umpk.Hosting.ChannelSessionScheduler(),
        };
        var actions = new InventoryActions(recorder, services);

        // Pick up the stone in slot 5 onto the cursor.
        ClickResult result = await actions.ClickAsync(new ClickAction.Pickup(5, MouseButton.Left));

        ServerboundContainerClickPacket sent = Assert.IsType<ServerboundContainerClickPacket>(
            Assert.Single(recorder.Packets));

        Assert.NotEmpty(sent.ChangedSlots);                 // no longer the empty shortcut
        Assert.NotNull(sent.CarriedItem);
        Assert.False(sent.CarriedItem!.IsEmpty);            // the picked-up stack rides the cursor
        Assert.Equal(TestItems.Stone(4), sent.CarriedItem);
        Assert.Contains(sent.ChangedSlots, s => s.Slot == 5); // slot 5 reported changed
        Assert.Equal(result.Cursor, sent.CarriedItem);

        // A pending prediction was recorded for reconciliation.
        Assert.True(inv.HasPendingPrediction);
    }

    [Theory]
    [InlineData(63)] // single chest or barrel
    [InlineData(90)] // double chest
    public async Task ConsecutiveQuickMoves_DepositBothPlayerStacks_BeforeServerEchoes(int slotCount)
    {
        (InventoryActions actions, InventoryState inventory, RecordingSink recorder) = BuildOpenContainer(slotCount);
        int topSlots = slotCount - 36;
        var contents = Enumerable.Repeat(ItemStack.Empty, slotCount).ToArray();
        contents[topSlots] = TestItems.Stone(64);
        contents[topSlots + 1] = TestItems.Stone(64);
        inventory.ReplaceContents(inventory.OpenWindowId, contents);

        await actions.QuickMoveAsync(topSlots);
        await actions.QuickMoveAsync(topSlots + 1);

        Assert.Collection(
            recorder.Packets,
            packet => AssertQuickMovePacket(packet, topSlots, destinationSlot: 0, actionNumber: 0),
            packet => AssertQuickMovePacket(packet, topSlots + 1, destinationSlot: 1, actionNumber: 1));
        Assert.Equal(128, inventory.ContainerSlots!.Take(topSlots).Sum(stack => stack.Count));
        Assert.Equal(0, inventory.PlayerSlots.Sum(stack => stack.Count));
    }

    [Fact]
    public async Task ConsecutiveQuickMoves_WithdrawFiveFullStacks_BeforeServerEchoes()
    {
        (InventoryActions actions, InventoryState inventory, RecordingSink recorder) = BuildOpenContainer(90);
        var contents = Enumerable.Repeat(ItemStack.Empty, 90).ToArray();
        for (int slot = 0; slot < 5; slot++)
        {
            contents[slot] = TestItems.Stone(64);
        }
        inventory.ReplaceContents(inventory.OpenWindowId, contents);

        for (int slot = 0; slot < 5; slot++)
        {
            await actions.QuickMoveAsync(slot);
        }

        Assert.Collection(
            recorder.Packets,
            packet => AssertQuickMovePacket(packet, sourceSlot: 0, destinationSlot: 54, actionNumber: 0),
            packet => AssertQuickMovePacket(packet, sourceSlot: 1, destinationSlot: 55, actionNumber: 1),
            packet => AssertQuickMovePacket(packet, sourceSlot: 2, destinationSlot: 56, actionNumber: 2),
            packet => AssertQuickMovePacket(packet, sourceSlot: 3, destinationSlot: 57, actionNumber: 3),
            packet => AssertQuickMovePacket(packet, sourceSlot: 4, destinationSlot: 58, actionNumber: 4));
        Assert.Equal(320, inventory.PlayerSlots.Sum(stack => stack.Count));
        Assert.All(inventory.ContainerSlots!.Take(5), stack => Assert.True(stack.IsEmpty));
    }

    private static void AssertQuickMovePacket(object recorded, int sourceSlot, int destinationSlot, short actionNumber)
    {
        ServerboundContainerClickPacket packet = Assert.IsType<ServerboundContainerClickPacket>(recorded);
        Assert.Equal(1, packet.ContainerId);
        Assert.Equal(0, packet.StateId);
        Assert.Equal(sourceSlot, packet.Slot);
        Assert.Equal(0, packet.Button);
        Assert.Equal(1, packet.Mode);
        Assert.Equal(actionNumber, packet.ActionNumber);
        Assert.Equal(TestItems.Stone(64), packet.LegacyClickedItem);
        Assert.Equal(ItemStack.Empty, packet.CarriedItem);
        Assert.Contains(packet.ChangedSlots, change => change.Slot == sourceSlot && change.Stack.IsEmpty);
        Assert.Contains(
            packet.ChangedSlots,
            change => change.Slot == destinationSlot && change.Stack.Equals(TestItems.Stone(64)));
    }

    private static (InventoryActions Actions, InventoryState Inventory, RecordingSink Recorder) BuildOpenContainer(int slotCount)
    {
        var recorder = new RecordingSink();
        var state = new ClientState(new ClientFeatures().Normalized());
        state.Inventory.OpenContainer(windowId: 1, slotCount);
        var services = new ClientSessionServices
        {
            Version = JavaVersions.V26_2,
            Options = new ClientOptions(),
            Policies = new ClientPolicies(),
            State = state,
            Wire = new WireIndex(JavaVersions.V26_2),
            Logger = NullLogger.Instance,
            Scheduler = new Umpk.Hosting.ChannelSessionScheduler(),
        };
        return (new InventoryActions(recorder, services), state.Inventory, recorder);
    }
}
