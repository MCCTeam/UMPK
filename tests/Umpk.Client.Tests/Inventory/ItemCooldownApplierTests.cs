using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests.PacketApplication.Inventory;

/// <summary>Item cooldown identity and duration delivered through the bound packet codec.</summary>
public sealed class ItemCooldownApplierTests
{
    [Theory]
    [InlineData(107)]
    [InlineData(340)]
    [InlineData(404)]
    public async Task DistinctItemIds_ProduceDistinctCooldowns(int protocol)
    {
        ApplierHarness harness = await BoundPacketApplierHarness.JoinedAsync(protocol);
        var seen = new List<ItemCooldownChanged>();
        harness.Events.Subscribe<ItemCooldownChanged>(seen.Add);

        await BoundPacketApplierHarness.RoundTripAndApplyAsync(
            harness,
            protocol,
            "cooldown",
            new ClientboundCooldownPacket(default, 100, ItemId: 368));
        await BoundPacketApplierHarness.RoundTripAndApplyAsync(
            harness,
            protocol,
            "cooldown",
            new ClientboundCooldownPacket(default, 40, ItemId: 386));

        Assert.Equal(2, seen.Count);
        Assert.Equal(new[] { 368, 386 }, seen.Select(value => value.ItemId));
        Assert.Equal(100, seen[0].CooldownTicks);
        Assert.Equal(40, seen[1].CooldownTicks);
    }
}
