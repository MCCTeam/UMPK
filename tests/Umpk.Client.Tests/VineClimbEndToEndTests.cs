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

/// <summary>Topping out of an ORDINARY <c>minecraft:vine</c>, end to end over the real generated block data, the real per-state collision boxes, the real climbable flag, the real A* and the real engine.</summary>
/// <remarks>
/// <para>The fixture has a pond, a corner column with vines on each side, and a shorter wall beside it. The behavior is a two-by-two. The exit off the top rung is either CARDINAL or DIAGONAL, and the column the vine hangs on either stops at the top rung or carries ONE MORE BLOCK above it. The measured outcomes are:</para>
/// <code>
/// exit      capped   outcome cardinal  no       Complete cardinal  yes      Complete diagonal  no       Complete diagonal  yes      FAILED, ends at (-0.3000, 66.1661, 2.5978) - pinned on the column face, then fell
/// </code>
/// <para><b>Why the last row cannot work.</b> A hanging body has no ground jump. Its climb lift applies only while the feet cell is climbable and peaks 1.2520 above the rung. That buys a cardinal step-off and it is why the two cardinal rows pass with the cap on. It does not buy a corner: with the cap in place the body cannot cut over the column, and going round the open side leaves the lift behind before it has crossed. See <c>Umpk.Pathfinding.Tests.Moves.HangingTakeoffTests</c> for the rule itself.</para>
/// <para>The two diagonal rows have different outcomes. With the cap OFF, refusing the corner costs no route at all: A* takes the CARDINAL step-off onto the column's own top instead, which is what a player does at the top of a vine and what the lift measurement says is real, and the run completes. With the cap ON there is no route, and that is the honest answer for that world - the column top is now a block higher than the lift reaches and the only other exit is the corner. A refusal the navigator can report beats a plan that drops the bot in the pond and is replanned until it gives up.</para>
/// </remarks>
public sealed class VineClimbEndToEndTests
{
    /// <summary>1.21.7/1.21.8.</summary>
    private const int Protocol = 772;

    private const int FloorY = 64;

    /// <summary>The plateau's top surface, one block above the vine's top rung.</summary>
    private const int PlateauY = FloorY + 4;

    [Theory]
    [InlineData("cardinal", false, true)]
    [InlineData("cardinal", true, true)]
    [InlineData("diagonal", false, true)]
    [InlineData("diagonal", true, false)]
    public void AVineTopOutEitherWorksOrIsRefused(string exit, bool capped, bool routeExpected)
    {
        bool cardinal = string.Equals(exit, "cardinal", StringComparison.Ordinal);
        Registry<BlockDefinition> blocks = JavaGameData.Registries(Protocol).Blocks;
        var data = new RegistryBlockDataSource(blocks, isLegacy: false);
        IBlockShapeSource shapes = JavaGameData.BlockShapes(Protocol);
        var world = new Umpk.Game.World.World(
            WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), registries: null, Protocol),
            data,
            WorldFactory.EmptyBiomes());

        int stone = DefaultState(blocks, "minecraft:stone");
        int vine = VineAttachedTo(blocks, data, "east");

        for (int x = -10; x <= 10; x++)
            for (int z = -10; z <= 10; z++)
                world.SetBlockStateId(new BlockPos(x, FloorY, z), stone);

        // The column the vine hangs on, and the 3-tall vine itself: rungs at 65, 66, 67.
        for (int y = FloorY + 1; y <= FloorY + 3; y++)
        {
            world.SetBlockStateId(new BlockPos(0, y, 0), stone);
            world.SetBlockStateId(new BlockPos(-1, y, 0), vine);
        }

        // The plateau. Cardinal: it reaches the vine's own -X lane, so the step-off is a straight line. Diagonal: it stops at x = 0, so the only cell the body can reach is across the corner.
        for (int x = -10; x <= 10; x++)
            for (int z = 1; z <= 10; z++)
            {
                if (!cardinal && x < 0)
                    continue;

                for (int y = FloorY + 1; y <= FloorY + 3; y++)
                    world.SetBlockStateId(new BlockPos(x, y, z), stone);

            }

        if (capped)
            world.SetBlockStateId(new BlockPos(0, FloorY + 4, 0), stone);

        var start = new BlockPos(3, FloorY + 1, 0);
        var target = new BlockPos(cardinal ? -1 : 1, PlateauY, 3);

        PlanningWorldView planning = PlanningWorldView.Capture(world, shapes, start, target, margin: 24);
        PathResult result = PathPlanner.FindPath(
            planning, PathfinderOptions.Default, start, new GoalNear(target.X, target.Y, target.Z, 0));

        if (!routeExpected)
        {
            Assert.Equal(PathStatus.Failed, result.Status);
            return;
        }

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.Contains(result.Path, node => node.MoveUsed == MoveType.Climb);

        IReadOnlyList<PathSegment> segments = PathSegmentBuilder.FromPath(result.Path, planning);
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

        Assert.True(
            state == PathExecutorState.Complete,
            Describe(exit, capped, state, engine.State.Position, segments));

        // Idle exactly as the client does once MoveToAsync returns. A body that "arrived" on a rung rather than on the plateau slides back down the vine here.
        for (int tick = 0; tick < 120; tick++)
            engine.Step(MovementInput.None);

        Assert.Equal(PlateauY, engine.State.Position.Y, 3);
        Assert.True(engine.State.OnGround, $"{exit} capped={capped}: did not settle: {engine.State.Position}");
    }

    private static string Describe(
        string exit, bool capped, PathExecutorState state, Vec3d position, IReadOnlyList<PathSegment> segments)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{exit} capped={capped}: navigation ended {state} at {position}\n");
        foreach (PathSegment segment in segments)
            sb.Append(CultureInfo.InvariantCulture, $"  {segment.MoveType,-9} {segment.Start} -> {segment.End}\n");

        return sb.ToString();
    }

    private static int DefaultState(Registry<BlockDefinition> blocks, string name)
    {
        Assert.True(blocks.TryGetValue(Identifier.Parse(name), out BlockDefinition? definition), name);
        return definition.DefaultStateId;
    }

    /// <summary>The <c>minecraft:vine</c> state attached to exactly one face. Vines carry five independent boolean faces and the DEFAULT state has none of them set, which is a state no vanilla world ever contains; a fixture built from the default would be testing a vine hanging on nothing.</summary>
    private static int VineAttachedTo(Registry<BlockDefinition> blocks, IBlockDataSource data, string face)
    {
        Assert.True(blocks.TryGetValue(Identifier.Minecraft("vine"), out BlockDefinition? definition));
        for (int id = definition.MinStateId; id <= definition.MaxStateId; id++)
        {
            bool matched = true;
            foreach (string property in VineFaces)
            {
                Assert.True(data.TryGetPropertyValue(id, property, out string value), property);
                bool wanted = string.Equals(property, face, StringComparison.Ordinal);
                if (!string.Equals(value, wanted ? "true" : "false", StringComparison.Ordinal))
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
                return id;

        }

        Assert.Fail("no vine state attached to " + face);
        return 0;
    }

    private static readonly string[] VineFaces = ["east", "north", "south", "up", "west"];
}
