using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Pathfinding;
using Umpk.Physics;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// The flowing-water current over the REAL generated block data: real <c>minecraft:water</c> state ids carrying real <c>level</c> values, read through the same <see cref="RegistryBlockDataSource"/> the client installs and the same planning view the navigator hands the engine.
/// <para>This is the offline twin of the live trench: a sealed one-wide channel with a source at one end and a descending level ramp after it. On the server the client stood byte-still in that trench for twelve seconds because no packet carries a current and nothing computed one locally.</para>
/// </summary>
public sealed class WaterCurrentEndToEndTests
{
    private const int Protocol = 772;
    private const int FloorY = 84;
    private const int WaterY = 85;

    [Fact]
    public void ARealWaterTrenchCarriesThePlayerDownstream()
    {
        PhysicsState settled = RunTrench(sourceOnly: false);

        // The source sits at the +X end, so the current runs toward -X.
        Assert.True(settled.Position.X < 4.0,
            $"the current did not carry the player downstream: {settled.Position}");
        Assert.Equal(4.5, settled.Position.Z, 6);
    }

    [Fact]
    public void ATrenchOfSourceBlocksOnlyDoesNotMoveThePlayer()
    {
        // The control: same geometry, same depth, no height gradient, so vanilla's flow is exactly zero.
        PhysicsState settled = RunTrench(sourceOnly: true);

        Assert.Equal(7.5, settled.Position.X, 9);
        Assert.Equal(4.5, settled.Position.Z, 9);
    }

    private static PhysicsState RunTrench(bool sourceOnly)
    {
        Registry<BlockDefinition> blocks = JavaGameData.Registries(Protocol).Blocks;
        var data = new RegistryBlockDataSource(blocks, isLegacy: false);
        IBlockShapeSource shapes = JavaGameData.BlockShapes(Protocol);
        var world = new Umpk.Game.World.World(
            WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), registries: null, Protocol),
            data,
            WorldFactory.EmptyBiomes());

        Assert.True(blocks.TryGetValue(Identifier.Minecraft("stone"), out BlockDefinition? stone));
        Assert.True(blocks.TryGetValue(Identifier.Minecraft("water"), out BlockDefinition? water));

        for (int x = 0; x <= 14; x++)
            for (int z = 2; z <= 6; z++)
                world.SetBlockStateId(new BlockPos(x, FloorY, z), stone.DefaultStateId);

        for (int x = 0; x <= 14; x++)
            for (int y = WaterY; y <= WaterY + 1; y++)
            {
                world.SetBlockStateId(new BlockPos(x, y, 3), stone.DefaultStateId);
                world.SetBlockStateId(new BlockPos(x, y, 5), stone.DefaultStateId);
            }

        // Source at x = 10, then one level down per block toward -X, which is exactly what a vanilla server spreads into a sealed channel.
        for (int x = 10; x >= 3; x--)
        {
            int level = sourceOnly ? 0 : 10 - x;
            world.SetBlockStateId(new BlockPos(x, WaterY, 4), WaterState(water, data, level));
        }

        var start = new Vec3d(7.5, WaterY, 4.5);
        PlanningWorldView planning = PlanningWorldView.Capture(
            world, shapes, new BlockPos(3, WaterY, 4), new BlockPos(11, WaterY, 4), margin: 8);

        var engine = new PlayerPhysics(planning, PhysicsProfile.ForProtocol(Protocol));
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(start, 0f, 0f);
        for (int tick = 0; tick < 240; tick++)
            engine.Step(MovementInput.None);

        return engine.State;
    }

    /// <summary>The <c>minecraft:water</c> state with a given <c>level</c>. Water's only property is <c>level</c>, so the state id is the block's minimum plus the level.</summary>
    private static int WaterState(BlockDefinition water, IBlockDataSource data, int level)
    {
        int id = water.MinStateId + level;
        var state = new BlockState(data, id);
        Assert.True(state.TryGetProperty("level", out string actual));
        Assert.Equal(level.ToString(System.Globalization.CultureInfo.InvariantCulture), actual);
        return id;
    }
}
