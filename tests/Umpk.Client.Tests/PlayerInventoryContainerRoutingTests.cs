using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class PlayerInventoryContainerRoutingTests
{
    private static JavaVersion Version => JavaVersions.V1_21_5;

    // A player-inventory (container id 0) full-content sync arriving while a chest (container id 3) is open must not overwrite the open container's slots. Vanilla servers routinely send container-0 updates while another window is open (e.g. item pickups). Expectation (one container model, id-routed): the chest view keeps its 27 slots.
    [Fact]
    public async Task PlayerInventorySync_WhileContainerOpen_DoesNotClobberContainer()
    {
        var harness = new ApplierHarness(Version);

        await harness.ApplyAsync(new ClientboundOpenScreenPacket(
            ContainerId: 3, MenuTypeId: 0, Title: Umpk.Text.Component.Text("Chest"),
            LegacyType: null, LegacySlotCount: 27, LegacyEntityId: null));
        Assert.Equal(27, harness.State.Inventory.ContainerSlots!.Count);

        // Full player-inventory sync (46 slots, window id 0) while the chest is open.
        var items = new Umpk.Game.Items.ItemStack[46];
        Array.Fill(items, Umpk.Game.Items.ItemStack.Empty);
        await harness.ApplyAsync(new ClientboundContainerSetContentPacket(
            ContainerId: 0, StateId: 5, Items: items, CarriedItem: Umpk.Game.Items.ItemStack.Empty));

        // The chest view must still be a 27-slot container; the player sync belongs to window 0.
        Assert.True(harness.State.Inventory.HasOpenContainer, "the chest should still be open");
        Assert.Equal(27, harness.State.Inventory.ContainerSlots!.Count);
    }
}
