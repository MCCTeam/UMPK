using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Physics;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>A bottom slab rests a falling player half a block up, on EVERY supported protocol.</summary>
/// <remarks>
/// <para>Every committed dataset carries the <c>[0,0,0,1,0.5,1]</c> collision box for a bottom slab. The same fall must therefore stop at y+0.5 on all supported protocols.</para>
/// <para>This test runs the same fall on all 50 protocols. A player already at rest inside a block that later becomes solid is a separate update-order case.</para>
/// <para>Pre-flattening bands select the slab by numeric block ID 44. Block ID 43 is the full-cube double slab. This keeps the geometry test independent from legacy identifier aliases.</para>
/// </remarks>
public sealed class SlabRestCompatibilityTests
{
    private const int FloorY = 63;

    private static readonly int[] All =
    [
        47, 107, 108, 109, 110, 210, 315, 316, 335, 338, 340, 393, 401, 404,
        477, 480, 485, 490, 498, 573, 575, 578,
        735, 736, 751, 753, 754, 755, 756, 757, 758,
        759, 760, 761, 762, 763, 764, 765, 766, 767,
        768, 769, 770, 771, 772, 773, 774, 775, 776, 777,
    ];

    /// <summary>The literal table's protocol column, read by <c>AllProtocolTableCoverageTests</c>.</summary>
    public static IReadOnlyList<int> Protocols() => All;

    public static TheoryData<int> AllProtocols
    {
        get
        {
            var data = new TheoryData<int>();
            foreach (int protocol in All)
                data.Add(protocol);

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AllProtocols))]
    public void A_bottom_slab_rests_the_player_half_a_block_up(int protocol)
    {
        double onSlab = RestHeightOnSlab(protocol);
        double onStone = RestHeightOn(protocol, "minecraft:stone");

        Assert.Equal(FloorY + 1.5, onSlab, 6);
        Assert.Equal(FloorY + 2.0, onStone, 6);
        Assert.True(onSlab < onStone, "the slab was as tall as a full block");
        Assert.True(onSlab > FloorY + 1.0, "the player rested inside the slab, not on top of it");
    }

    /// <summary>The eleven protocols covered by the live run, named explicitly so a failure identifies the version rather than only its number.</summary>
    [Theory]
    [InlineData(47, "1.8")]
    [InlineData(107, "1.9")]
    [InlineData(340, "1.12.2")]
    [InlineData(404, "1.13.2")]
    [InlineData(754, "1.16.5")]
    [InlineData(758, "1.18.2")]
    [InlineData(760, "1.19.2")]
    [InlineData(765, "1.20.3 and 1.20.4")]
    [InlineData(767, "1.21.1")]
    [InlineData(769, "1.21.4")]
    [InlineData(776, "26.2")]
    public void TheVersionsTheLiveRunReportedFailing_RestOnTheSlabHere(int protocol, string version)
    {
        double onSlab = RestHeightOnSlab(protocol);
        Assert.True(
            Math.Abs(onSlab - (FloorY + 1.5)) < 1e-6,
            $"{version} (protocol {protocol}) rested at {onSlab}, not {FloorY + 1.5}");
    }

    /// <summary>The vanilla pre-flattening block id of the stone slab; 43 is its double-slab sibling.</summary>
    private const int LegacyStoneSlabBlockId = 44;

    /// <summary>The flattened name of 1.12's stone-variant slab. 1.13 renamed it and gave <c>stone_slab</c> to a different block, so the 393-404 datasets carry both and the older name is the right one there.</summary>
    private static string SlabName(int protocol) =>
        protocol < 477 ? "minecraft:stone_slab" : "minecraft:smooth_stone_slab";

    /// <summary>Drops a player onto a one-block layer of bottom stone slab and settles.</summary>
    private static double RestHeightOnSlab(int protocol) =>
        protocol < 393
            ? RestHeightOn(protocol, LegacyStoneSlabBlockId << 4)
            : RestHeightOn(protocol, SlabName(protocol));

    /// <summary>Drops a player onto a one-block layer of <paramref name="blockName"/> and settles.</summary>
    private static double RestHeightOn(int protocol, string blockName) =>
        RestHeightOn(protocol, new ShapedWorld(protocol).StateOf(blockName));

    /// <summary>Drops a player onto a one-block layer of an explicit block-state id and settles.</summary>
    private static double RestHeightOn(int protocol, int stateId)
    {
        var world = new ShapedWorld(protocol);
        world.Fill(-4, FloorY, -4, 4, FloorY, 4, world.StateOf("minecraft:stone"));
        world.Fill(-4, FloorY + 1, -4, 4, FloorY + 1, 4, stateId);

        var engine = new PlayerPhysics(world, PhysicsProfile.ForProtocol(protocol));
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(0.5, FloorY + 4.0, 0.5), 0f, 0f);
        for (int tick = 0; tick < 80; tick++)
            engine.Step(MovementInput.None);

        Assert.True(engine.State.OnGround, $"the player never landed on state {stateId} at protocol {protocol}");
        return engine.State.Position.Y;
    }

    /// <summary>A sparse voxel world over one protocol's generated block registry and collision-shape table: the same two sources the client installs at runtime.</summary>
    private sealed class ShapedWorld : IPhysicsWorldView
    {
        private readonly Registry<BlockDefinition> _blocks;
        private readonly IBlockShapeSource _shapes;
        private readonly RegistryBlockDataSource _data;
        private readonly Dictionary<BlockPos, int> _states = [];
        private readonly int _protocol;

        public ShapedWorld(int protocol)
        {
            _protocol = protocol;
            _blocks = JavaGameData.Registries(protocol).Blocks;
            _shapes = JavaGameData.BlockShapes(protocol);
            _data = new RegistryBlockDataSource(_blocks, isLegacy: protocol < 393);
        }

        public int StateOf(string blockName) => DefaultStateId(blockName);

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

        private int DefaultStateId(string blockName)
        {
            Assert.True(
                _blocks.TryGetValue(Identifier.Parse(blockName), out BlockDefinition? definition),
                $"protocol {_protocol} has no block {blockName}");
            return definition.DefaultStateId;
        }
    }
}
