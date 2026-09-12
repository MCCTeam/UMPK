using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Pathfinding;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Goals;
using Umpk.Physics;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// A kelp forest over the REAL generated block data, read through the same <see cref="RegistryBlockDataSource"/> the client installs and the same planning view the navigator hands both the planner and the engine.
///
/// <para>Kelp is the default decoration of every ocean biome, and a kelp cell is a full water source cell. Treating a kelp cell as empty which cut a hole clean through the swim graph - no move family can use a cell that is neither water nor floor - while the executor, physically inside the same cell, fell through it under air gravity.</para>
/// </summary>
public sealed class KelpForestNavigationTests
{
    private const int Protocol = 772;
    private const int BedY = 59;

    private sealed record Forest(
        Umpk.Game.World.World World, IBlockShapeSource Shapes, RegistryBlockDataSource Data);

    /// <summary>A stone bed at <see cref="BedY"/> with a nine-tall kelp column over it and open air above. The bed is the ONLY floor: a body at the middle of the column has nothing to stand on, so the route across it exists only if kelp is swimmable.</summary>
    private static Forest BuildForest(string plant)
    {
        Registry<BlockDefinition> blocks = JavaGameData.Registries(Protocol).Blocks;
        var data = new RegistryBlockDataSource(blocks, isLegacy: false);
        IBlockShapeSource shapes = JavaGameData.BlockShapes(Protocol);
        var world = new Umpk.Game.World.World(
            WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), registries: null, Protocol),
            data,
            WorldFactory.EmptyBiomes());

        Assert.True(blocks.TryGetValue(Identifier.Minecraft("stone"), out BlockDefinition? stone));
        Assert.True(blocks.TryGetValue(Identifier.Minecraft(plant), out BlockDefinition? weed));

        for (int x = -4; x <= 14; x++)
            for (int z = -4; z <= 4; z++)
                world.SetBlockStateId(new BlockPos(x, BedY, z), stone.DefaultStateId);

        for (int x = -2; x <= 12; x++)
            for (int y = BedY + 1; y <= BedY + 9; y++)
                world.SetBlockStateId(new BlockPos(x, y, 0), weed.DefaultStateId);

        return new Forest(world, shapes, data);
    }

    [Theory]
    [InlineData("kelp")]
    [InlineData("kelp_plant")]
    [InlineData("seagrass")]
    [InlineData("tall_seagrass")]
    public void Kelp_IsWaterToThePhysicsEngine(string plant)
    {
        Forest forest = BuildForest(plant);
        PlanningWorldView view = PlanningWorldView.Capture(
            forest.World, forest.Shapes, new BlockPos(0, BedY, 0), new BlockPos(0, BedY + 10, 0), margin: 6);

        var engine = new PlayerPhysics(view, PhysicsProfile.ForProtocol(Protocol));
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(0.5, BedY + 5, 0.5), 0f, 0f);
        engine.Step(MovementInput.None);

        Assert.True(engine.State.InWater, $"minecraft:{plant} is a full water source cell to vanilla");
    }

    [Fact]
    public void Kelp_IsSwimmableToThePlanner()
    {
        Forest forest = BuildForest("kelp");
        PlanningWorldView view = PlanningWorldView.Capture(
            forest.World, forest.Shapes, new BlockPos(0, BedY, 0), new BlockPos(0, BedY + 10, 0), margin: 6);
        var ctx = new CalculationContext(view, PathfinderOptions.Default);

        Assert.True(ctx.CanWalkThrough(0, BedY + 5, 0));
        Assert.True(ctx.CanTraverseWater(0, BedY + 5, 0));
    }

    /// <summary>The route itself. The goal sits in the middle of the column with nothing under it, so before the dataset carried kelp the planner could not even accept the goal, let alone reach it.</summary>
    [Fact]
    public void KelpForest_DoesNotBlockASwimRoute()
    {
        Forest forest = BuildForest("kelp");
        var start = new BlockPos(0, BedY + 5, 0);
        var goal = new BlockPos(10, BedY + 5, 0);
        PlanningWorldView view = PlanningWorldView.Capture(forest.World, forest.Shapes, start, goal, margin: 6);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));

        Assert.Equal(PathStatus.Success, result.Status);

        bool swam = false;
        foreach (MoveType move in result.Moves)
            if (move == MoveType.Swim)
            {
                swam = true;
                break;
            }

        Assert.True(swam, "crossing a kelp forest at mid-column depth is a swim");
    }

    /// <summary>A bubble column is water for navigation, applies vertical motion, and does not drain air.</summary>
    /// <remarks>
    /// <para><c>minecraft:bubble_column</c> has an unconditional water fluid state. <c>PlayerPhysics</c> applies its upward motion, and <c>BreathModel.IsSubmerged</c> carries the air-drain exemption. The row therefore asserts all three: the column is water, the body in it is buoyant, and the breath model does not count it as submerged.</para>
    /// <para>The forest uses the default <c>drag=true</c> bubble-column state. That state is water even when its downward motion is not modeled.</para>
    /// </remarks>
    [Fact]
    public void BubbleColumn_IsWaterAndDoesNotDrainAir()
    {
        Forest forest = BuildForest("bubble_column");
        PlanningWorldView view = PlanningWorldView.Capture(
            forest.World, forest.Shapes, new BlockPos(0, BedY, 0), new BlockPos(0, BedY + 10, 0), margin: 6);

        var engine = new PlayerPhysics(view, PhysicsProfile.ForProtocol(Protocol));
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(0.5, BedY + 5, 0.5), 0f, 0f);
        engine.Step(MovementInput.None);

        Assert.True(engine.State.InWater, "a bubble column's fluid state is an unconditional water source");
        Assert.False(
            Umpk.Pathfinding.Core.BreathModel.IsSubmerged(view, 0, BedY + 5, 0),
            "vanilla exempts a bubble-column eye cell from air drain (LivingEntity.java:437-438)");
    }
}
