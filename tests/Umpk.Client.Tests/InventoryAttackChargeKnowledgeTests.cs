using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;
using Xunit;
using Fixture = Umpk.Client.Tests.TickMiningLifecycleTests.Fixture;

namespace Umpk.Client.Tests;

/// <summary>Owner-level charge recovery controls using the existing tick fixture. Availability masks are constructed state inputs, not native traffic.</summary>
public sealed class InventoryAttackChargeKnowledgeTests
{
    [Theory]
    [InlineData("player-lifetime")]
    [InlineData("known-type-transition")]
    [InlineData("explicit-reset-after-known-item")]
    public async Task ADefiniteResetCanRestoreChargeKnowledge(string reset)
    {
        await using var fixture = new Fixture(777);
        fixture.SetBlock("bedrock");
        ItemStack spear = HoldRequiredSpear(fixture);
        await QuietTicks(fixture, 32);
        fixture.State.Inventory.MarkSlotUnavailable(0, 36);
        await QuietTicks(fixture, 1);
        fixture.State.Inventory.SetAuthoritativeSlot(0, 36, spear);

        // The first known item after the gap establishes an observation, not a native item-change reset.
        await QuietTicks(fixture, 1);
        if (reset == "player-lifetime")
            await fixture.EndPlayerLifetimeAsync();
        else if (reset == "known-type-transition")
        {
            fixture.Hold("stone");
            await QuietTicks(fixture, 1);
            fixture.State.Inventory.SetAuthoritativeSlot(0, 36, spear);
            await QuietTicks(fixture, 1);
        }
        else
        {
            Assert.Equal("explicit-reset-after-known-item", reset);
            await fixture.Actions.AttackEntityAsync(42);
            Assert.Single(fixture.Packets);
            fixture.Packets.Clear();
        }

        await QuietTicks(fixture, 32);
        using var caller = new CancellationTokenSource();
        Task action = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            await fixture.TickAsync();
            var stab = Assert.IsType<ServerboundPlayerActionPacket>(Assert.Single(fixture.Packets));
            Assert.Equal(8, stab.Action);
            Assert.Equal(default, stab.Position);
            Assert.Equal((byte)Direction.Down, stab.Direction);
            Assert.Equal(0, stab.Sequence);
            Assert.False(action.IsCompleted);
        }
        finally
        {
            await caller.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => action.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task ResetWhileLastItemIsUnknownDoesNotProveTheNextAdvance()
    {
        await using var fixture = new Fixture(777);
        fixture.SetBlock("bedrock");
        ItemStack spear = HoldRequiredSpear(fixture);
        await QuietTicks(fixture, 32);
        fixture.State.Inventory.MarkSlotUnavailable(0, 36);
        await QuietTicks(fixture, 1);
        await fixture.Actions.AttackEntityAsync(42);
        Assert.Single(fixture.Packets);
        fixture.Packets.Clear();
        fixture.State.Inventory.SetAuthoritativeSlot(0, 36, spear);

        // Reset proved zero at its phase. With unknown native last-item identity, the next advance could increment to one or reset to zero.
        await QuietTicks(fixture, 64);
        using var caller = new CancellationTokenSource();
        Task action = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            await fixture.TickAsync();
            Assert.Empty(fixture.Packets);
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => action.WaitAsync(TimeSpan.FromSeconds(5)));
            await QuietTicks(fixture, 2);
        }
        finally
        {
            await caller.CancelAsync();
            if (!action.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => action.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    private static ItemStack HoldRequiredSpear(Fixture fixture)
    {
        fixture.Hold("diamond_spear");
        ItemStack spear = fixture.State.Inventory.PlayerSlots[36]
            .With(DataComponents.MinimumAttackCharge, new MinimumAttackChargeComponent(1));
        fixture.State.Inventory.SetAuthoritativeSlot(0, 36, spear);
        return spear;
    }

    private static async Task QuietTicks(Fixture fixture, int count)
    {
        for (int tick = 0; tick < count; tick++)
        {
            await fixture.TickAsync();
            Assert.Empty(fixture.Packets);
        }
    }
}
