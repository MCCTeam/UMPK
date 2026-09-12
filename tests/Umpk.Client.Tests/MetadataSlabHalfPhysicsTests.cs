using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Physics;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>On a pre-flattening band, a player that falls onto a TOP slab comes to rest a different height from one that falls onto a BOTTOM slab.</summary>
/// <remarks>
/// <para>This is the physics-level statement of the shape data that <c>MetadataCollisionShapeTests</c> pins in the data. The legacy shape table was keyed by block network id and resolved with <c>stateId &gt;&gt; 4</c>, so both halves of a slab answered the bottom slab's box and both rests landed on the same y. <c>SlabRestCompatibilityTests</c> could not see it: it asks only for <c>44 &lt;&lt; 4</c>, meta 0, which is exactly the variant the meta-0 extraction gets right.</para>
/// <para>A top slab has the box <c>(0,0.5,0,1,1,1)</c>, while a bottom slab has <c>(0,0,0,1,0.5,1)</c>. Metadata bit 3 selects the half. A player landing on a slab at <c>FloorY + 1</c> rests at <c>FloorY + 1.5</c> on the bottom half, and on the top half the box reaches the full block, which rests them at <c>FloorY + 2</c>.</para>
/// </remarks>
public sealed class MetadataSlabHalfPhysicsTests
{
    private const int FloorY = 63;

    /// <summary>Vanilla's pre-flattening stone slab; 43 is its full-cube double.</summary>
    private const int StoneSlab = 44;

    public static TheoryData<int> LegacyProtocols => [47, 107, 108, 109, 110, 210, 315, 316, 335, 338, 340];

    [Theory]
    [MemberData(nameof(LegacyProtocols))]
    public void A_top_slab_and_a_bottom_slab_rest_the_player_at_different_heights(int protocol)
    {
        double onBottom = RestHeightOn(protocol, (StoneSlab << 4) | 0);
        double onTop = RestHeightOn(protocol, (StoneSlab << 4) | 8);

        Assert.Equal(FloorY + 1.5, onBottom, 6);
        Assert.Equal(FloorY + 2.0, onTop, 6);
        Assert.True(
            onTop > onBottom,
            $"protocol {protocol}: the top slab rested the player at {onTop}, the bottom at {onBottom}");
    }

    /// <summary>And the top slab is not simply "a full cube by accident": a player standing on it is half a block higher than on the bottom half, and exactly level with one standing on a full block.</summary>
    [Theory]
    [MemberData(nameof(LegacyProtocols))]
    public void A_top_slab_rests_level_with_stone_and_half_a_block_above_a_bottom_slab(int protocol)
    {
        double onTop = RestHeightOn(protocol, (StoneSlab << 4) | 8);
        double onStone = RestHeightOn(protocol, 1 << 4);

        Assert.Equal(onStone, onTop, 6);
        Assert.Equal(0.5, onTop - RestHeightOn(protocol, (StoneSlab << 4) | 0), 6);
    }

    private static double RestHeightOn(int protocol, int stateId)
    {
        var world = new LegacyShapedWorld(protocol);
        world.Fill(-4, FloorY, -4, 4, FloorY, 4, 1 << 4);
        world.Fill(-4, FloorY + 1, -4, 4, FloorY + 1, 4, stateId);

        var engine = new PlayerPhysics(world, PhysicsProfile.ForProtocol(protocol));
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(0.5, FloorY + 4.0, 0.5), 0f, 0f);
        for (int tick = 0; tick < 80; tick++)
            engine.Step(MovementInput.None);

        Assert.True(engine.State.OnGround, $"the player never landed on state {stateId} at protocol {protocol}");
        return engine.State.Position.Y;
    }

    /// <summary>A sparse voxel world over one legacy band's block registry and shape table: the same two sources <c>UmpkClient</c> installs at runtime.</summary>
    private sealed class LegacyShapedWorld : IPhysicsWorldView
    {
        private readonly IBlockShapeSource _shapes;
        private readonly RegistryBlockDataSource _data;
        private readonly Dictionary<BlockPos, int> _states = [];

        public LegacyShapedWorld(int protocol)
        {
            _shapes = JavaGameData.BlockShapes(protocol);
            _data = new RegistryBlockDataSource(JavaGameData.Registries(protocol).Blocks, isLegacy: true);
        }

        public void Fill(int x1, int y1, int z1, int x2, int y2, int z2, int state)
        {
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
