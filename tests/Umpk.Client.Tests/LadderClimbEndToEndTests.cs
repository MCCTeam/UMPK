using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Pathfinding;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Physics;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// Ladder climbing end to end over the REAL generated block data: real ladder state ids, the real per-state collision boxes and the real climbable flag, planned by the real A* and executed by the real engine, asserted as the FINAL RESTING Y after the navigation has finished and the client has gone back to idling.
/// <para>The resting Y is the assertion that matters. A player is held on a ladder only while pressing into it: downward velocity is clamped to -0.15 and zeroes it only while sneaking, so a climb that ends in mid-air on a rung slides all the way back down the moment the navigator stops driving. A ladder route is only complete when it ends somewhere the player can stand.</para>
/// </summary>
public sealed class LadderClimbEndToEndTests
{
    /// <summary>1.21.7/1.21.8, the protocol the live reproduction ran on.</summary>
    private const int Protocol = 772;

    private const int FloorY = 64;

    /// <summary>A ladder shaft with a landing at the top, approached from either side at three heights. The +X approach routes around the ladder column through two diagonals meeting at a right angle, and the handoff must complete.</summary>
    [Theory]
    [InlineData(3, -2)]
    [InlineData(3, 2)]
    [InlineData(6, -2)]
    [InlineData(6, 2)]
    [InlineData(10, -2)]
    public void AClimbToALandingEndsStandingOnTheLanding(int height, int approachX)
    {
        Registry<BlockDefinition> blocks = JavaGameData.Registries(Protocol).Blocks;
        var data = new RegistryBlockDataSource(blocks, isLegacy: false);
        IBlockShapeSource shapes = JavaGameData.BlockShapes(Protocol);
        var world = new Umpk.Game.World.World(
            WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), registries: null, Protocol),
            data,
            WorldFactory.EmptyBiomes());

        int stone = DefaultState(blocks, "minecraft:stone");
        int ladder = LadderAttachedToTheNorthWall(blocks, data, shapes);

        for (int x = -8; x <= 8; x++)
            for (int z = -8; z <= 8; z++)
                world.SetBlockStateId(new BlockPos(x, FloorY, z), stone);

        // The wall the ladder hangs on, one block taller than the ladder column.
        for (int y = FloorY + 1; y <= FloorY + height + 1; y++)
            world.SetBlockStateId(new BlockPos(0, y, -1), stone);

        for (int y = FloorY + 1; y <= FloorY + height; y++)
            world.SetBlockStateId(new BlockPos(0, y, 0), ladder);

        // The landing: its top face is level with the top rung's floor, so the player steps off sideways.
        world.SetBlockStateId(new BlockPos(1, FloorY + height - 1, 0), stone);
        var target = new BlockPos(1, FloorY + height, 0);

        var start = new BlockPos(approachX, FloorY + 1, 0);
        PlanningWorldView planning = PlanningWorldView.Capture(world, shapes, start, target, margin: 24);

        PathResult result = PathPlanner.FindPath(
            planning, PathfinderOptions.Default, start, new GoalNear(target.X, target.Y, target.Z, 0));
        Assert.Equal(PathStatus.Success, result.Status);
        Assert.Contains(result.Path, node => node.MoveUsed == MoveType.Climb);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path);
        var conditions = PhysicsConditions.Default;
        var ctx = new PathExecutionContext(planning, PhysicsProfile.ForProtocol(Protocol), conditions);
        var engine = new PlayerPhysics(planning, PhysicsProfile.ForProtocol(Protocol));
        engine.SetConditions(conditions);
        engine.Reset(new Vec3d(start.X + 0.5, start.Y, start.Z + 0.5), 0f, 0f);
        engine.Step(MovementInput.None);

        var executor = new PathExecutor(ctx, segments);
        PathExecutorState state = PathExecutorState.InProgress;
        for (int tick = 0; tick < 1200 && state == PathExecutorState.InProgress; tick++)
        {
            PathExecutorTick step = executor.Tick(engine.State);
            state = step.State;
            if (state != PathExecutorState.InProgress)
                break;

            engine.SetRotation(step.Output.TargetYaw, step.Output.TargetPitch);
            engine.Step(step.Output.Input);
        }

        Assert.True(state == PathExecutorState.Complete,
            $"height {height} approach {approachX}: navigation ended {state} at {engine.State.Position}");

        // Idle exactly as the client does once MoveToAsync returns. A climb that stopped on a rung slides back to the floor here, which is what the server sees and what a mid-climb "complete" hides.
        for (int tick = 0; tick < 120; tick++)
            engine.Step(MovementInput.None);

        Assert.Equal(FloorY + height, engine.State.Position.Y, 3);
        Assert.True(engine.State.OnGround, $"did not settle on the landing: {engine.State.Position}");
    }

    private static int DefaultState(Registry<BlockDefinition> blocks, string name)
    {
        Assert.True(blocks.TryGetValue(Identifier.Parse(name), out BlockDefinition? definition), name);
        return definition.DefaultStateId;
    }

    /// <summary>A DRY ladder whose collision slab sits against the -Z face, that is, one attached to a wall at z-1. See <see cref="LadderStates.ShapedAgainstTheNorthWall"/> for why the dry half matters.</summary>
    private static int LadderAttachedToTheNorthWall(
        Registry<BlockDefinition> blocks, IBlockDataSource data, IBlockShapeSource shapes)
        => LadderStates.ShapedAgainstTheNorthWall(blocks, data, shapes);
}
