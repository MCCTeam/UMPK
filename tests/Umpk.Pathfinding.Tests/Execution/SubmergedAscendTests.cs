using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests.Execution;

public sealed class SubmergedAscendTests
{
    private const int FloorY = 64;
    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(107);

    private static Vec3d Center(int x, int y, int z) => new(x + 0.5, y, z + 0.5);

    /// <summary>A stone floor at <see cref="FloorY"/>, a one-block step at x=3, and a player walking from x=0 onto the step. Execution consists of <c>Traverse (0.5,65,0.5)-&gt;(2.5,65,0.5)</c>, then <c>Ascend</c> onto the step.</summary>
    private static IReadOnlyList<PathSegment> StepUpSegments() =>
    [
        new PathSegment
        {
            Start = Center(0, FloorY + 1, 0),
            End = Center(2, FloorY + 1, 0),
            MoveType = MoveType.Traverse,
            ExitTransition = PathTransitionType.PrepareJump,
        },
        new PathSegment
        {
            Start = Center(2, FloorY + 1, 0),
            End = Center(3, FloorY + 2, 0),
            MoveType = MoveType.Ascend,
            ExitTransition = PathTransitionType.FinalStop,
        },
    ];

    private static FixtureWorld StepWorld()
    {
        var world = new FixtureWorld();
        world.Floor(-4, 12, -4, 4, FloorY);
        // The one-block step and the shelf behind it, from x=3 on.
        world.Fill(3, FloorY + 1, -4, 8, FloorY + 1, 4, FixtureWorld.Stone);
        return world;
    }

    /// <summary>THE DRY CONTROL. The same two segments with no water anywhere must still complete on top of the step, so the submerged case below cannot be made to pass by breaking the ground jump.</summary>
    [Fact]
    public void Ascend_ClimbsTheStep_OnDryLand()
    {
        FixtureWorld world = StepWorld();
        var start = new BlockPos(0, FloorY + 1, 0);
        var goal = new BlockPos(3, FloorY + 2, 0);

        PlanningWorldView view = world.Capture(start, goal, margin: 8);
        var ctx = new PathExecutionContext(view, Profile);

        var driver = new ExecutionDriver(ctx, StepUpSegments(), Center(0, FloorY + 1, 0), startYaw: 270f);
        PathExecutorState state = driver.Run(600);

        Assert.Equal(PathExecutorState.Complete, state);
        Assert.True(
            driver.State.Position.Y >= FloorY + 2,
            $"the player never got on top of the step: {driver.State.Position}");
    }

    [Fact]
    public void Ascend_ClimbsTheStep_WhileSubmerged()
    {
        FixtureWorld world = StepWorld();
        // Flood every open cell over both levels, so the player is fully in water the whole way.
        world.Fill(-4, FloorY + 1, -4, 2, FloorY + 6, 4, FixtureWorld.Water);
        world.Fill(3, FloorY + 2, -4, 8, FloorY + 6, 4, FixtureWorld.Water);

        var start = new BlockPos(0, FloorY + 1, 0);
        var goal = new BlockPos(3, FloorY + 2, 0);

        PlanningWorldView view = world.Capture(start, goal, margin: 8);
        var ctx = new PathExecutionContext(view, Profile);

        var driver = new ExecutionDriver(ctx, StepUpSegments(), Center(0, FloorY + 1, 0), startYaw: 270f);
        PathExecutorState state = driver.Run(600);

        Assert.Equal(PathExecutorState.Complete, state);
        Assert.True(
            driver.State.Position.Y >= FloorY + 2,
            $"the player never got on top of the submerged step: {driver.State.Position}");
    }
}
