using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Game-event weather mapping: vanilla ids (begin/end raining, rain/thunder level) land in the world model and raise change events.</summary>
public sealed class WeatherApplierTests
{
    private static JavaVersion Version => JavaVersions.V1_21_5;

    private static async Task<ApplierHarness> JoinedHarnessAsync()
    {
        var harness = new ApplierHarness(Version);
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);
        await harness.ApplyAsync(new ClientboundLoginPacket(
            PlayerId: 1, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false, Legacy: null));
        return harness;
    }

    [Fact]
    public async Task RainLevelChange_StoresLevelAndRaisesEvent()
    {
        ApplierHarness harness = await JoinedHarnessAsync();
        RainLevelChanged? raised = null;
        harness.Events.Subscribe<RainLevelChanged>(e => raised = e);

        await harness.ApplyAsync(new ClientboundGameEventPacket(7, 0.5f));

        Assert.Equal(0.5f, harness.State.World.RainLevel);
        Assert.NotNull(raised);
        Assert.Equal(0.5f, raised.Level);
        Assert.True(raised.Raining);
    }

    [Fact]
    public async Task ThunderLevelChange_StoresLevelAndRaisesEvent()
    {
        ApplierHarness harness = await JoinedHarnessAsync();
        ThunderLevelChanged? raised = null;
        harness.Events.Subscribe<ThunderLevelChanged>(e => raised = e);

        await harness.ApplyAsync(new ClientboundGameEventPacket(8, 0.25f));

        Assert.Equal(0.25f, harness.State.World.ThunderLevel);
        Assert.NotNull(raised);
        Assert.Equal(0.25f, raised.Level);
    }

    [Fact]
    public async Task BeginEndRaining_FlipTheFlag()
    {
        ApplierHarness harness = await JoinedHarnessAsync();

        await harness.ApplyAsync(new ClientboundGameEventPacket(1, 0f));
        Assert.True(harness.State.World.IsRaining);

        await harness.ApplyAsync(new ClientboundGameEventPacket(2, 0f));
        Assert.False(harness.State.World.IsRaining);
    }

    [Fact]
    public async Task RepeatLevel_DoesNotRepublish()
    {
        ApplierHarness harness = await JoinedHarnessAsync();
        await harness.ApplyAsync(new ClientboundGameEventPacket(7, 0.5f));

        int count = 0;
        harness.Events.Subscribe<RainLevelChanged>(_ => count++);
        await harness.ApplyAsync(new ClientboundGameEventPacket(7, 0.5f));

        Assert.Equal(0, count);
    }
}
