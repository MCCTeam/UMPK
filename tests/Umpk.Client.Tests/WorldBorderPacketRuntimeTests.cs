using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.World;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class WorldBorderPacketRuntimeTests
{
    public static IEnumerable<object[]> Protocols => JavaVersions.All.Select(row => row.Version.Protocol).Distinct().Order().Select(protocol => new object[] { protocol });

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task InitializationClampsTheBorderToTheWireAbsoluteMaximum(int protocol)
    {
        ApplierHarness harness = await BoundPacketApplierHarness.JoinedAsync(protocol);
        int changes = 0;
        harness.Events.Subscribe<WorldBorderChanged>(_ => changes++);

        await InitializeAsync(harness, protocol, 99, 100, 100, 0, 100);

        WorldBorderContainmentEra era = WorldBorderState.ContainmentEraForProtocol(protocol);
        Assert.True(harness.State.World.Border.IsWithinBounds(99, 0, era));
        Assert.False(harness.State.World.Border.IsWithinBounds(110, 0, era));
        Assert.Equal(1, changes);
    }

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task InitializationWithoutInterpolationUsesTheNewSize(int protocol)
    {
        ApplierHarness harness = await BoundPacketApplierHarness.JoinedAsync(protocol);

        await InitializeAsync(harness, protocol, 0, 100, 20, 0, 100);

        Assert.Equal(20, harness.State.World.Border.Size);
        Assert.True(harness.State.World.Border.IsWithinBounds(9, 0, WorldBorderState.ContainmentEraForProtocol(protocol)));
        Assert.False(harness.State.World.Border.IsWithinBounds(25, 0, WorldBorderState.ContainmentEraForProtocol(protocol)));
    }

    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(754)]
    [InlineData(755)]
    [InlineData(765)]
    [InlineData(773)]
    public async Task WallTimeUpdatesKeepOriginalInterpolationProgress(int protocol)
    {
        var time = new ManualTimeProvider();
        time.Advance(TimeSpan.FromTicks(10_009_000));
        ApplierHarness harness = await BoundPacketApplierHarness.JoinedAsync(protocol, time);
        await InitializeAsync(harness, protocol, 0, 100, 20, 4, 100);
        Assert.Equal(100, harness.State.World.Border.Size);

        time.Advance(TimeSpan.FromTicks(5_000));
        Assert.Equal(80, harness.State.World.Border.Size);
        IPacket center = protocol switch
        {
            47 => new ClientboundWorldBorderPacket(WorldBorderAction.SetCenter, 3.25, -2, 0, 0, 0, 0, 0, 0),
            < 755 => new ClientboundSetBorderPacket(WorldBorderAction.SetCenter, 3.25, -2, 0, 0, 0, 0, 0, 0),
            _ => new ClientboundSetBorderCenterPacket(3.25, -2),
        };
        await BoundPacketApplierHarness.RoundTripAndApplyAsync(harness, protocol, center.Type.Id.Path, center);
        Assert.Equal(80, harness.State.World.Border.Size);

        time.Advance(TimeSpan.FromTicks(25_000));
        Assert.Equal(40, harness.State.World.Border.Size);
        Assert.Equal(3.25, harness.State.World.Border.CenterX);
        Assert.Equal(-2, harness.State.World.Border.CenterZ);
    }

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task UpdatesPreserveAbsoluteMaximumAndTheVersionClock(int protocol)
    {
        var time = new ManualTimeProvider();
        ApplierHarness harness = await BoundPacketApplierHarness.JoinedAsync(protocol, time);
        await InitializeAsync(harness, protocol, 0, 100, 100, 0, 100);
        foreach (WorldBorderAction action in new[] { WorldBorderAction.SetCenter, WorldBorderAction.SetWarningTime, WorldBorderAction.SetWarningBlocks, WorldBorderAction.SetSize, WorldBorderAction.LerpSize })
        {
            IPacket packet = protocol switch
            {
                47 => new ClientboundWorldBorderPacket(action, 99, 0, 100, 20, 4, 0, 17, 7),
                < 755 => new ClientboundSetBorderPacket(action, 99, 0, 100, 20, 4, 0, 17, 7),
                _ => action switch
                {
                    WorldBorderAction.SetCenter => new ClientboundSetBorderCenterPacket(99, 0),
                    WorldBorderAction.SetWarningTime => new ClientboundSetBorderWarningDelayPacket(17),
                    WorldBorderAction.SetWarningBlocks => new ClientboundSetBorderWarningDistancePacket(7),
                    WorldBorderAction.SetSize => new ClientboundSetBorderSizePacket(20),
                    _ => new ClientboundSetBorderLerpSizePacket(100, 20, 4),
                },
            };
            await BoundPacketApplierHarness.RoundTripAndApplyAsync(harness, protocol, packet.Type.Id.Path, packet);
            Assert.Equal(100, harness.State.World.Border.AbsoluteMaxSize);
            Assert.Equal(WorldBorderState.InterpolationClockForProtocol(protocol), harness.State.World.Border.InterpolationClock);
            Assert.False(harness.State.World.Border.IsWithinBounds(110, 0, WorldBorderState.ContainmentEraForProtocol(protocol)));
        }

        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(protocol < 774 ? 80 : 100, harness.State.World.Border.Size);
        Assert.Equal(17, harness.State.World.Border.WarningTimeSeconds);
        Assert.Equal(7, harness.State.World.Border.WarningBlocks);
    }

    private static async Task InitializeAsync(ApplierHarness harness, int protocol, double center, double oldSize, double newSize, long duration, int absolute)
    {
        IPacket packet = protocol switch
        {
            47 => new ClientboundWorldBorderPacket(WorldBorderAction.Initialize, center, 0, oldSize, newSize, duration, absolute, 15, 5),
            < 755 => new ClientboundSetBorderPacket(WorldBorderAction.Initialize, center, 0, oldSize, newSize, duration, absolute, 15, 5),
            _ => new ClientboundInitializeBorderPacket(center, 0, oldSize, newSize, duration, absolute, 5, 15),
        };
        string identifier = protocol is > 47 and < 755 ? "set_border" : packet.Type.Id.Path;
        await BoundPacketApplierHarness.RoundTripAndApplyAsync(harness, protocol, identifier, packet);
    }
}
