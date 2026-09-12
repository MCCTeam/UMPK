using Umpk.Client.Internal;
using Umpk.Client.Navigation;
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
/// A ladder fixture, block for block, on both a legacy and a modern protocol. It verifies that the player does not return to the platform one block past the ladder.
/// <para>A Climb segment has no horizontal extent (<c>MoveClimb.XOffset</c> and <c>ZOffset</c> are both zero), so the template had no heading to aim with; it pressed FORWARD along whatever yaw the approach walk had left, which on a ladder is a real horizontal acceleration, and the bot walks out of its own shaft. Reporting reported Complete on <c>Math.Abs(dy) &lt; 0.4</c>, a third of the way up a one-block climb, while still moving is premature. A climber needs no horizontal press: the jumping arm alone gives exactly <c>(0.2 - 0.08) * 0.98 = 0.1176</c> blocks per tick.</para>
/// </summary>
public sealed class LadderClimbProbeFixtureTests
{
    /// <summary>The platform surface, matching the probe's <c>PROBE_AY</c>.</summary>
    private const int Ay = 75;

    /// <summary>Vanilla's climb rate with jump held: <c>(0.2 - 0.08) * 0.98</c>.</summary>
    private const double VanillaClimbPerTick = 0.1176;

    /// <summary>1.12.2 and 1.21.7/1.21.8: one protocol from each relevant band.</summary>
    public static TheoryData<int> Protocols => [340, 772];

    /// <summary>The probe's fixture and the probe's own command. `/move` resolves to <c>NearGoal(BlockPos.Containing(target), 1)</c>, so the plan legitimately stops one rung short of the block named on the command line, and the probe's verdict is the server-side Y after the client has gone back to idling: <c>y &gt;= PROBE_AY + 1</c>.</summary>
    [Theory]
    [MemberData(nameof(Protocols))]
    public void TheProbeFixtureClimbsAndStaysUp(int protocol)
    {
        Fixture fixture = Fixture.Build(protocol);

        // The probe sends `/move (AX+1) (AY+2) (AZ)`; MovementApi turns that into a radius-1 near goal.
        var goal = new GoalNear(1, Ay + 2, 0, 1);
        var start = BlockPos.Containing(fixture.Engine.State.Position);
        PlanningWorldView planning = PlanningWorldView.Capture(
            fixture.World, fixture.Shapes, start, new BlockPos(1, Ay + 2, 0), margin: 24);

        PathResult result = PathPlanner.FindPath(planning, PathfinderOptions.Default, start, goal);
        Assert.Equal(PathStatus.Success, result.Status);
        Assert.Contains(result.Path, node => node.MoveUsed == MoveType.Climb);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path);
        var ctx = new PathExecutionContext(planning, PhysicsProfile.ForProtocol(protocol), fixture.Conditions);
        var executor = new PathExecutor(ctx, segments);

        PathExecutorState state = PathExecutorState.InProgress;
        for (int tick = 0; tick < 600 && state == PathExecutorState.InProgress; tick++)
        {
            PathExecutorTick step = executor.Tick(fixture.Engine.State);
            state = step.State;
            if (state != PathExecutorState.InProgress)
                break;

            fixture.Engine.SetRotation(step.Output.TargetYaw, step.Output.TargetPitch);
            fixture.Engine.Step(step.Output.Input);
        }

        Assert.True(
            state == PathExecutorState.Complete,
            $"protocol {protocol}: navigation ended {state} at {fixture.Engine.State.Position}");

        // Arrival must be on the rung, not 0.34 blocks below it with the bot still moving, which is how the climb ended up abandoned in mid-shaft.
        Vec3d arrival = fixture.Engine.State.Position;
        PathSegment climb = segments[^1];
        Assert.Equal(MoveType.Climb, climb.MoveType);
        Assert.InRange(arrival.Y - climb.End.Y, 0.0, PhysicsConstants.ClimbMaxSpeed);
        Assert.True(
            arrival.X > 1.0 && arrival.X < 2.0 && arrival.Z > 0.0 && arrival.Z < 1.0,
            $"protocol {protocol}: the climb finished outside the ladder's own column at {arrival}");
        Assert.True(fixture.Engine.State.OnClimbable, $"protocol {protocol}: not on the ladder at {arrival}");

        // Then idle exactly as the client does once the movement lease is released, holding the climbable the way PhysicsEngineHolder.IdleInput does. Ten seconds, which is what the probe waits.
        for (int tick = 0; tick < 200; tick++)
            fixture.Engine.Step(new MovementInput { Sneak = true });

        Vec3d rest = fixture.Engine.State.Position;
        Assert.True(
            rest.Y >= Ay + 1.0,
            $"protocol {protocol}: the probe's own verdict fails, server would see y={rest.Y} against platform {Ay}");
        Assert.True(
            rest.X > 1.0 && rest.X < 2.0,
            $"protocol {protocol}: the bot came to rest outside the ladder column at {rest}");
    }

    /// <summary>Pressing forward on the approach yaw for the whole climb makes the bot leave the ladder column and stop on the platform beyond it.</summary>
    [Theory]
    [MemberData(nameof(Protocols))]
    public void PressingForwardOnTheApproachYawWalksOutOfTheShaft(int protocol)
    {
        Fixture fixture = Fixture.Build(protocol);
        fixture.Engine.Reset(new Vec3d(1.5, Ay, 0.5), yaw: 270f, pitch: 0f);
        fixture.Engine.SetRotation(270f, 0f);

        for (int tick = 0; tick < 40; tick++)
            fixture.Engine.Step(new MovementInput { Forward = true, Jump = true });

        Vec3d pos = fixture.Engine.State.Position;
        Assert.False(fixture.Engine.State.OnClimbable, $"protocol {protocol}: still on the ladder at {pos}");
        Assert.True(pos.X > 2.0, $"protocol {protocol}: expected to have left the column, at {pos}");
    }

    /// <summary>The physics boundary on its own: jump held, nothing else, on a dry rung. This pins vanilla's climb rate and proves the engine does not need a horizontal press. It is also the assertion that would have caught the waterlogged-fixture problem in the two older ladder tests: on a waterlogged rung the rise is 0.04 per tick from <c>JumpInLiquid</c>, not 0.1176.</summary>
    [Theory]
    [MemberData(nameof(Protocols))]
    public void JumpAloneClimbsAtVanillasRateAndHoldsTheColumn(int protocol)
    {
        Fixture fixture = Fixture.Build(protocol);
        fixture.Engine.Reset(new Vec3d(1.5, Ay, 0.5), yaw: 270f, pitch: 0f);
        fixture.Engine.SetRotation(270f, 0f);

        // One tick to leave the ground (a ground jump is 0.42), then the steady climbable rate.
        fixture.Engine.Step(new MovementInput { Jump = true });
        for (int tick = 0; tick < 4; tick++)
        {
            double before = fixture.Engine.State.Position.Y;
            fixture.Engine.Step(new MovementInput { Jump = true });
            Assert.Equal(VanillaClimbPerTick, fixture.Engine.State.Position.Y - before, 4);
        }

        Assert.Equal(1.5, fixture.Engine.State.Position.X, 6);
        Assert.Equal(0.5, fixture.Engine.State.Position.Z, 6);
        Assert.True(fixture.Engine.State.OnClimbable);
    }

    /// <summary>Sneaking holds a rung by suppressing ladder descent. Without it the clamp lets the player slide at 0.15 per tick, which is the two blocks in 0.7 seconds that undid every arrival.</summary>
    [Theory]
    [MemberData(nameof(Protocols))]
    public void WithoutSneakARungIsNotHeldAndWithSneakItIs(int protocol)
    {
        Fixture slipping = Fixture.Build(protocol);
        slipping.Engine.Reset(new Vec3d(1.5, Ay + 2, 0.5), yaw: 270f, pitch: 0f);
        for (int tick = 0; tick < 60; tick++)
            slipping.Engine.Step(MovementInput.None);

        Assert.Equal(Ay, slipping.Engine.State.Position.Y, 3);

        Fixture holding = Fixture.Build(protocol);
        holding.Engine.Reset(new Vec3d(1.5, Ay + 2, 0.5), yaw: 270f, pitch: 0f);
        for (int tick = 0; tick < 60; tick++)
            holding.Engine.Step(new MovementInput { Sneak = true });

        Assert.Equal(Ay + 2, holding.Engine.State.Position.Y, 3);
    }

    /// <summary>The probe's fixture: the anchor platform, the wall, and a three-rung ladder in the state a vanilla server assigns to <c>minecraft:ladder[facing=south]</c>, in creative, which is what <c>run_movement_probe.sh</c> runs in.</summary>
    private sealed record Fixture(
        Umpk.Game.World.World World,
        IBlockShapeSource Shapes,
        PlayerPhysics Engine,
        PhysicsConditions Conditions)
    {
        internal static Fixture Build(int protocol)
        {
            bool legacy = protocol < 393;
            Registry<BlockDefinition> blocks = JavaGameData.Registries(protocol).Blocks;
            var data = new RegistryBlockDataSource(blocks, legacy);
            IBlockShapeSource shapes = JavaGameData.BlockShapes(protocol);
            var world = new Umpk.Game.World.World(
                WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), registries: null, protocol),
                data,
                WorldFactory.EmptyBiomes());

            Assert.True(blocks.TryGetValue(Identifier.Minecraft("stone"), out BlockDefinition? stone));
            int stoneState = stone.DefaultStateId;
            int ladder = LadderStates.FacingSouthDry(blocks, data);
            Assert.True(new BlockState(data, ladder).IsClimbable, "the chosen ladder state is not climbable");

            // probe_anchor: stone floor at AY-1 over [-4..4] x [-4..4], air from AY to AY+4.
            for (int x = -4; x <= 4; x++)
                for (int z = -4; z <= 4; z++)
                    world.SetBlockStateId(new BlockPos(x, Ay - 1, z), stoneState);

            // cap_ladder: the wall the ladder attaches to, then the three-rung column.
            for (int y = Ay; y <= Ay + 3; y++)
                world.SetBlockStateId(new BlockPos(1, y, -1), stoneState);

            for (int y = Ay; y <= Ay + 2; y++)
                world.SetBlockStateId(new BlockPos(1, y, 0), ladder);

            var conditions = PhysicsConditions.Default with
            {
                GameMode = Umpk.Game.Players.GameMode.Creative,
                MayFly = true,
            };

            var engine = new PlayerPhysics(new WorldPhysicsView(world, shapes), PhysicsProfile.ForProtocol(protocol));
            engine.SetConditions(conditions);
            engine.Reset(new Vec3d(0.5, Ay, 0.5), 0f, 0f);
            engine.Step(MovementInput.None);
            return new Fixture(world, shapes, engine, conditions);
        }
    }
}
