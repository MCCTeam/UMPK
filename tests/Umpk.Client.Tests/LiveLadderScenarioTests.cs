using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Pathfinding;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Execution.Telemetry;
using Umpk.Pathfinding.Goals;
using Umpk.Physics;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// The live ladder scenario reproduced block for block offline: the platform, the wall, the three-rung ladder, the landing beside the top rung, the bot starting two blocks away on the LANDING side, creative game mode, and the <c>move</c> command's own goal (a <see cref="GoalNear"/> of radius one around the target block).
/// <para>On the server this printed <c>Segment 0/4 failed (Diagonal) after 141 ticks at (9.1513, 85.0000, 5.3000)</c> and then walked off the platform over four replans, ending eleven blocks BELOW where it started. Nothing was in the bot's way: it was oscillating on the boundary of its own destination block.</para>
/// </summary>
public sealed class LiveLadderScenarioTests
{
    private const int Protocol = 772;

    [Fact]
    public void TheLiveLadderScenarioReachesTheLanding()
    {
        Registry<BlockDefinition> blocks = JavaGameData.Registries(Protocol).Blocks;
        var data = new RegistryBlockDataSource(blocks, isLegacy: false);
        IBlockShapeSource shapes = JavaGameData.BlockShapes(Protocol);
        var world = new Umpk.Game.World.World(
            WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), registries: null, Protocol),
            data,
            WorldFactory.EmptyBiomes());

        Assert.True(blocks.TryGetValue(Identifier.Minecraft("stone"), out BlockDefinition? stone));
        int ladder = LadderAttachedToTheNorthWall(blocks, data, shapes);

        for (int x = 1; x <= 14; x++)
            for (int z = 1; z <= 7; z++)
                world.SetBlockStateId(new BlockPos(x, 84, z), stone.DefaultStateId);

        for (int y = 85; y <= 88; y++)
            world.SetBlockStateId(new BlockPos(8, y, 3), stone.DefaultStateId);

        for (int y = 85; y <= 87; y++)
            world.SetBlockStateId(new BlockPos(8, y, 4), ladder);

        world.SetBlockStateId(new BlockPos(9, 86, 4), stone.DefaultStateId);

        var start = new BlockPos(10, 85, 4);
        var goal = new GoalNear(9, 87, 4, 1);
        PlanningWorldView planning = PlanningWorldView.Capture(world, shapes, start, new BlockPos(9, 87, 4), margin: 24);

        PathResult result = PathPlanner.FindPath(planning, PathfinderOptions.Default, start, goal);
        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path);
        var conditions = PhysicsConditions.Default with
        {
            GameMode = Umpk.Game.Players.GameMode.Creative,
            MayFly = true,
        };

        var ctx = new PathExecutionContext(planning, PhysicsProfile.ForProtocol(Protocol), conditions);
        var engine = new PlayerPhysics(planning, PhysicsProfile.ForProtocol(Protocol));
        engine.SetConditions(conditions);
        engine.Reset(new Vec3d(10.5, 85, 4.5), 0f, 0f);
        engine.Step(MovementInput.None);

        var failures = new List<string>();
        var observer = new DelegatePathExecutionObserver(line =>
        {
            if (line.Contains("FAILED", StringComparison.Ordinal))
                failures.Add(line);

        });

        var executor = new PathExecutor(ctx, segments, observer);
        PathExecutorState state = PathExecutorState.InProgress;
        for (int tick = 0; tick < 600 && state == PathExecutorState.InProgress; tick++)
        {
            PathExecutorTick step = executor.Tick(engine.State);
            state = step.State;
            if (state != PathExecutorState.InProgress)
                break;

            engine.SetRotation(step.Output.TargetYaw, step.Output.TargetPitch);
            engine.Step(step.Output.Input);
        }

        Assert.True(state == PathExecutorState.Complete,
            $"navigation ended {state} at {engine.State.Position}; failures [{string.Join("; ", failures)}]");
        Assert.Empty(failures);

        for (int tick = 0; tick < 120; tick++)
            engine.Step(MovementInput.None);

        Assert.Equal(87.0, engine.State.Position.Y, 3);
        Assert.True(engine.State.OnGround, $"did not settle on the landing: {engine.State.Position}");
    }

    /// <summary>A DRY ladder whose collision slab sits against the -Z face. See <see cref="LadderStates.ShapedAgainstTheNorthWall"/> for why the dry half is load-bearing: this fixture must select the dry state rather than the first shape-compatible state, which is waterlogged on protocol 772. Otherwise the scenario exercises <c>TravelInWater</c>, not climbing.</summary>
    private static int LadderAttachedToTheNorthWall(
        Registry<BlockDefinition> blocks, IBlockDataSource data, IBlockShapeSource shapes)
        => LadderStates.ShapedAgainstTheNorthWall(blocks, data, shapes);
}
