using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;
using Xunit;
using Fixture = Umpk.Client.Tests.TickMiningLifecycleTests.Fixture;

namespace Umpk.Client.Tests;

public sealed class PiercingMiningInputTests
{
    [Theory]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    [InlineData(777)]
    public async Task EntityAttackResetsTheChargeBeforeTheNextBlockClick(int protocol)
    {
        await using var fixture = new Fixture(protocol);
        fixture.SetBlock("bedrock");
        fixture.Hold("diamond_spear");
        for (int tick = 0; tick < 6; tick++)
            await fixture.TickAsync();
        await fixture.Actions.AttackEntityAsync(42);
        fixture.Packets.Clear();
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            await fixture.TickAsync();
            Assert.Empty(fixture.Packets);
        }
        finally
        {
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Theory]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    [InlineData(777)]
    public async Task UnchargedOrdinaryClickStillEntersTheHeldPathWithOneSwing(int protocol)
    {
        await using var fixture = new Fixture(protocol);
        fixture.SetBlock("bedrock");
        fixture.Hold("stone");
        ItemStack held = fixture.State.Inventory.PlayerSlots[36].With(DataComponents.MinimumAttackCharge, new MinimumAttackChargeComponent(1));
        fixture.State.Inventory.SetSlot(0, 36, held);
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            await fixture.TickAsync();
            Assert.Equal(2, fixture.Packets.Count);
            Assert.Equal(0, Assert.IsType<ServerboundPlayerActionPacket>(fixture.Packets[0]).Action);
            Assert.True(fixture.Packets[1] is ServerboundPunchPacket or ServerboundSwingPacket);
        }
        finally
        {
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Theory]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    [InlineData(777)]
    public async Task ARejectedReclickUsesTheCachedTargetWithoutInventingAnotherStart(int protocol)
    {
        await using var fixture = new Fixture(protocol);
        fixture.SetBlock("bedrock");
        fixture.Hold("stone");
        ItemStack held = fixture.State.Inventory.PlayerSlots[36].With(DataComponents.MinimumAttackCharge, new MinimumAttackChargeComponent(1));
        fixture.State.Inventory.SetSlot(0, 36, held);
        for (int tick = 0; tick < 6; tick++)
            await fixture.TickAsync();
        using var first = new CancellationTokenSource();
        Task initial = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, first.Token);
        await fixture.TickAsync();
        first.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => initial.WaitAsync(TimeSpan.FromSeconds(5)));
        fixture.Packets.Clear();
        using var second = new CancellationTokenSource();
        Task again = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, second.Token);
        try
        {
            await fixture.TickAsync();
            Assert.True(Assert.Single(fixture.Packets) is ServerboundPunchPacket or ServerboundSwingPacket);
            second.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => again.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Single(fixture.Packets);
        }
        finally
        {
            second.Cancel();
            if (!again.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => again.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Theory]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    [InlineData(777)]
    public async Task DirectStopBypassesPiercingAndRetainsTheInactiveCachedTarget(int protocol)
    {
        await using var fixture = new Fixture(protocol);
        fixture.SetBlock("bedrock");
        fixture.Hold("stone");
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            await fixture.TickAsync();
            fixture.Hold("diamond_spear");
            await fixture.StopDestroyAsync();
            Assert.Equal(protocol == 777 ? 2 : 1, Assert.IsType<ServerboundPlayerActionPacket>(fixture.Packets[^1]).Action);
            int count = fixture.Packets.Count;
            await fixture.TickAsync();
            Assert.Equal(count, fixture.Packets.Count);
            fixture.Hold("stone");
            await fixture.TickAsync();
            Assert.Equal(count + 1, fixture.Packets.Count);
            Assert.True(fixture.Packets[^1] is ServerboundPunchPacket or ServerboundSwingPacket);
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(count + 1, fixture.Packets.Count);
        }
        finally
        {
            caller.Cancel();
            if (!mining.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Theory]
    [InlineData(774, false)]
    [InlineData(775, false)]
    [InlineData(776, false)]
    [InlineData(777, false)]
    [InlineData(774, true)]
    [InlineData(775, true)]
    [InlineData(776, true)]
    [InlineData(777, true)]
    public async Task InitialPiercingClickUsesItsChargeGateAndNeverBecomesOrdinaryMining(int protocol, bool removeCharge)
    {
        await using var fixture = new Fixture(protocol);
        fixture.SetBlock("bedrock");
        fixture.Hold("diamond_spear");
        if (removeCharge)
        {
            ItemStack held = fixture.State.Inventory.PlayerSlots[36];
            fixture.State.Inventory.SetSlot(0, 36, held.WithComponents(held.Components.Without(DataComponents.MinimumAttackCharge)));
        }
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            await fixture.TickAsync();
            if (removeCharge)
            {
                ServerboundPlayerActionPacket stab = Assert.IsType<ServerboundPlayerActionPacket>(fixture.Packets[0]);
                Assert.Equal(protocol == 777 ? 8 : 7, stab.Action);
                Assert.Equal(default, stab.Position);
                Assert.Equal((byte)Direction.Down, stab.Direction);
                Assert.Equal(0, stab.Sequence);
                if (protocol == 777)
                    Assert.Single(fixture.Packets);
                else
                    Assert.IsType<ServerboundSwingPacket>(fixture.Packets[1]);
            }
            else
                Assert.Empty(fixture.Packets);

            int count = fixture.Packets.Count;
            for (int tick = 0; tick < 30; tick++)
                await fixture.TickAsync();
            Assert.Equal(count, fixture.Packets.Count);
            Assert.False(mining.IsCompleted);
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(count, fixture.Packets.Count);
        }
        finally
        {
            caller.Cancel();
            if (!mining.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Theory]
    [InlineData(774, false)]
    [InlineData(775, false)]
    [InlineData(776, false)]
    [InlineData(777, false)]
    [InlineData(774, true)]
    [InlineData(775, true)]
    [InlineData(776, true)]
    [InlineData(777, true)]
    public async Task PiercingRetainsDestroyStateAcrossReleaseOrViewLoss(int protocol, bool loseView)
    {
        await using var fixture = new Fixture(protocol);
        fixture.SetBlock("bedrock");
        fixture.Hold("stone");
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockInViewAsync(Fixture.Target, ct: caller.Token);
        try
        {
            await fixture.TickAsync();
            int count = fixture.Packets.Count;
            fixture.Hold("diamond_spear");
            if (loseView)
                fixture.ViewHit = null;
            await fixture.TickAsync();
            if (!loseView)
                caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(count, fixture.Packets.Count);

            fixture.Hold("stone");
            await fixture.TickAsync();
            ServerboundPlayerActionPacket abort = Assert.IsType<ServerboundPlayerActionPacket>(fixture.Packets[^1]);
            Assert.Equal(protocol == 777 ? 2 : 1, abort.Action);
            Assert.Equal((byte)Direction.Down, abort.Direction);
            Assert.Equal(count + 1, fixture.Packets.Count);
            await fixture.TickAsync();
            Assert.Equal(count + 1, fixture.Packets.Count);
        }
        finally
        {
            caller.Cancel();
            if (!mining.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Theory]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    [InlineData(777)]
    public async Task RemovingPiercingWhileHeldStartsMiningWithoutInventingAnotherClick(int protocol)
    {
        await using var fixture = new Fixture(protocol);
        fixture.SetBlock("bedrock");
        fixture.Hold("diamond_spear");
        ItemStack held = fixture.State.Inventory.PlayerSlots[36];
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            await fixture.TickAsync();
            Assert.Empty(fixture.Packets);
            fixture.State.Inventory.SetSlot(0, 36, held.WithComponents(held.Components.Without(DataComponents.PiercingWeapon)));
            await fixture.TickAsync();
            Assert.Equal(2, fixture.Packets.Count);
            Assert.Equal(0, Assert.IsType<ServerboundPlayerActionPacket>(fixture.Packets[0]).Action);
            Assert.True(fixture.Packets[1] is ServerboundSwingPacket or ServerboundPunchPacket);
        }
        finally
        {
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }
}
