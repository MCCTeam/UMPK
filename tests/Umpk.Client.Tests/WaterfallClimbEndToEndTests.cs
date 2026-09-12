using System.Globalization;
using System.Text;
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

/// <summary>Climbing out of a WATERFALL, end to end over the real generated block data, the real per-state collision boxes, the real fluid model, the real A* and the real engine.</summary>
/// <remarks>
/// <para>A body in fluid deeper than 0.4 uses the 0.04 liquid jump instead of the 0.42 ground jump. Driven on this engine, a body settled on the floor of one full water source block and holding Jump for forty ticks reaches <c>y = 64.603043</c> from a floor at 64: it never touches the next block. The same body in knee-deep water (<c>level=6</c>, height 2/9) jumps a full <c>vy = 0.331</c> and clears it. See <c>Umpk.Physics.Tests.ShallowFluidTests</c> for the impulse itself and <c>FloatingTakeoffReachTests</c> for the reach.</para>
/// <para><c>JumpFeasibility</c> must reject land-jump moves when the takeoff cell is deep fluid. The available route is to swim up the column and climb out at the top. Both moves already existed - <c>MoveSwimVertical</c> and <c>MoveSwimExit</c>. A one-block swim exit cannot clear a falling water column, so the route out of a waterfall is vertical.</para>
/// </remarks>
public sealed class WaterfallClimbEndToEndTests
{
    /// <summary>1.21.7/1.21.8.</summary>
    private const int Protocol = 772;

    /// <summary>The bed under the pool.</summary>
    private const int BedY = 1;

    /// <summary>The feet cell the body starts in, at the foot of the fall.</summary>
    private const int FootY = 3;

    /// <summary>The submerged ledge's feet cell: one up, and still under water.</summary>
    private const int LedgeY = FootY + 1;

    /// <summary>The z lane the dry bank starts at.</summary>
    private const int BankZ = 2;

    /// <summary>
    /// A waterfall fixture with a corridor one cell wide in x at <c>x = 0</c>, running along +Z:
    /// <code>
    /// z:      0                1                2        3 y=5     air              air              air      air y=4     water[level=1]   water[level=1]   air      air     &lt;- the submerged ledge at z=1 y=3     water[level=8]   stone            stone    stone   &lt;- the takeoff, FALLING water y=2     water[level=0]   stone            stone    stone y=1     stone            stone            stone    stone
    /// </code>
    /// </summary>
    /// <remarks>
    /// <para>The move leaves the falling column for a ledge that still has water over it. The body is afloat at both ends, so <c>AscendTemplate</c>'s completion, which needs <c>physics.OnGround</c>, can never fire, and the segment dies on a budget sized for a land jump. 44 ticks, live, deterministic over five runs.</para>
    /// <para><b>Why a dry bank would prove nothing.</b> A one-block climb-out onto dry land works because pressing into the bank adds a measured 0.9203 over the waterline, where an ordinary bank stands 0.111 above a full cell's 8/9 surface. Course rows C6b, C6c and C7 climb out of a sunken basin on such an Ascend. This fixture instead keeps the move in water.</para>
    /// </remarks>
    /// <param name="waterfall">True for <c>water[level=8]</c>, the falling state, under <c>water[level=1]</c>. This adds the <c>(0, -6, 0)</c> downward term against the corridor walls. False for plain sources, which isolates the takeoff medium from the current: both rows must pass so the behavior is not specific to one fixture.</param>
    private static Umpk.Game.World.World BuildFoot(bool waterfall, out IBlockShapeSource shapes)
    {
        Registry<BlockDefinition> blocks = JavaGameData.Registries(Protocol).Blocks;
        var data = new RegistryBlockDataSource(blocks, isLegacy: false);
        shapes = JavaGameData.BlockShapes(Protocol);
        var world = new Umpk.Game.World.World(
            WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), registries: null, Protocol),
            data,
            WorldFactory.EmptyBiomes());

        int stone = DefaultState(blocks, "minecraft:stone");
        int air = DefaultState(blocks, "minecraft:air");
        int source = DefaultState(blocks, "minecraft:water");
        int falling = waterfall ? WaterAtLevel(blocks, data, 8) : source;
        int film = waterfall ? WaterAtLevel(blocks, data, 1) : source;

        // Solid stone, then carve. Carving is the honest direction: every cell this test does not name is rock, so nothing can be routed through a hole the fixture forgot to close.
        for (int x = -3; x <= 3; x++)
            for (int z = -3; z <= 8; z++)
                for (int y = BedY - 2; y <= LedgeY + 4; y++)
                    world.SetBlockStateId(new BlockPos(x, y, z), stone);

        // The corridor's head-room, over the whole run.
        for (int z = 0; z <= 6; z++)
        {
            world.SetBlockStateId(new BlockPos(0, LedgeY + 1, z), air);
            world.SetBlockStateId(new BlockPos(0, LedgeY + 2, z), air);
        }

        // The pool the body starts in, and the falling cell it starts in.
        world.SetBlockStateId(new BlockPos(0, FootY - 1, 0), source);
        world.SetBlockStateId(new BlockPos(0, FootY, 0), falling);
        world.SetBlockStateId(new BlockPos(0, LedgeY, 0), film);

        // The SUBMERGED ledge: stone floor, water still over it.
        world.SetBlockStateId(new BlockPos(0, LedgeY, 1), film);

        // The dry bank beyond it, at the same feet cell.
        for (int z = BankZ; z <= 6; z++)
            world.SetBlockStateId(new BlockPos(0, LedgeY, z), air);

        return world;
    }

    /// <summary>The body starts at the foot of the fall and must leave it by swimming.</summary>
    [Theory]
    [InlineData(true, "the owner's states: water[level=8] under water[level=1], transcribed from the cave")]
    [InlineData(false, "plain sources: the same stances with no falling flag anywhere")]
    public void ABodyAfloatAtBothEndsIsGivenASwimAndNotALandJump(bool waterfall, string why)
    {
        Assert.False(string.IsNullOrEmpty(why));
        Umpk.Game.World.World world = BuildFoot(waterfall, out IBlockShapeSource shapes);

        var start = new BlockPos(0, FootY, 0);
        var target = new BlockPos(0, LedgeY, 5);

        PlanningWorldView planning = PlanningWorldView.Capture(world, shapes, start, target, margin: 24);
        PathResult result = PathPlanner.FindPath(
            planning, PathfinderOptions.Default, start, new GoalNear(target.X, target.Y, target.Z, 0));

        Assert.Equal(PathStatus.Success, result.Status);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path, planning);

        // The planner half: no jump-family segment may both start and end with the body afloat. Stated on the SEGMENTS rather than the nodes so a future move type falling into the same trap is caught too.
        foreach (PathSegment segment in segments)
        {
            if (segment.MoveType is not (MoveType.Ascend or MoveType.Parkour))
                continue;

            BlockPos takeoff = BlockPos.Containing(segment.Start.X, segment.Start.Y, segment.Start.Z);
            BlockPos landing = BlockPos.Containing(segment.End.X, segment.End.Y, segment.End.Z);
            Assert.False(
                planning.GetBlock(takeoff).IsFluid && planning.GetBlock(landing).IsFluid,
                $"{segment.MoveType} runs {takeoff} -> {landing}, both under water: {Describe(segments, null)}");
        }

        // The positive half: the way off the submerged ledge is a swim, priced and budgeted for water.
        PathSegment leaving = Assert.Single(
            segments, segment => segment.Start.Y < LedgeY && segment.End.Y >= LedgeY);
        Assert.Equal(MoveType.Swim, leaving.MoveType);

        var conditions = PhysicsConditions.Default;
        var ctx = new PathExecutionContext(planning, PhysicsProfile.ForProtocol(Protocol), conditions);
        var engine = new PlayerPhysics(planning, PhysicsProfile.ForProtocol(Protocol));
        engine.SetConditions(conditions);
        engine.Reset(new Vec3d(start.X + 0.5, start.Y, start.Z + 0.5), 0f, 0f);

        // Settle first: the body has to reach the fall's own equilibrium before it is asked to leave it, exactly as the live bot does after it is washed to the foot.
        for (int tick = 0; tick < 40; tick++)
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

        Assert.True(state == PathExecutorState.Complete, Describe(segments, engine.State.Position));

        // Idle exactly as the client does once MoveToAsync returns. A body that "arrived" while still in the column washes straight back down here.
        for (int tick = 0; tick < 120; tick++)
            engine.Step(MovementInput.None);

        Assert.True(engine.State.OnGround, $"did not settle on the bank: {engine.State.Position}");
        Assert.Equal(LedgeY, engine.State.Position.Y, 3);
        Assert.False(engine.State.InWater, $"settled back in the water at {engine.State.Position}");
    }

    private static string Describe(IReadOnlyList<PathSegment> segments, Vec3d? position)
    {
        var sb = new StringBuilder();
        if (position is { } p)
            sb.Append(CultureInfo.InvariantCulture, $"navigation ended at {p}\n");

        foreach (PathSegment segment in segments)
            sb.Append(CultureInfo.InvariantCulture, $"  {segment.MoveType,-9} {segment.Start} -> {segment.End}\n");

        return sb.ToString();
    }

    private static int DefaultState(Registry<BlockDefinition> blocks, string name)
    {
        Assert.True(blocks.TryGetValue(Identifier.Parse(name), out BlockDefinition? definition), name);
        return definition.DefaultStateId;
    }

    /// <summary>The <c>minecraft:water</c> state at one block <c>level</c>. Level 8 is the FALLING state: <c>LiquidBlock</c>'s cache maps block level 0 to a source, 1-7 to flowing amounts 7-1 and 8 to a falling fluid state at level 8, which is the state a column of falling water is actually made of.</summary>
    private static int WaterAtLevel(Registry<BlockDefinition> blocks, IBlockDataSource data, int level)
    {
        Assert.True(blocks.TryGetValue(Identifier.Minecraft("water"), out BlockDefinition? definition));
        for (int id = definition.MinStateId; id <= definition.MaxStateId; id++)
        {
            Assert.True(data.TryGetPropertyValue(id, "level", out string value), "water has no level property");
            if (string.Equals(value, level.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                return id;

        }

        Assert.Fail($"no minecraft:water state with level={level}");
        return 0;
    }
}
