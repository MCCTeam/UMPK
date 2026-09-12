using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests;

public sealed class SqueezeLaneGeometryTests
{
    private const int FloorY = 60;

    private const int FeetY = 61;

    /// <summary>A one-wide walled lane along X at <c>z = 0</c>, with whatever is asked for at <paramref name="obstacleX"/>.</summary>
    private static FixtureWorld Lane(int obstacleX, int obstacleState, int height = 3)
    {
        var world = new FixtureWorld();
        world.Fill(-1, FloorY, -1, 11, FloorY, 1, FixtureWorld.Stone);
        world.Fill(-1, FeetY, -1, 11, FeetY + 2, -1, FixtureWorld.Stone);
        world.Fill(-1, FeetY, 1, 11, FeetY + 2, 1, FixtureWorld.Stone);
        world.Fill(-1, FeetY, 0, -1, FeetY + 2, 0, FixtureWorld.Stone);
        world.Fill(11, FeetY, 0, 11, FeetY + 2, 0, FixtureWorld.Stone);
        world.Fill(obstacleX, FeetY, 0, obstacleX, FeetY + height - 1, 0, obstacleState);
        return world;
    }

    private static PathStatus Cross(FixtureWorld world)
    {
        CalculationContext ctx = FixtureContext.Build(world, new BlockPos(0, FeetY, 0), new BlockPos(10, FeetY, 0));
        return new AStarPathFinder().Calculate(
            ctx, 0, FeetY, 0, new GoalBlock(10, FeetY, 0), CancellationToken.None,
            nodeBudget: 20_000, timeout: TimeSpan.FromSeconds(10), TimeProvider.System).Status;
    }

    /// <summary><b>Dripstone is not in scope, and it is refused by the ARITHMETIC rather than by a name.</b> The upward-tip shape spans <c>[0.3125, 0.6875]</c> and moves by at most 0.125, so the widest a face can be from the box is <c>0.3125 + 0.125 = 0.4375</c>, under the 0.6 a flush body needs. Every thickness is worse: base is 12/16 and the two mid states 10/16 and 14/16. The census's 0.00% is this inequality, and it holds at all 256 columns rather than at this one.</summary>
    [Theory]
    [InlineData(FixtureWorld.DripstoneTipUp)]
    [InlineData(FixtureWorld.DripstoneBaseUp)]
    public void ADripstoneColumn_IsNeverSqueezedPast(int state)
    {
        Assert.NotEqual(PathStatus.Success, Cross(Lane(5, state)));
    }

    /// <summary>The same inequality, asserted over the whole 16x16 offset grid instead of at one column, so the row above cannot pass merely because <c>(5, 0)</c> happened to be unlucky.</summary>
    [Fact]
    public void NoColumnAnywhere_AdmitsAFlushBodyBesideADripstone()
    {
        // SHAPE_TIP_UP = column-shape construction, i.e. [0.3125, 0.6875] horizontally, and max horizontal offset behavior is the base shape inset = 0.125.
        const double BoxMin = 0.3125;
        const double BoxMax = 0.6875;
        const double MaxOffset = 0.125;

        // A flush body spans [0, 0.6] or [0.4, 1.0], so it clears the box only if the box's near face is at or beyond 0.6, or its far face at or before 0.4.
        double offsetNeededLow = PhysicsConstants.PlayerWidth - BoxMin;
        double offsetNeededHigh = BoxMax - (1.0 - PhysicsConstants.PlayerWidth);

        Assert.Equal(0.2875, offsetNeededLow, 12);
        Assert.Equal(0.2875, offsetNeededHigh, 12);
        Assert.True(
            Math.Min(offsetNeededLow, offsetNeededHigh) > MaxOffset,
            $"a dripstone would need |offset| {offsetNeededLow} to leave a flush lane, and vanilla "
                + $"clamps it to {MaxOffset}; that is the census's 0.00%, at all 256 columns");

        // And bamboo, the same arithmetic, is the other side of it: 0.19375 needed, 0.25 available.
        Assert.True(PhysicsConstants.PlayerWidth - 0.40625 < 0.25);
    }

    [Fact]
    public void AClosedIronDoorPanel_IsNotSqueezedAlong()
    {
        // IRON specifically. A hand-openable door is admitted by MoveHelper.CanWalkThrough itself - the plan is allowed to open it and pays ActionCosts.InteractLatency at the edge that does - so its cell never reaches the squeeze arm and a lane past its panel is the ordinary, correct answer. Measured: a plain closed door answers IsClear=true at Centre for exactly that reason. Only a door the plan may NOT open is a wall, and only a wall tests this arm.
        const int state = FixtureWorld.IronDoorClosed;

        // A lane running along Z, so the door's 3/16 X plate is on the PERPENDICULAR axis - the one a lateral can move along, and the only orientation where the geometry would say yes.
        var world = new FixtureWorld();
        world.Fill(-1, FloorY, -1, 1, FloorY, 11, FixtureWorld.Stone);
        world.Fill(-1, FeetY, -1, -1, FeetY + 2, 11, FixtureWorld.Stone);
        world.Fill(1, FeetY, -1, 1, FeetY + 2, 11, FixtureWorld.Stone);
        world.Fill(0, FeetY, 5, 0, FeetY + 1, 5, state);

        CalculationContext ctx = FixtureContext.Build(world, new BlockPos(0, FeetY, 0), new BlockPos(0, FeetY, 10));

        // The premise: the panel really does leave a flush body 13/16 of clear run.
        Aabb panel = ctx.World.GetCollisionShapes(ctx.GetBlock(0, FeetY, 5))[0];
        Assert.True(panel.MaxX <= 1.0 - PhysicsConstants.PlayerWidth,
            $"the fixture door's panel [{panel.MinX}, {panel.MaxX}] must clear a FarFace body's 0.4");

        foreach (LateralQuantum q in new[] { LateralQuantum.NearFace, LateralQuantum.Centre, LateralQuantum.FarFace })
            Assert.False(
                SqueezeLane.IsClear(ctx, 0, FeetY, 4, 0, 1, LateralQuantum.Centre, q),
                $"a closed door must not be squeezed ALONG at {q}, however open its panel's far side is");

    }

    [Theory]
    [InlineData(FixtureWorld.Cactus)]
    [InlineData(FixtureWorld.MagmaBlock)]
    public void AHazard_IsNotSqueezedPast(int state)
    {
        Assert.NotEqual(PathStatus.Success, Cross(Lane(5, state, height: 1)));
    }

    /// <summary><b>Lava has no collision box at all</b>, so a purely geometric lane would read it as open air and walk a body straight into it. Caught by the hazard arm here rather than by the motion-blocking one, which is worth knowing: ablating the motion-blocking arm leaves this row green, and <see cref="BootedPowderSnow_IsNotSqueezedThrough"/> is what catches that instead.</summary>
    [Fact]
    public void Lava_IsNotSqueezedThrough()
    {
        FixtureWorld world = Lane(5, FixtureWorld.Lava, height: 2);
        CalculationContext ctx = FixtureContext.Build(world, new BlockPos(0, FeetY, 0), new BlockPos(10, FeetY, 0));

        Assert.Empty(ctx.World.GetCollisionShapes(ctx.GetBlock(5, FeetY, 0)).ToArray());

        foreach (LateralQuantum q in new[] { LateralQuantum.NearFace, LateralQuantum.Centre, LateralQuantum.FarFace })
            Assert.False(
                SqueezeLane.IsClear(ctx, 4, FeetY, 0, 1, 0, LateralQuantum.Centre, q),
                $"lava has no box at all and must not read as an open lane at {q}");

        Assert.NotEqual(PathStatus.Success, Cross(world));
    }

    [Fact]
    public void BootedPowderSnow_IsNotSqueezedThrough()
    {
        FixtureWorld world = Lane(5, FixtureWorld.PowderSnow, height: 2);
        var booted = new PathfinderCapabilities
        {
            EffectsKnown = false,
            InventoryKnown = true,
            VitalsKnown = false,
            Items = [new CapabilityItem(Identifier.Minecraft("leather_boots"), Count: 1, MenuSlot: 8, EnchantmentReadout.None)],
        };

        CalculationContext ctx = FixtureContext.Build(
            world, new BlockPos(0, FeetY, 0), new BlockPos(10, FeetY, 0), capabilities: booted);

        // The premise, all four parts, so the row cannot pass because some other arm caught it.
        Assert.True(ctx.PowderSnowWalkable);
        Assert.False(MoveHelper.IsHazardAt(ctx, 5, FeetY, 0));
        Assert.False(MoveHelper.CanWalkThrough(ctx, 5, FeetY, 0));
        Assert.Empty(ctx.World.GetCollisionShapes(ctx.GetBlock(5, FeetY, 0)).ToArray());

        foreach (LateralQuantum q in new[] { LateralQuantum.NearFace, LateralQuantum.Centre, LateralQuantum.FarFace })
            Assert.False(
                SqueezeLane.IsClear(ctx, 4, FeetY, 0, 1, 0, LateralQuantum.Centre, q),
                $"powder snow has no box for a body inside it and must not read as a lane at {q}");

    }

    /// <summary><b>The flanking columns cannot change an answer, and here is the proof rather than the claim.</b> §B.3 asks for the destination's two perpendicular neighbours to be projected as well. The lane fixture already packs both flanks with stone from floor to ceiling - it is a WALLED lane - and the threadable row passes anyway, because a flush body's band is contained in its own cell and a collision box is contained in its own cell, so the two can touch and can never overlap. This row says it directly: the identical geometry with the walls made of the widest possible boxes behaves identically.</summary>
    [Fact]
    public void ASolidFlank_DoesNotCloseTheLane()
    {
        FixtureWorld world = Lane(5, FixtureWorld.Bamboo);

        // The flanks are already solid stone. Assert the body really is flush against them: at the NearFace lateral its low face is at exactly the cell boundary the wall starts on.
        Assert.Equal(-0.2, SqueezeLane.Offset(LateralQuantum.NearFace), 12);
        Assert.Equal(0.5 - (PhysicsConstants.PlayerWidth / 2.0), SqueezeLane.LateralOffset, 12);

        Assert.Equal(PathStatus.Success, Cross(world));
    }

    /// <summary>A lane along X at <c>z = -3</c>, where the offset hash lays out a matched pair of cases four cells apart.</summary>
    /// <remarks>
    /// Measured from <c>BlockShapeOffset</c> itself, and pinned by <see cref="TheZMinus3Lane_HasTheOffsetsTheseRowsClaim"/> so a change to the hash cannot leave these rows passing for the wrong reason:
    /// <code>
    ///   x=16  oz=-0.25000  box=[0.15625,0.34375]  lane=FarFace
    ///   x=17  oz=-0.21667  box=[0.18958,0.37708]  lane=FarFace
    ///   x=18  oz=+0.25000  box=[0.65625,0.84375]  lane=NearFace
    /// </code>
    /// </remarks>
    private static FixtureWorld SwapLane(params int[] postXs)
    {
        var world = new FixtureWorld();
        world.Fill(12, FloorY, -4, 24, FloorY, -2, FixtureWorld.Stone);
        world.Fill(12, FeetY, -4, 24, FeetY + 3, -4, FixtureWorld.Stone);
        world.Fill(12, FeetY, -2, 24, FeetY + 3, -2, FixtureWorld.Stone);
        world.Fill(12, FeetY, -3, 12, FeetY + 3, -3, FixtureWorld.Stone);
        world.Fill(24, FeetY, -3, 24, FeetY + 3, -3, FixtureWorld.Stone);
        foreach (int x in postXs)
            world.Fill(x, FeetY, -3, x, FeetY + 2, -3, FixtureWorld.Bamboo);

        return world;
    }

    private static PathStatus CrossSwapLane(FixtureWorld world)
    {
        CalculationContext ctx = FixtureContext.Build(world, new BlockPos(13, FeetY, -3), new BlockPos(23, FeetY, -3));
        return new AStarPathFinder().Calculate(
            ctx, 13, FeetY, -3, new GoalBlock(23, FeetY, -3), CancellationToken.None,
            nodeBudget: 20_000, timeout: TimeSpan.FromSeconds(10), TimeProvider.System).Status;
    }

    /// <summary>The premise both swap rows rest on, so neither can pass for the wrong reason.</summary>
    [Theory]
    [InlineData(16, -0.25)]
    [InlineData(17, -0.21666666492819786)]
    [InlineData(18, 0.25)]
    public void TheZMinus3Lane_HasTheOffsetsTheseRowsClaim(int x, double expected)
    {
        Vec3d offset = BlockShapeOffset.For(
            new Umpk.Game.Blocks.BlockState(new FixtureBlockData(), FixtureWorld.Bamboo), new BlockPos(x, FeetY, -3));

        Assert.Equal(expected, offset.Z, 12);
    }

    /// <summary><b>The lateral is real search state, not decoration.</b> Two posts leaning OPPOSITE ways need opposite laterals - <c>(16, -3)</c> is at <c>-0.25</c> so its lane is the far face, and <c>(18, -3)</c> is at <c>+0.25</c> so its lane is the near face - and a body cannot swap sides inside a post's own cell, because the source column is tested over the whole band the shift sweeps. Here <c>(17, -3)</c> is CLEAR, so the swap has somewhere to happen and the route exists.</summary>
    [Fact]
    public void TwoPostsLeaningOppositeWays_AreThreadedBySwappingInTheClearCellBetween()
    {
        Assert.Equal(PathStatus.Success, CrossSwapLane(SwapLane(16, 18)));
    }

    [Fact]
    public void TwoADJACENTPostsLeaningOppositeWays_HaveNowhereToSwapAndMustRefuse()
    {
        Assert.NotEqual(PathStatus.Success, CrossSwapLane(SwapLane(17, 18)));
    }

    /// <summary>The negative half of the pair above: each of those two posts on its own IS threadable, so the refusal is the swap and not either post.</summary>
    [Theory]
    [InlineData(17)]
    [InlineData(18)]
    public void EitherOfTheAdjacentPosts_IsThreadableAlone(int postX)
    {
        Assert.Equal(PathStatus.Success, CrossSwapLane(SwapLane(postX)));
    }

    /// <summary><b>The head cell is read.</b> A one-tall post the body walks OVER is course row <c>O1p</c> and is not this feature at all; a post that fills the head cell is. Removing the head-cell half of the column test would let a body walk with its chest through a stalk, so this row builds a lane whose FEET cell is clear at the flush lateral and whose HEAD cell is not, and requires a refusal.</summary>
    [Fact]
    public void AnObstructionInTheHeadCellAlone_ClosesTheLane()
    {
        FixtureWorld world = Lane(5, FixtureWorld.Bamboo, height: 3);

        // Feet cell open, head cell a full stone block: the flush lane exists at feet height and does not exist at head height, so the column must be refused.
        world.Set(5, FeetY, 0, FixtureWorld.Air);
        world.Set(5, FeetY + 1, 0, FixtureWorld.Stone);

        Assert.NotEqual(PathStatus.Success, Cross(world));
    }

    /// <summary><b>A squeeze needs a full cube underfoot.</b> <c>CanWalkOn</c> asks whether a CENTRED footprint rests on the cell below, which is a different question from whether an off-centre one does, and rather than build an offset footprint query for one move family the squeeze arm demands <c>IsSolid</c>. Conservative on purpose; this row is what would catch its removal, by putting a bottom slab under the post's own cell.</summary>
    [Fact]
    public void ASqueezeOverAPartialFloor_IsRefused()
    {
        FixtureWorld world = Lane(5, FixtureWorld.Bamboo);
        world.Set(5, FloorY, 0, FixtureWorld.BottomSlab);

        Assert.NotEqual(PathStatus.Success, Cross(world));
    }

    [Fact]
    public void AnAscendOutOfALane_IsRefusedWhileTheBodyIsOffCentre()
    {
        var world = new FixtureWorld();
        world.Fill(12, FloorY, -4, 24, FloorY, -2, FixtureWorld.Stone);
        world.Fill(12, FeetY, -4, 24, FeetY + 4, -4, FixtureWorld.Stone);
        world.Fill(12, FeetY, -2, 24, FeetY + 4, -2, FixtureWorld.Stone);
        world.Fill(12, FeetY, -3, 12, FeetY + 4, -3, FixtureWorld.Stone);
        world.Fill(24, FeetY, -3, 24, FeetY + 4, -3, FixtureWorld.Stone);
        world.Fill(16, FeetY, -3, 16, FeetY + 1, -3, FixtureWorld.Bamboo);
        world.Fill(17, FeetY, -3, 24, FeetY, -3, FixtureWorld.Stone);

        CalculationContext ctx = FixtureContext.Build(world, new BlockPos(13, FeetY, -3), new BlockPos(23, FeetY + 1, -3));

        // The premise: the takeoff headroom above the post really is clear, so a centred body's ascend would be offered here.
        Assert.True(ctx.CanWalkThrough(16, FeetY + 2, -3));

        PathResult result = new AStarPathFinder().Calculate(
            ctx, 13, FeetY, -3, new GoalBlock(23, FeetY + 1, -3), CancellationToken.None,
            nodeBudget: 20_000, timeout: TimeSpan.FromSeconds(10), TimeProvider.System);

        Assert.NotEqual(PathStatus.Success, result.Status);
    }

    /// <summary>The negative half, so the row above cannot pass because the STEP is impossible rather than because the body was off centre: the identical step with the post removed is walked.</summary>
    [Fact]
    public void TheSameAscend_IsWalkedWhenNoPostForcedTheBodyOffCentre()
    {
        var world = new FixtureWorld();
        world.Fill(12, FloorY, -4, 24, FloorY, -2, FixtureWorld.Stone);
        world.Fill(12, FeetY, -4, 24, FeetY + 4, -4, FixtureWorld.Stone);
        world.Fill(12, FeetY, -2, 24, FeetY + 4, -2, FixtureWorld.Stone);
        world.Fill(12, FeetY, -3, 12, FeetY + 4, -3, FixtureWorld.Stone);
        world.Fill(24, FeetY, -3, 24, FeetY + 4, -3, FixtureWorld.Stone);
        world.Fill(17, FeetY, -3, 24, FeetY, -3, FixtureWorld.Stone);

        CalculationContext ctx = FixtureContext.Build(world, new BlockPos(13, FeetY, -3), new BlockPos(23, FeetY + 1, -3));
        PathResult result = new AStarPathFinder().Calculate(
            ctx, 13, FeetY, -3, new GoalBlock(23, FeetY + 1, -3), CancellationToken.None,
            nodeBudget: 20_000, timeout: TimeSpan.FromSeconds(10), TimeProvider.System);

        Assert.Equal(PathStatus.Success, result.Status);
    }

    /// <summary><b>The region gate is exact in the direction it is used.</b> A capture with no offset block in it answers false, and a capture with one answers true; the arm short-circuits on the first, so this is what makes the feature free on every existing plan.</summary>
    [Fact]
    public void TheRegionGate_IsFalseWithoutAnOffsetBlockAndTrueWithOne()
    {
        var bare = new FixtureWorld();
        bare.Fill(-1, FloorY, -1, 11, FloorY, 1, FixtureWorld.Stone);
        bare.Fill(5, FeetY, 0, 5, FeetY + 2, 0, FixtureWorld.Fence);
        Assert.False(FixtureContext.Build(bare, new BlockPos(0, FeetY, 0), new BlockPos(10, FeetY, 0))
            .World.MayContainShapeOffset);

        Assert.True(FixtureContext.Build(Lane(5, FixtureWorld.Bamboo), new BlockPos(0, FeetY, 0), new BlockPos(10, FeetY, 0))
            .World.MayContainShapeOffset);
    }

    /// <summary><b>The endpoint really moves.</b> The whole executor-side change is one term in <c>PathSegmentBuilder</c>, and this row reads it back off a built segment: the plan that crosses the post has at least one segment whose Z endpoint is 0.2 off the cell centre, and the plan over the empty lane has none.</summary>
    [Fact]
    public void TheBuiltSegments_CarryTheLateralIntoTheirEndpoints()
    {
        FixtureWorld world = Lane(5, FixtureWorld.Bamboo);
        CalculationContext ctx = FixtureContext.Build(world, new BlockPos(0, FeetY, 0), new BlockPos(10, FeetY, 0));
        PathResult result = new AStarPathFinder().Calculate(
            ctx, 0, FeetY, 0, new GoalBlock(10, FeetY, 0), CancellationToken.None,
            nodeBudget: 20_000, timeout: TimeSpan.FromSeconds(10), TimeProvider.System);

        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments =
            PathSegmentBuilder.FromPath(result.Path, ctx.World, PathfinderOptions.Default);
        List<double> offCentre = segments
            .Select(s => s.End.Z - Math.Floor(s.End.Z) - 0.5)
            .Where(d => Math.Abs(d) > 1.0E-9)
            .ToList();

        Assert.NotEmpty(offCentre);
        Assert.All(offCentre, d => Assert.Equal(-SqueezeLane.LateralOffset, d, 12));
    }
}
