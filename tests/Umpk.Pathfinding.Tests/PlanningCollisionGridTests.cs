using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests;

public sealed class PlanningCollisionGridTests
{
    [Theory]
    [InlineData(false, BlockCollisionQueryEra.QueryFirst)]
    [InlineData(true, BlockCollisionQueryEra.QueryFirst)]
    [InlineData(false, BlockCollisionQueryEra.QueryFirstStrict)]
    [InlineData(true, BlockCollisionQueryEra.QueryFirstStrict)]
    public void SelectingAnEraPreservesCapturedTerrainAndDefersDemandReads(bool demand, BlockCollisionQueryEra era)
    {
        var fixture = new FixtureWorld();
        var position = new BlockPos(0, 61, 0);
        fixture.Set(position.X, position.Y, position.Z, FixtureWorld.Bamboo);
        PlanningWorldView original = demand ? fixture.CaptureOnDemand(position, position) : fixture.Capture(position, position);
        int sections = original.Region.SectionsCaptured;
        PlanningWorldView configured = original.WithCollisionQueryEra(era);

        Assert.Same(original.Region, configured.Region);
        Assert.Same(original.Shapes, configured.Shapes);
        Assert.Equal(sections, configured.Region.SectionsCaptured);
        Assert.Equal(BlockCollisionQueryEra.BoundingBoxes, original.CollisionQueryEra);
        Assert.Equal(era, configured.CollisionQueryEra);
        Assert.Equal(FixtureWorld.Bamboo, configured.GetBlock(position).StateId);
        fixture.Set(position.X, position.Y, position.Z, FixtureWorld.Air);
        Assert.Equal(FixtureWorld.Bamboo, configured.GetBlock(position).StateId);
    }

    [Theory]
    [InlineData(BlockCollisionQueryEra.QueryFirst)]
    [InlineData(BlockCollisionQueryEra.QueryFirstStrict)]
    [InlineData(BlockCollisionQueryEra.Modern)]
    public void SourcesWithoutGridsRetainTheirBoundingBoxFallback(BlockCollisionQueryEra era)
    {
        var fixture = new FixtureWorld();
        var position = new BlockPos(0, 61, 0);
        fixture.Set(0, 61, 0, FixtureWorld.Stone);
        PlanningWorldView view = fixture.Capture(position, position).WithCollisionQueryEra(era);
        var query = new PhysicsCollisionQuery(view);

        Assert.Null(view.GetCollisionGrid(view.GetBlock(position)));
        Assert.False(query.NoCollision(new Vec3d(.5, 62, .5), new Aabb(.1, 61.1, .1, .9, 61.9, .9)));
        Assert.True(query.NoCollision(new Vec3d(.5, 62, .5), new Aabb(3.1, 61.1, .1, 3.9, 61.9, .9)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void UndefinedCollisionErasAreRejected(int era)
    {
        var fixture = new FixtureWorld();
        var position = new BlockPos(0, 61, 0);
        PlanningWorldView view = fixture.Capture(position, position);

        Assert.Throws<ArgumentOutOfRangeException>(() => view.WithCollisionQueryEra((BlockCollisionQueryEra)era));
    }

    [Fact]
    public void CapturedPhysicsViewPreservesTheOptionalGridCapability()
    {
        var fixture = new FixtureWorld();
        fixture.Set(0, 61, 0, FixtureWorld.Bamboo);
        PlanningWorldView original = fixture.Capture(new BlockPos(0, 61, 0), new BlockPos(0, 61, 0));
        var shapes = new GridOnlyShapes();
        IPhysicsWorldView view = new PlanningWorldView(original.Region, shapes);

        Assert.Same(shapes.Grid, view.GetCollisionGrid(view.GetBlock(new BlockPos(0, 61, 0))));
    }

    [Theory]
    [InlineData(LateralQuantum.NearFace, false)]
    [InlineData(LateralQuantum.FarFace, true)]
    public void LaneGeometryUsesTheExactGridWhenTheBoxTableHasNoEntry(LateralQuantum lateral, bool clear)
    {
        var fixture = new FixtureWorld();
        fixture.Set(0, 61, 0, FixtureWorld.Bamboo);
        PlanningWorldView original = fixture.Capture(new BlockPos(-1, 61, 0), new BlockPos(0, 61, 0));
        var view = new PlanningWorldView(original.Region, new GridOnlyShapes());
        var context = new CalculationContext(view, PathfinderOptions.Default);

        Assert.Equal(clear, SqueezeLane.IsClear(context, -1, 61, 0, 1, 0, lateral, lateral));
    }

    private sealed class GridOnlyShapes : IBlockCollisionGridSource
    {
        internal BlockCollisionGrid Grid { get; } = BlockCollisionGrid.CreateWithHorizontalOffset([.40625, .59375], [0, 1], [.40625, .59375], [0], [new(.40625, 0, .40625, .59375, 1, .59375)], .25);

        public BlockCollisionGrid? GetCollisionGrid(BlockState state) => state.StateId == FixtureWorld.Bamboo ? Grid : null;
        public ReadOnlySpan<Aabb> GetCollisionShapes(BlockState state) => [];
        public ReadOnlySpan<Aabb> GetCollisionShapes(int stateId) => [];
        public ReadOnlySpan<Aabb> GetOutlineShapes(BlockState state) => [];
        public ReadOnlySpan<Aabb> GetOutlineShapes(int stateId) => [];
    }
}
