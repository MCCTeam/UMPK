using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class WorldTickLifetimeTests
{
    public static IEnumerable<object[]> VersionsAndFeatures() =>
        from protocol in Enumerable.Range(765, 13)
        from terrain in new[] { false, true }
        select new object[] { protocol, terrain };

    [Theory]
    [MemberData(nameof(VersionsAndFeatures))]
    public async Task TickControlFollowsTheLevelLifetimeWithOrWithoutTerrain(int protocol, bool terrain)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        var harness = new ApplierHarness(version, new ClientFeatures { Terrain = terrain, Physics = false, Pathfinding = false });
        Assert.True(await harness.TryApplyAsync(new ClientboundTickingStatePacket(1, true)));
        Assert.True(await harness.TryApplyAsync(new ClientboundTickingStepPacket(3)));
        AssertDefault(harness.State.WorldTicks);

        Assert.True(WorldFactory.InstallWorld(harness.State, new CommonWorldSetup("minecraft:overworld", 0), protocol));
        Assert.Equal(terrain, harness.State.WorldOrNull is not null);
        await harness.ApplyAsync(new ClientboundTickingStatePacket(0.5f, true));
        await harness.ApplyAsync(new ClientboundTickingStepPacket(2));
        WorldTickState ticks = harness.State.WorldTicks;
        ticks.BeginTick();
        Assert.True(ticks.RunsNormally);
        Assert.Equal(1, ticks.FrozenTicksToRun);
        await harness.ApplyAsync(new ClientboundTickingStatePacket(10, true));
        Assert.Equal(1, ticks.FrozenTicksToRun);
        await harness.ApplyAsync(new ClientboundTickingStepPacket(0));
        Assert.True(ticks.RunsNormally);
        ticks.BeginTick();
        Assert.False(ticks.RunsNormally);

        Assert.False(WorldFactory.InstallWorld(harness.State, new CommonWorldSetup("minecraft:overworld", 0) { IsRespawn = true }, protocol));
        Assert.Equal(10, ticks.TickRate);
        Assert.True(ticks.IsFrozen);
        Assert.False(ticks.RunsNormally);
        Assert.True(WorldFactory.InstallWorld(harness.State, new CommonWorldSetup("minecraft:the_nether", 0) { IsRespawn = true }, protocol));
        AssertDefault(ticks);

        await harness.ApplyAsync(new ClientboundTickingStatePacket(1, true));
        await harness.ApplyAsync(new ClientboundTickingStepPacket(5));
        harness.State.ResetForConfigurationReentry();
        AssertDefault(ticks);
        await harness.ApplyAsync(new ClientboundTickingStatePacket(1, true));
        await harness.ApplyAsync(new ClientboundTickingStepPacket(5));
        AssertDefault(ticks);
        Assert.True(WorldFactory.InstallWorld(harness.State, new CommonWorldSetup("minecraft:overworld", 0), protocol));
        await harness.ApplyAsync(new ClientboundTickingStatePacket(1, true));
        await harness.ApplyAsync(new ClientboundTickingStepPacket(5));
        ticks.BeginTick();
        harness.State.ResetForSessionEnd();
        AssertDefault(ticks);
    }

    private static void AssertDefault(WorldTickState ticks)
    {
        Assert.Equal(20, ticks.TickRate);
        Assert.Equal(50_000_000, ticks.NanosecondsPerTick);
        Assert.Equal(50, ticks.TargetMilliseconds);
        Assert.False(ticks.IsFrozen);
        Assert.Equal(0, ticks.FrozenTicksToRun);
        Assert.True(ticks.RunsNormally);
    }
}
