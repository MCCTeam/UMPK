using System.Globalization;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Execution.Templates;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests.Execution;

public sealed class JumpEntryBrakingTests
{
    private const int FloorY = 64;

    private const float FacingPlusX = 270f;

    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(774);

    /// <summary>A sprinting bot a third of a block from the end of a segment that declares <c>MaxExitSpeed 0.16</c>. Carrying that sprint through the hint's own twelve-tick horizon runs it far past the cell it is supposed to hand off in, and the segment's envelope is the statement that this is not allowed; the planner short-circuited to <c>Carry</c> without ever scoring it.</summary>
    [Fact]
    public void JumpReadyTurn_WithAFiniteEnvelope_ShedsSpeedInsteadOfCarrying()
    {
        (PathExecutionContext ctx, PhysicsState sprinting) = SprintOnAFloor();
        PathSegment segment = JumpReadyTurn(sprinting.Position, maxExitSpeed: 0.16);

        // The fixture has to be a real discriminator rather than a coin flip, so state what carrying actually costs here before asserting that the planner refuses to.
        var carry = new MovementInput[segment.ExitHints.HorizonTicks];
        Array.Fill(carry, new MovementInput { Forward = true, Sprint = true });
        PhysicsState carried = PhysicsSimulator.Run(sprinting, ctx.Conditions, ctx.World, ctx.Profile, carry);
        double overshoot = -SegmentGeometry.RemainingDistanceAlongSegment(carried.Position, segment);
        string overshootText = overshoot.ToString("F3", CultureInfo.InvariantCulture);
        Assert.True(
            overshoot > 1.0,
            $"carrying only overshoots the handoff cell by {overshootText}, so this fixture cannot tell a "
                + "scored decision from a short-circuited one");

        TransitionBrakingDecision decision = ctx.Braking.Plan(segment, sprinting.Position, sprinting);

        string capText = segment.ExitHints.MaxExitSpeed.ToString("F2", CultureInfo.InvariantCulture);
        Assert.False(
            decision.HoldForward,
            $"a jump-ready Turn declaring MaxExitSpeed {capText} carried forward anyway, {overshootText} "
                + "past its own handoff cell");
    }

    [Fact]
    public void PrepareJumpEntry_WithNoEnvelope_StillCarries()
    {
        (PathExecutionContext ctx, PhysicsState sprinting) = SprintOnAFloor();
        PathSegment segment = PrepareJumpEntry(sprinting.Position);
        Assert.True(double.IsPositiveInfinity(segment.ExitHints.MaxExitSpeed));

        TransitionBrakingDecision decision = ctx.Braking.Plan(segment, sprinting.Position, sprinting);

        Assert.True(decision.HoldForward, "a PrepareJump run-up stopped carrying its momentum");
        Assert.False(decision.HoldBack, "a PrepareJump run-up braked on its own launch block");
    }

    [Fact]
    public void JumpReadyTurn_WithNoEnvelope_StillCarries()
    {
        (PathExecutionContext ctx, PhysicsState sprinting) = SprintOnAFloor();
        PathSegment segment = JumpReadyTurn(sprinting.Position, maxExitSpeed: double.PositiveInfinity);

        TransitionBrakingDecision decision = ctx.Braking.Plan(segment, sprinting.Position, sprinting);

        Assert.True(decision.HoldForward, "a jump entry with no declared envelope stopped carrying");
    }

    [Fact]
    public void JumpReadyTurn_DoesNotCarryPastItsOwnEnvelope()
    {
        var world = new FixtureWorld();
        world.Fill(512, 99, 192, 517, 99, 194, FixtureWorld.Stone);
        world.Fill(520, 100, 192, 525, 100, 194, FixtureWorld.Stone);

        var start = new BlockPos(513, 100, 193);
        var goal = new BlockPos(522, 101, 193);
        PlanningWorldView view = world.Capture(start, goal, margin: 24);
        PathResult plan = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, plan.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(plan.Path);
        PathSegment? entry = null;
        foreach (PathSegment segment in segments)
            if (segment.ExitTransition == PathTransitionType.Turn && segment.ExitHints.RequireJumpReady)
            {
                entry = segment;
                break;
            }

        // The row requires a jump entry with a FINITE envelope; otherwise it cannot exercise braking.
        Assert.NotNull(entry);
        Assert.Equal(0.05, entry.ExitHints.MinExitSpeed, 6);
        Assert.Equal(0.16, entry.ExitHints.MaxExitSpeed, 6);

        var ctx = new PathExecutionContext(view, Profile);
        var driver = new ExecutionDriver(
            ctx, segments, new Vec3d(513.5, 100, 193.5), FacingPlusX, seedAtStartPos: true);
        PathExecutorState state = driver.Run(maxTicks: 800);
        Vec3d end = driver.State.Position;

        Assert.True(
            end.Y >= 99.9,
            string.Create(
                CultureInfo.InvariantCulture,
                $"D8 left the shelf: {state} at ({end.X:F3}, {end.Y:F3}, {end.Z:F3}), with the floor at y = 100"));
    }

    /// <summary>A bot sprinting along +x on flat stone, which is the state a jump run-up arrives in.</summary>
    private static (PathExecutionContext Ctx, PhysicsState Sprinting) SprintOnAFloor()
    {
        var world = new FixtureWorld();
        world.Fill(-4, FloorY, -4, 24, FloorY, 4, FixtureWorld.Stone);
        PlanningWorldView view = world.Capture(
            new BlockPos(0, FloorY + 1, 0), new BlockPos(20, FloorY + 1, 0), margin: 8);
        var ctx = new PathExecutionContext(view, Profile);

        var engine = new PlayerPhysics(ctx.World, ctx.Profile);
        engine.SetConditions(ctx.Conditions);
        engine.Reset(new Vec3d(0.5, FloorY + 1, 0.5), FacingPlusX, 0f);
        var run = new MovementInput { Forward = true, Sprint = true };
        for (int tick = 0; tick < 20; tick++)
            engine.Step(run);

        return (ctx, engine.State);
    }

    /// <summary>The <c>nextImmediatelyJumps</c> Turn hint <c>PathSegmentBuilder</c> builds verbatim, with the envelope cap parameterised so the same fixture can state one and decline to state one.</summary>
    private static PathSegment JumpReadyTurn(in Vec3d pos, double maxExitSpeed) => new()
    {
        Start = new Vec3d(pos.X - 4.0, pos.Y, pos.Z),
        End = new Vec3d(pos.X + 0.3, pos.Y, pos.Z),
        MoveType = MoveType.Traverse,
        PreserveSprint = true,
        ExitTransition = PathTransitionType.Turn,
        ExitHints = new PathTransitionHints(
            DesiredHeadingX: 1,
            DesiredHeadingZ: 1,
            MinExitSpeed: 0.05,
            MaxExitSpeed: maxExitSpeed,
            RequireStableFooting: false,
            RequireGrounded: true,
            RequireJumpReady: true,
            AllowAirBrake: true,
            HorizonTicks: 12),
    };

    /// <summary>The run-up-into-a-Parkour hint, verbatim: jump-ready, and no envelope at all.</summary>
    private static PathSegment PrepareJumpEntry(in Vec3d pos) => new()
    {
        Start = new Vec3d(pos.X - 4.0, pos.Y, pos.Z),
        End = new Vec3d(pos.X + 1.0, pos.Y, pos.Z),
        MoveType = MoveType.Traverse,
        PreserveSprint = true,
        ExitTransition = PathTransitionType.PrepareJump,
        ExitHints = new PathTransitionHints(
            DesiredHeadingX: 1,
            DesiredHeadingZ: 0,
            MinExitSpeed: 0.10,
            MaxExitSpeed: double.PositiveInfinity,
            RequireStableFooting: false,
            RequireGrounded: true,
            RequireJumpReady: true,
            AllowAirBrake: false,
            HorizonTicks: 10),
    };
}
