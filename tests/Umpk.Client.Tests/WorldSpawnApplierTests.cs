using Umpk.Client.Events;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>The world spawn survives arriving before the world exists.</summary>
/// <remarks><c>set_default_spawn_position</c> is routinely sent inside the join sequence, and the terrain applier gated EVERY packet it saw behind "the world has been built". An early spawn announcement was therefore dropped outright: no state was written (there was none to write) and the <c>SpawnPositionChanged</c> event was never published either, which is indistinguishable from a server that never sent one. The spawn is a server fact rather than terrain, so it is now applied ahead of that gate and recorded on <see cref="ServerState.WorldSpawn"/>.</remarks>
public sealed class WorldSpawnApplierTests
{
    private static JavaVersion Version(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version), $"unknown protocol {protocol}");
        return version!;
    }

    /// <summary>A spawn that arrives before join remains pending until the world exists.</summary>
    [Theory]
    [InlineData(47)]
    [InlineData(340)]
    [InlineData(754)]
    [InlineData(770)]
    [InlineData(776)]
    public async Task SpawnPosition_ArrivingBeforeJoin_IsKept(int protocol)
    {
        var harness = new ApplierHarness(Version(protocol));
        SpawnPositionChanged? seen = null;
        harness.Events.Subscribe<SpawnPositionChanged>(e => seen = e);

        // No world yet: nothing has built one, which is exactly the join-sequence ordering.
        Assert.Null(harness.State.WorldOrNull);

        var position = new BlockPos(128, 70, -256);
        await harness.ApplyAsync(new ClientboundSetDefaultSpawnPositionPacket(position, 90f, Dimension: null, Pitch: 0f));

        Assert.NotNull(harness.State.Server.WorldSpawn);
        Assert.Equal(position, harness.State.Server.WorldSpawn!.Value.Position);
        Assert.Equal(90f, harness.State.Server.WorldSpawn.Value.Angle);

        Assert.NotNull(seen);
        Assert.Equal(position, seen!.Position);
        Assert.Equal(90f, seen.Angle);
    }

    /// <summary>The same packet remains reachable after join.</summary>
    [Fact]
    public async Task SpawnPosition_ArrivingAfterJoin_IsKept()
    {
        var harness = new ApplierHarness(Version(770));
        await JoinAsync(harness);
        Assert.NotNull(harness.State.WorldOrNull);

        var position = new BlockPos(8, 64, 8);
        await harness.ApplyAsync(new ClientboundSetDefaultSpawnPositionPacket(position, 0f, Dimension: null, Pitch: 0f));

        Assert.Equal(position, harness.State.Server.WorldSpawn!.Value.Position);
    }

    /// <summary>A later announcement replaces the earlier one, as vanilla's compass does.</summary>
    [Fact]
    public async Task SpawnPosition_IsReplacedByALaterAnnouncement()
    {
        var harness = new ApplierHarness(Version(770));

        await harness.ApplyAsync(new ClientboundSetDefaultSpawnPositionPacket(new BlockPos(1, 1, 1), 0f, Dimension: null, Pitch: 0f));
        await harness.ApplyAsync(new ClientboundSetDefaultSpawnPositionPacket(new BlockPos(2, 2, 2), 45f, Dimension: null, Pitch: 0f));

        Assert.Equal(new BlockPos(2, 2, 2), harness.State.Server.WorldSpawn!.Value.Position);
        Assert.Equal(45f, harness.State.Server.WorldSpawn.Value.Angle);
    }

    /// <summary>Unset until a server announces one, so null is honest rather than a zero position.</summary>
    [Fact]
    public void SpawnPosition_IsNullBeforeAnyAnnouncement()
    {
        var harness = new ApplierHarness(Version(770));
        Assert.Null(harness.State.Server.WorldSpawn);
    }

    private static async Task JoinAsync(ApplierHarness harness)
    {
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);
        await harness.ApplyAsync(new ClientboundLoginPacket(
            PlayerId: 1, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false, Legacy: null));
    }
}
