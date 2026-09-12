using Umpk.Client.Snapshots;
using Umpk.Client.Tests.Support;
using Umpk.Game.Blocks;
using Umpk.Game.World;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary><see cref="BlockSnapshot"/> contains one block state and records whether its column was loaded. A <see cref="BlockState"/> cannot carry the column-load state by itself.</summary>
public sealed class BlockSnapshotTests
{
    private static readonly BlockPos Pos = new(10, 64, -3);

    [Fact]
    public void LoadedColumn_CarriesTheRealState()
    {
        World world = SnapshotWorldFixture.NewWorld();
        world.SetBlockStateId(Pos, SnapshotWorldFixture.StoneState);
        BlockState state = world.GetBlock(Pos);

        var snapshot = new BlockSnapshot(Pos, state, ChunkLoaded: true);

        Assert.Equal(Pos, snapshot.Position);
        Assert.Equal(SnapshotWorldFixture.StoneState, snapshot.State.StateId);
        Assert.True(snapshot.ChunkLoaded);
    }

    /// <summary>The load-bearing case: <see cref="World.GetBlockStateId"/> returns 0 (air) outside a loaded column, indistinguishable from a real air block on the state alone. A snapshot built for an unloaded column must report IsAir false, not true, and must not throw evaluating the default <see cref="BlockState"/> it necessarily carries.</summary>
    [Fact]
    public void UnloadedColumn_IsNotReportedAsAir()
    {
        World world = SnapshotWorldFixture.NewWorld();
        // Nothing loaded at Pos: GetColumn is null, GetBlock reads the air default.
        BlockState state = world.GetBlock(Pos);
        bool chunkLoaded = world.GetColumn(Pos) is not null;

        var snapshot = new BlockSnapshot(Pos, state, chunkLoaded);

        Assert.False(chunkLoaded);
        Assert.False(snapshot.IsAir);
    }

    [Fact]
    public void UnloadedColumn_ReportsChunkLoadedFalse()
    {
        var snapshot = new BlockSnapshot(Pos, default, ChunkLoaded: false);

        Assert.False(snapshot.ChunkLoaded);
    }

    [Fact]
    public void AirInALoadedColumn_IsReportedAsAir()
    {
        World world = SnapshotWorldFixture.NewWorld();
        world.SetBlockStateId(Pos, SnapshotWorldFixture.AirState);
        BlockState state = world.GetBlock(Pos);

        var snapshot = new BlockSnapshot(Pos, state, world.GetColumn(Pos) is not null);

        Assert.True(snapshot.ChunkLoaded);
        Assert.True(snapshot.IsAir);
    }

    [Theory]
    [InlineData(SnapshotWorldFixture.WaterState, true, false)]
    [InlineData(SnapshotWorldFixture.WaterloggedFenceState, false, true)]
    public void Snapshot_CarriesTheLiveBlockStateFlags(int stateId, bool expectFluid, bool expectWaterlogged)
    {
        World world = SnapshotWorldFixture.NewWorld();
        world.SetBlockStateId(Pos, stateId);
        BlockState state = world.GetBlock(Pos);

        var snapshot = new BlockSnapshot(Pos, state, ChunkLoaded: true);

        Assert.Equal(expectFluid, snapshot.State.IsFluid);
        Assert.Equal(expectWaterlogged, snapshot.State.IsWaterlogged);
    }
}
