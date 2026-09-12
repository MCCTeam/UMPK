using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>The "a give never showed up in the local inventory" story, from both directions: the creative set we SEND, and the server-authoritative writes we RECEIVE for slots that belong to no open window.</summary>
public sealed class CreativeGiveReflectionTests
{
    private static (InventoryActions Actions, ClientState State, RecordingSink Sink) Setup(JavaVersion version)
    {
        var recorder = new RecordingSink();
        var state = new ClientState(new ClientFeatures().Normalized());
        var services = new ClientSessionServices
        {
            Version = version,
            Options = new ClientOptions(),
            Policies = new ClientPolicies(),
            State = state,
            Wire = new WireIndex(version),
            Logger = NullLogger.Instance,
            Scheduler = new Umpk.Hosting.ChannelSessionScheduler(),
        };
        return (new InventoryActions(recorder, services), state, recorder);
    }

    [Fact]
    public async Task CreativeSetSlot_AppliesOptimisticallyToThePlayerSnapshot()
    {
        // The send must update the local snapshot optimistically without waiting for a server echo. Slot 36 is the first hotbar slot in the player-window index space the API documents.
        (InventoryActions actions, ClientState state, RecordingSink sink) = Setup(JavaVersions.V1_21_5);

        await actions.CreativeSetSlotAsync(36, TestItems.DiamondSword());

        var sent = Assert.IsType<ServerboundSetCreativeModeSlotPacket>(Assert.Single(sink.Packets));
        Assert.Equal(36, sent.Slot);
        Assert.False(sent.IsLegacy);
        Assert.Equal(TestItems.DiamondSword(), state.Inventory.PlayerSlots[36]);
    }

    [Fact]
    public async Task CreativeSetSlot_On1_8_UsesTheLegacyPacketIdentity()
    {
        // 1.8 sends minecraft:creative_inventory_action, a different wire identity. Without the flag the packet resolved to the 1.9+ identity, which is unbound on protocol 47, so the send threw.
        (InventoryActions actions, ClientState state, RecordingSink sink) = Setup(JavaVersions.V1_8);

        await actions.CreativeSetSlotAsync(36, TestItems.Stone(5));

        var sent = Assert.IsType<ServerboundSetCreativeModeSlotPacket>(Assert.Single(sink.Packets));
        Assert.True(sent.IsLegacy);
        Assert.Equal(TestItems.Stone(5), state.Inventory.PlayerSlots[36]);
    }

    [Theory]
    [InlineData(0)]   // the crafting RESULT slot; vanilla accepts only 1-45 and ignores this
    [InlineData(-1)]  // a negative slot means "drop into the world", not "store"
    public async Task CreativeSetSlot_NonStorableSlot_SendsButChangesNothingLocally(int slot)
    {
        // Mirroring the server keeps the snapshot honest: vanilla's handler only writes when the slot is in 1-45.
        (InventoryActions actions, ClientState state, RecordingSink sink) = Setup(JavaVersions.V1_21_5);

        await actions.CreativeSetSlotAsync(slot, TestItems.DiamondSword());

        Assert.Single(sink.Packets);
        Assert.True(state.Inventory.PlayerSlots.All(s => s.IsEmpty));
    }

    [Fact]
    public async Task ServerGive_OnContainerIdMinusTwo_ReachesThePlayerSnapshot()
    {
        // Container id -2 is "write this player-inventory slot", in INVENTORY index space, and it is how a server reports a result belonging to no menu: pick-item and give/pickup overflow The applier had no case for it at all, so every such write was dropped by window routing. Inventory 0 is hotbar slot 0, which is menu index 36.
        var harness = new ApplierHarness(JavaVersions.V1_21);

        await harness.ApplyAsync(new ClientboundContainerSetSlotPacket(
            ContainerId: -2, StateId: 0, Slot: 0, Item: TestItems.DiamondSword()));

        Assert.Equal(TestItems.DiamondSword(), harness.State.Inventory.PlayerSlots[36]);
    }

    [Fact]
    public async Task ServerGive_OnContainerIdMinusTwo_AppliesEvenWhileAContainerIsOpen()
    {
        var harness = new ApplierHarness(JavaVersions.V1_21);
        await harness.ApplyAsync(new ClientboundOpenScreenPacket(
            4, 2, Umpk.Text.Component.Text("Chest"), null, 0, null));

        await harness.ApplyAsync(new ClientboundContainerSetSlotPacket(
            ContainerId: -2, StateId: 0, Slot: 40, Item: TestItems.Stone(9)));

        InventoryState inv = harness.State.Inventory;
        Assert.True(inv.HasOpenContainer);
        Assert.Equal(TestItems.Stone(9), inv.PlayerSlots[45]); // inventory 40 (offhand) -> menu 45
        Assert.True(inv.ContainerSlots!.All(s => s.IsEmpty));  // the open window is untouched
    }

    [Fact]
    public async Task CursorSetSlot_IsKeyedOnContainerIdAlone()
    {
        // Vanilla routes container id -1 to setCarried regardless of the slot index Requiring slot == -1 too meant a cursor update sent with any other index was dropped.
        var harness = new ApplierHarness(JavaVersions.V1_21);

        await harness.ApplyAsync(new ClientboundContainerSetSlotPacket(
            ContainerId: -1, StateId: 0, Slot: 0, Item: TestItems.DiamondSword()));

        Assert.Equal(TestItems.DiamondSword(), harness.State.Inventory.Cursor);
    }
}
