using Umpk.Client.Actions;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;
using Xunit;
using Fixture = Umpk.Client.Tests.TickMiningLifecycleTests.Fixture;

namespace Umpk.Client.Tests;

public sealed class ViewMiningTests
{
    public static IEnumerable<object[]> Protocols => TickMiningLifecycleTests.Protocols;
    public static IEnumerable<object[]> LostTargets => Protocols.SelectMany(row => new[] { new object[] { row[0], false }, new object[] { row[0], true } });

    [Theory]
    [MemberData(nameof(LostTargets))]
    public async Task LeavingTheOriginalBlockReleasesItWithoutAttackingAnother(int protocol, bool anotherBlock)
    {
        await using var fixture = new Fixture(protocol);
        fixture.SetBlock("bedrock");
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockInViewAsync(Fixture.Target, ct: caller.Token);
        try
        {
            await fixture.TickAsync();
            Assert.Equal((byte)Direction.North, Assert.Single(fixture.Packets.OfType<ServerboundPlayerActionPacket>()).Direction);
            fixture.ViewHit = anotherBlock ? BlockHitResult.FromHit(new BlockPos(2, 64, 1), Direction.West, new Vec3d(2, 64.5, 1.5), 3) : null;
            await fixture.TickAsync();

            ServerboundPlayerActionPacket[] actions = fixture.Packets.OfType<ServerboundPlayerActionPacket>().ToArray();
            Assert.Equal(2, actions.Length);
            Assert.Equal(protocol == 777 ? 2 : 1, actions[1].Action);
            Assert.Equal(Fixture.Target, actions[1].Position);
            Assert.Equal((byte)Direction.Down, actions[1].Direction);
            Assert.Same(actions[1], fixture.Packets[^1]);
            OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(error.CancellationToken.IsCancellationRequested);
            Assert.False(caller.IsCancellationRequested);
            int count = fixture.Packets.Count;
            await fixture.TickAsync();
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
    [MemberData(nameof(Protocols))]
    public async Task ChangingFaceKeepsProgressAndUsesOnlyTheNativeDirectionPacket(int protocol)
    {
        await using var fixture = new Fixture(protocol);
        fixture.SetBlock("bedrock");
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockInViewAsync(Fixture.Target, ct: caller.Token);
        try
        {
            await fixture.TickAsync();
            fixture.ViewHit = BlockHitResult.FromHit(Fixture.Target, Direction.East, Fixture.Target.Center, 2);
            await fixture.TickAsync();
            ServerboundPlayerActionPacket[] actions = fixture.Packets.OfType<ServerboundPlayerActionPacket>().ToArray();
            Assert.Equal(protocol == 777 ? 2 : 1, actions.Length);
            if (protocol == 777)
            {
                Assert.Equal(1, actions[1].Action);
                Assert.Equal((byte)Direction.East, actions[1].Direction);
                Assert.Equal(0, actions[1].Sequence);
                Assert.Equal(Fixture.Target, actions[1].Position);
            }
            Assert.False(mining.IsCompleted);
            await fixture.TickAsync();
            Assert.Equal(actions.Length, fixture.Packets.OfType<ServerboundPlayerActionPacket>().Count());
        }
        finally
        {
            caller.Cancel();
            if (!mining.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task FinishingOnANewFaceUsesThatFaceWithoutAnExtraDirectionPacket(int protocol)
    {
        await using var fixture = new Fixture(protocol);
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockInViewAsync(Fixture.Target, ct: caller.Token);
        try
        {
            for (int tick = 0; tick < 150; tick++)
                await fixture.TickAsync();
            Assert.False(mining.IsCompleted);
            fixture.ViewHit = BlockHitResult.FromHit(Fixture.Target, Direction.East, Fixture.Target.Center, 2);
            await fixture.TickAsync();

            ServerboundPlayerActionPacket[] actions = fixture.Packets.OfType<ServerboundPlayerActionPacket>().ToArray();
            Assert.Equal(2, actions.Length);
            Assert.Equal(protocol == 777 ? 3 : 2, actions[1].Action);
            Assert.Equal((byte)Direction.East, actions[1].Direction);
            await mining.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            caller.Cancel();
            if (!mining.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task ExplicitTargetMiningDoesNotConsumeViewInput()
    {
        await using var fixture = new Fixture();
        fixture.SetBlock("bedrock");
        fixture.ViewHit = null;
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.West, caller.Token);
        try
        {
            await fixture.TickAsync();
            await fixture.TickAsync();
            Assert.Equal((byte)Direction.West, Assert.Single(fixture.Packets.OfType<ServerboundPlayerActionPacket>()).Direction);
            Assert.False(mining.IsCompleted);
        }
        finally
        {
            caller.Cancel();
            if (!mining.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task ABlockOutsideTheInitialViewNeverStartsAnAttack()
    {
        await using var fixture = new Fixture();
        fixture.ViewHit = null;
        Task mining = fixture.Actions.DigBlockInViewAsync(Fixture.Target);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
        await fixture.TickAsync();
        Assert.Empty(fixture.Packets);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task InvalidViewDistanceNeverQueuesMining(double distance)
    {
        await using var fixture = new Fixture();
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = fixture.Actions.DigBlockInViewAsync(Fixture.Target, distance); });
        await fixture.TickAsync();
        Assert.Empty(fixture.Packets);
    }
}
