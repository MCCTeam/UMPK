using System.Linq;
using Umpk.Game.Blocks;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Execution.Templates;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests.Execution;

public sealed class StalagmiteLandingTests
{
    private const int FloorY = 64;

    /// <summary>The tip's own top: <c>SHAPE_TIP_UP = column-shape construction</c>, 11/16.</summary>
    private const double TipTop = 0.6875;

    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(774);

    /// <summary>The declared elevation is the CENTRED rest height and the tip offers it. Landing feasibility must additionally account for whether the arriving body can hold that stance.</summary>
    [Fact]
    public void TheColumn_DeclaresTheTipAsItsElevation()
    {
        (PlanningWorldView view, _) = Column(FixtureWorld.DripstoneTipUp);

        Assert.Equal(
            FloorY + TipTop,
            PathSegmentBuilder.ResolveElevation(view, 3, FloorY + 1, 0));

        // And the neighbours add nothing, so the completion band is the plain +/- tolerance one.
        Assert.Equal(0.0, PathSegmentBuilder.ResolveNeighbourSlack(view, 3, FloorY + 1, 0));
    }

    /// <summary>The mechanism, as a support query rather than as a story: a body CENTRED in the tip's cell rests on the tip, and the same body pressed against the tip's face rests on nothing in that cell.</summary>
    [Fact]
    public void ABodyPressedAgainstTheTip_IsNotOnIt()
    {
        (PlanningWorldView view, _) = Column(FixtureWorld.DripstoneTipUp);
        BlockState tip = view.GetBlock(new BlockPos(3, FloorY, 0));

        Assert.Equal(
            TipTop,
            BlockSupport.FootprintSupportHeight(view.Shapes.GetCollisionShapes(tip), 0.5, 0.5, 0.3));

        // The tip's -X face is at 0.3125. A body whose +X face rests on it is centred at 0.0125, and its footprint then ends exactly where the tip's box begins: overlap is strict, so it is zero.
        Assert.Equal(
            0.0,
            BlockSupport.FootprintSupportHeight(view.Shapes.GetCollisionShapes(tip), 0.0125, 0.5, 0.3));
    }

    [Fact]
    public void AJumpedLanding_DoesNotCompleteBelowItsDeclaredElevation()
    {
        (PathExecutionContext ctx, PathSegment segment) = JumpAtTheTip();

        // The live arm: PrepareJump + OnGround + RequireJumpReady + centre inside the target column.
        Assert.Equal(PathTransitionType.PrepareJump, segment.ExitTransition);
        Assert.True(segment.ExitHints.RequireJumpReady);

        var template = new SprintJumpTemplate(ctx, segment);
        template.Tick(Airborne(1.6, FloorY + 1.1), out _);
        template.Tick(Airborne(2.4, FloorY + 0.9), out _);
        TemplateState landed = template.Tick(BesideTheTip(), out _);

        Assert.NotEqual(TemplateState.Complete, landed);
    }

    /// <summary>The same three ticks complete with the body ON the tip, proving the rejection is about landing elevation rather than an unreachable execution arm.</summary>
    [Fact]
    public void AJumpedLanding_StillCompletesOnTheTip()
    {
        (PathExecutionContext ctx, PathSegment segment) = JumpAtTheTip();

        var template = new SprintJumpTemplate(ctx, segment);
        template.Tick(Airborne(1.6, FloorY + 1.4), out _);
        template.Tick(Airborne(2.4, FloorY + 1.0), out _);
        TemplateState landed = template.Tick(OnTheTip(), out _);

        Assert.Equal(TemplateState.Complete, landed);
    }

    /// <summary>A jump may not target a floor that is absent under some possible landing stances. The tip is the only support in this list that fails: a stair's raised octet does not span the cell either, but its base slab does, so the body always lands on SOMETHING there.</summary>
    [Theory]
    [InlineData(FixtureWorld.Stone, true, "a full cube holds a body anywhere in the cell")]
    [InlineData(FixtureWorld.BottomSlab, true, "0.5 over the whole footprint")]
    [InlineData(FixtureWorld.SnowLayer, true, "0.375 over the whole footprint")]
    [InlineData(FixtureWorld.Honey, true, "inset 1/16 a side, still under every stance")]
    [InlineData(FixtureWorld.StairsBottom, true, "the base slab is full-cell; only the octet is not")]
    [InlineData(FixtureWorld.DripstoneBaseUp, true, "12/16 wide: still under every stance")]
    [InlineData(FixtureWorld.DripstoneTipUp, false, "6/16 wide, inset 5/16: a corner stance misses it")]
    public void CanLandOn_IsGuaranteedFloorAndNotCentredFloor(int stateId, bool landable, string why)
    {
        var world = new FixtureWorld();
        world.Set(0, FloorY, 0, stateId);
        CalculationContext ctx = FixtureContext.Around(world, 0, FloorY, 0);

        Assert.Equal(landable, MoveHelper.CanLandOn(ctx, 0, FloorY, 0));

        // Every listed support remains WALKABLE; only jump landability differs.
        Assert.True(MoveHelper.CanWalkOn(ctx, 0, FloorY, 0));
        Assert.False(string.IsNullOrEmpty(why));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1)]
    public void ThePlanner_DoesNotUseTheTipAsAJumpSteppingStone(int dy)
    {
        (PlanningWorldView tipView, _) = Column(FixtureWorld.DripstoneTipUp, dy);
        PathResult overTheTip = PathPlanner.FindPath(
            tipView,
            PathfinderOptions.Default,
            new BlockPos(0, FloorY + 1, 0),
            new GoalBlock(new BlockPos(6, FloorY + dy + 1, 0)));

        // Include the plan in the failure message because every expansion arm must reject the tip, including a sidewall route into the same cell.
        string plan = string.Join(" | ", overTheTip.Path.Select(n => $"({n.X},{n.Y},{n.Z}) {n.MoveUsed}"));
        Assert.False(overTheTip.Path.Any(node => node.X == 3 && node.Z == 0), plan);

        // The control: the same geometry with a stone pillar in the tip's place is still crossed, so the refusal above is about the SHAPE and not about the gap being unjumpable.
        (PlanningWorldView stoneView, _) = Column(FixtureWorld.Stone, dy);
        PathResult overTheStone = PathPlanner.FindPath(
            stoneView,
            PathfinderOptions.Default,
            new BlockPos(0, FloorY + 1, 0),
            new GoalBlock(new BlockPos(6, FloorY + dy + 1, 0)));

        Assert.Equal(PathStatus.Success, overTheStone.Status);
        Assert.Contains(overTheStone.Path, node => node.X == 3 && node.Z == 0);
    }

    /// <summary>Course row O7 distinguishes jump landability from ordinary walkability: every listed surface, including the tip, remains walkable. Only jump landing is restricted.</summary>
    [Theory]
    [InlineData(FixtureWorld.DripstoneTipUp)]
    [InlineData(FixtureWorld.DripstoneBaseUp)]
    [InlineData(FixtureWorld.StairsBottom)]
    public void ADeckOfThese_IsStillWalkable(int stateId)
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, -1, 6, FloorY, 1, stateId);
        CalculationContext ctx = FixtureContext.Around(world, 3, FloorY, 0);

        for (int x = 0; x <= 6; x++)
            Assert.True(MoveHelper.CanWalkOn(ctx, x, FloorY, 0), $"x={x}");

    }

    private static (PlanningWorldView View, FixtureWorld World) Column(int supportState, int dy = 0)
    {
        var world = new FixtureWorld();
        world.Fill(-6, FloorY, -2, 0, FloorY, 2, FixtureWorld.Stone);
        for (int y = FloorY - 1; y < FloorY + dy; y++)
            world.Set(3, y, 0, FixtureWorld.Stone);

        world.Set(3, FloorY + dy - 1, 0, FixtureWorld.Stone);
        world.Set(3, FloorY + dy, 0, supportState);
        world.Set(6, FloorY + dy, 0, FixtureWorld.Stone);
        PlanningWorldView view = world.Capture(
            new BlockPos(-8, FloorY - 2, -4), new BlockPos(9, FloorY + 3, 4), margin: 6);
        return (view, world);
    }

    /// <summary>The owner's own shape of plan: a jump onto the tip's column, and another jump straight out of it, which is what makes the first segment's exit a <c>PrepareJump</c> rather than a stop.</summary>
    private static (PathExecutionContext Ctx, PathSegment Segment) JumpAtTheTip()
    {
        (PlanningWorldView view, _) = Column(FixtureWorld.DripstoneTipUp);
        List<PathNode> nodes =
        [
            new PathNode(0, FloorY + 1, 0) { MoveUsed = MoveType.Traverse },
            new PathNode(3, FloorY + 1, 0) { MoveUsed = MoveType.Parkour },
            new PathNode(6, FloorY + 1, 0) { MoveUsed = MoveType.Parkour },
        ];

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(nodes, view);
        return (new PathExecutionContext(view, Profile), segments[0]);
    }

    private static PhysicsState Airborne(double x, double y) => new()
    {
        Position = new Vec3d(x, y, 0.5),
        Velocity = new Vec3d(0.28, -0.1, 0.0),
        Yaw = 270f,
        OnGround = false,
    };

    /// <summary>The live stance: the body's +X face resting on the tip's -X face, its centre inside the tip's own column, and its feet on the floor 0.6875 below the elevation the segment declared.</summary>
    private static PhysicsState BesideTheTip() => new()
    {
        Position = new Vec3d(3.0125, FloorY, 0.5),
        Velocity = new Vec3d(0.0, -0.0784, 0.0),
        Yaw = 270f,
        OnGround = true,
        HorizontalCollision = true,
    };

    private static PhysicsState OnTheTip() => new()
    {
        Position = new Vec3d(3.5, FloorY + TipTop, 0.5),
        Velocity = new Vec3d(0.05, -0.0784, 0.0),
        Yaw = 270f,
        OnGround = true,
    };
}
