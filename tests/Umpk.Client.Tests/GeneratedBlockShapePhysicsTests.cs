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
/// The generated block shapes, driven through the real physics engine and the real planning world view, asserted as positions.
/// <para>A unit-cube fallback produces observably wrong movement: a player standing on a slab rests a full block up, a carpet becomes a block-high step, a fence becomes a solid wall with no gap to walk through, and a stair becomes an unclimbable cube. Every assertion below picks a number the unit cube cannot produce.</para>
/// </summary>
public sealed class GeneratedBlockShapePhysicsTests
{
    /// <summary>1.21.5 shape coverage.</summary>
    private const int Protocol = 770;

    private const int FloorY = 63;

    /// <summary>The bottom slab's top face is half a block up, so a player landing on one rests at y+0.5 and not y+1.0.</summary>
    [Fact]
    public void A_player_landing_on_a_slab_rests_half_a_block_up()
    {
        double onSlab = RestHeightOn("minecraft:smooth_stone_slab");
        double onStone = RestHeightOn("minecraft:stone");

        Assert.Equal(FloorY + 1.5, onSlab, 6);
        Assert.Equal(FloorY + 2.0, onStone, 6);
    }

    /// <summary>A carpet is one sixteenth of a block high.</summary>
    [Fact]
    public void A_player_landing_on_a_carpet_rests_one_sixteenth_up()
        => Assert.Equal(FloorY + 1.0625, RestHeightOn("minecraft:white_carpet"), 6);

    /// <summary>An unconnected fence is a 4/16-wide centered post, so its collision starts at x/z 0.375 rather than at the block edge. Walking into the post stops the player LATER than a cube would, and the 0.375-wide margin beside the post is walkable, which a cube makes impossible.</summary>
    [Fact]
    public void A_fence_post_stops_the_walker_at_the_post_and_leaves_the_gap_beside_it_open()
    {
        // Two lone fence posts at x = 0 and x = 2, with block x = 1 left empty between them.
        ShapedWorld world = Ground();
        world.Set(0, FloorY + 1, 3, "minecraft:oak_fence");
        world.Set(2, FloorY + 1, 3, "minecraft:oak_fence");

        // Straight at the post at x = 0: the player's +Z face (z + 0.3) meets the post face at 3.375.
        double blockedZ = WalkNorthward(world, startX: 0.5).Z;
        Assert.Equal(3.375 - (PhysicsConstants.PlayerWidth / 2.0), blockedZ, 3);
        Assert.True(blockedZ > 3.0, $"stopped at the block edge like a full cube, z={blockedZ}");

        // Through the gap at x = 1.5: the player box spans 1.2 to 1.8 and clears both posts.
        double throughZ = WalkNorthward(world, startX: 1.5).Z;
        Assert.True(throughZ > 5.0, $"the gap between two fence posts was not walkable, z={throughZ}");
    }

    /// <summary>A straight stair is the bottom slab joined with the raised half on the facing side; the default state faces north. Approached from the south the player takes two half-block steps and ends on top of the stair, which is inside <c>StepHeight</c> 0.6 twice over. A unit cube is a single 1.0 step and stops the player dead at the block face.</summary>
    [Fact]
    public void A_bottom_stair_is_climbed_in_two_half_steps()
    {
        // A backstop wall past the stair so the walk ends ON the stair instead of carrying on north.
        ShapedWorld stairs = Ground();
        stairs.Set(0, FloorY + 1, 3, "minecraft:oak_stairs");
        stairs.Fill(-4, FloorY + 1, 2, 4, FloorY + 3, 2, "minecraft:stone");
        Vec3d onStairs = WalkSouthward(stairs, startZ: 6.5);

        // Half a step onto the stair's base (top 64.5), then half a step onto its raised half (top 65).
        Assert.Equal(FloorY + 2.0, onStairs.Y, 6);
        Assert.Equal(3.0 + (PhysicsConstants.PlayerWidth / 2.0), onStairs.Z, 3);

        // The control: the same block as a full cube is a wall, not a staircase.
        ShapedWorld cube = Ground();
        cube.Set(0, FloorY + 1, 3, "minecraft:stone");
        cube.Fill(-4, FloorY + 1, 2, 4, FloorY + 3, 2, "minecraft:stone");
        Vec3d atWall = WalkSouthward(cube, startZ: 6.5);

        Assert.Equal(FloorY + 1.0, atWall.Y, 6);
        Assert.Equal(4.0 + (PhysicsConstants.PlayerWidth / 2.0), atWall.Z, 3);
    }

    /// <summary>The pathfinder plans and forward-simulates through <see cref="PlanningWorldView"/>, so the same shapes have to reach it: a captured region of slab floor must rest the simulated player at the slab's height, not the block's.</summary>
    [Fact]
    public void The_pathfinders_planning_view_carries_the_same_shapes()
    {
        Registry<BlockDefinition> blocks = JavaGameData.Registries(Protocol).Blocks;
        var data = new RegistryBlockDataSource(blocks, isLegacy: false);
        var world = new Umpk.Game.World.World(
            WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), registries: null, Protocol),
            data,
            WorldFactory.EmptyBiomes());

        int slab = DefaultStateId(blocks, "minecraft:smooth_stone_slab");
        for (int x = -4; x <= 4; x++)
            for (int z = -4; z <= 8; z++)
                world.SetBlockStateId(new BlockPos(x, FloorY + 1, z), slab);

        PlanningWorldView planning = PlanningWorldView.Capture(
            world,
            JavaGameData.BlockShapes(Protocol),
            new BlockPos(0, FloorY, 0),
            new BlockPos(0, FloorY + 4, 6));

        var engine = new PlayerPhysics(planning, PhysicsProfile.ForProtocol(Protocol));
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(0.5, FloorY + 4.0, 0.5), 0f, 0f);
        for (int tick = 0; tick < 80; tick++)
            engine.Step(MovementInput.None);

        Assert.True(engine.State.OnGround);
        Assert.Equal(FloorY + 1.5, engine.State.Position.Y, 6);
    }

    /// <summary>Drops a player onto a one-block-thick layer of <paramref name="blockName"/> and settles.</summary>
    private static double RestHeightOn(string blockName)
    {
        ShapedWorld world = Ground();
        world.Fill(-4, FloorY + 1, -4, 4, FloorY + 1, 8, blockName);

        var engine = NewEngine(world, new Vec3d(0.5, FloorY + 4.0, 0.5), yaw: 0f);
        for (int tick = 0; tick < 80; tick++)
            engine.Step(MovementInput.None);

        Assert.True(engine.State.OnGround, "the player never landed");
        return engine.State.Position.Y;
    }

    /// <summary>Walks toward -Z (yaw 180) from <paramref name="startZ"/> and returns where it ends up.</summary>
    private static Vec3d WalkSouthward(ShapedWorld world, double startZ)
        => Walk(world, new Vec3d(0.5, FloorY + 1.0, startZ), yaw: 180f);

    /// <summary>Walks toward +Z (yaw 0) from z = 0.5 and returns where it ends up.</summary>
    private static Vec3d WalkNorthward(ShapedWorld world, double startX)
        => Walk(world, new Vec3d(startX, FloorY + 1.0, 0.5), yaw: 0f);

    private static Vec3d Walk(ShapedWorld world, Vec3d start, float yaw)
    {
        var engine = NewEngine(world, start, yaw);
        var forward = new MovementInput { Forward = true };
        for (int tick = 0; tick < 120; tick++)
            engine.Step(forward);

        return engine.State.Position;
    }

    private static PlayerPhysics NewEngine(ShapedWorld world, Vec3d start, float yaw)
    {
        var engine = new PlayerPhysics(world, PhysicsProfile.ForProtocol(Protocol));
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(start, yaw, 0f);
        return engine;
    }

    private static ShapedWorld Ground()
    {
        var world = new ShapedWorld();
        world.Fill(-4, FloorY, -4, 4, FloorY, 12, "minecraft:stone");
        return world;
    }

    private static int DefaultStateId(Registry<BlockDefinition> blocks, string blockName)
    {
        Assert.True(
            blocks.TryGetValue(Identifier.Parse(blockName), out BlockDefinition? definition),
            $"protocol {Protocol} has no block {blockName}");
        return definition.DefaultStateId;
    }

    /// <summary>A sparse voxel world whose block table and collision shapes both come from the generated per-version data: the same <see cref="RegistryBlockDataSource"/> the client installs and the same <see cref="IBlockShapeSource"/> the client now defaults to. Blocks are named, and placed in their vanilla default state.</summary>
    private sealed class ShapedWorld : IPhysicsWorldView
    {
        private readonly Registry<BlockDefinition> _blocks = JavaGameData.Registries(Protocol).Blocks;
        private readonly IBlockShapeSource _shapes = JavaGameData.BlockShapes(Protocol);
        private readonly RegistryBlockDataSource _data;
        private readonly Dictionary<BlockPos, int> _states = [];

        public ShapedWorld() => _data = new RegistryBlockDataSource(_blocks, isLegacy: false);

        public void Set(int x, int y, int z, string blockName)
            => _states[new BlockPos(x, y, z)] = DefaultStateId(_blocks, blockName);

        public void Fill(int x1, int y1, int z1, int x2, int y2, int z2, string blockName)
        {
            int state = DefaultStateId(_blocks, blockName);
            for (int x = x1; x <= x2; x++)
                for (int y = y1; y <= y2; y++)
                    for (int z = z1; z <= z2; z++)
                        _states[new BlockPos(x, y, z)] = state;

        }

        public BlockState GetBlock(BlockPos pos)
            => new(_data, _states.TryGetValue(pos, out int state) ? state : 0);

        public ReadOnlySpan<Aabb> GetCollisionShapes(BlockState state) => _shapes.GetCollisionShapes(state);

        public bool IsChunkLoaded(BlockPos pos) => true;
    }
}
