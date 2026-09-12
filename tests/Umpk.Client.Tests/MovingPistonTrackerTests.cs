using Umpk.Client.Internal;
using Umpk.Client.Navigation;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>How many moving block entities one block event creates, which is the seam between <see cref="PistonStructureResolver"/> and the physics tick.</summary>
/// <remarks>The retraction cases are here because they are the only place with a branch that could move a player in an invalid direction. A sticky piston retracting PULLS the block two ahead of it back toward itself, subject to four conditions. Each negative below sets up a fixture where exactly one of them is false and asserts the pull does not happen. Two of the four (the piston-is- sticky check and the contract-not-drop check) also discriminate <c>ShouldPullOnRetract</c>'s own clause for that condition; the other two (air, reaction) are correct end-to-end assertions but do NOT discriminate their own clause, because <see cref="PistonStructureResolver"/> independently refuses the same cases - see each test's own remarks and <c>MovingPistonTracker.ShouldPullOnRetract</c>'s.</remarks>
public sealed class MovingPistonTrackerTests
{
    private const int Protocol = 774;

    private static readonly BlockPos Piston = new(4, 64, 8);

    /// <summary>Piston event actions: extend, contract, and drop.</summary>
    private const int Extend = 0;
    private const int Contract = 1;
    private const int Drop = 2;

    [Fact]
    public void An_extension_over_a_stone_block_creates_the_head_and_the_stone()
    {
        Fixture world = Fixture.Create();
        world.SetPiston(Piston, Direction.East, "minecraft:piston");
        world.Set(5, 64, 8, "minecraft:stone");

        MovingPistonTracker tracker = world.Tracker();
        tracker.OnBlockEvent(Piston, Extend, (int)Direction.East);
        Assert.Equal(2, tracker.ActiveCount);
    }

    /// <summary>The anvil's reaction is BLOCK, so <c>resolve</c> fails and the head is the ONLY entity. The head is still created because the server sends the block event only when its own resolution succeeded, so refusing it too would turn a data gap into a missed push.</summary>
    [Fact]
    public void An_extension_into_an_anvil_creates_only_the_head()
    {
        Fixture world = Fixture.Create();
        world.SetPiston(Piston, Direction.East, "minecraft:piston");
        world.Set(5, 64, 8, "minecraft:anvil");

        MovingPistonTracker tracker = world.Tracker();
        tracker.OnBlockEvent(Piston, Extend, (int)Direction.East);
        Assert.Equal(1, tracker.ActiveCount);
    }

    /// <summary>A resolved block with NO collision boxes pushes nothing however long it lives, so it is not tracked. A torch is NORMAL-reaction on the way there, so this is not the BLOCK arm again.</summary>
    [Fact]
    public void A_pushed_block_with_no_collision_shape_is_not_tracked()
    {
        Fixture world = Fixture.Create();
        world.SetPiston(Piston, Direction.East, "minecraft:piston");
        world.Set(5, 64, 8, "minecraft:cobweb");
        Assert.Empty(JavaGameData.BlockShapes(Protocol).GetCollisionShapes(world.State("minecraft:cobweb")).ToArray());

        MovingPistonTracker tracker = world.Tracker();
        tracker.OnBlockEvent(Piston, Extend, (int)Direction.East);
        Assert.Equal(1, tracker.ActiveCount);
    }

    [Fact]
    public void A_sticky_retraction_pulls_the_block_two_ahead()
    {
        Fixture world = Fixture.Create();
        world.SetPiston(Piston, Direction.East, "minecraft:sticky_piston", extended: true);
        world.SetPistonHead(new BlockPos(5, 64, 8), Direction.East);
        world.Set(6, 64, 8, "minecraft:stone");

        MovingPistonTracker tracker = world.Tracker();
        tracker.OnBlockEvent(Piston, Contract, (int)Direction.East);
        Assert.Equal(2, tracker.ActiveCount);
    }

    /// <summary>The gate's first condition. An ordinary piston has no sticky face and pulls NOTHING, so this is the case where creating a moving block would push a player in a direction vanilla would not.</summary>
    [Fact]
    public void A_plain_piston_retracting_pulls_nothing()
    {
        Fixture world = Fixture.Create();
        world.SetPiston(Piston, Direction.East, "minecraft:piston", extended: true);
        world.SetPistonHead(new BlockPos(5, 64, 8), Direction.East);
        world.Set(6, 64, 8, "minecraft:stone");

        MovingPistonTracker tracker = world.Tracker();
        tracker.OnBlockEvent(Piston, Contract, (int)Direction.East);
        Assert.Equal(1, tracker.ActiveCount);
    }

    /// <summary>The gate's second condition: <c>TRIGGER_DROP</c> retracts the head without pulling, even on a sticky piston.</summary>
    [Fact]
    public void A_sticky_piston_dropping_pulls_nothing()
    {
        Fixture world = Fixture.Create();
        world.SetPiston(Piston, Direction.East, "minecraft:sticky_piston", extended: true);
        world.SetPistonHead(new BlockPos(5, 64, 8), Direction.East);
        world.Set(6, 64, 8, "minecraft:stone");

        MovingPistonTracker tracker = world.Tracker();
        tracker.OnBlockEvent(Piston, Drop, (int)Direction.East);
        Assert.Equal(1, tracker.ActiveCount);
    }

    /// <summary>An end-to-end assertion, NOT a discriminator for <c>ShouldPullOnRetract</c>'s own reaction check: glazed terracotta is PUSH_ONLY, and vanilla leaves it exactly where it is. Verified by disabling that specific clause and re-running this suite: the count stays 1, because <c>PistonStructureResolver.Resolve()</c> independently checks this same position and also refuses push-only blocks during retraction. What WOULD turn this red is removing <c>AddPushedBlocks</c>'s call into the resolver entirely, or breaking the resolver's own PUSH_ONLY handling - see <c>MovingPistonTracker.ShouldPullOnRetract</c>'s remarks for the full accounting of which of its four clauses this test does and does not pin down.</summary>
    [Fact]
    public void A_sticky_retraction_does_not_pull_a_non_normal_reaction()
    {
        Fixture world = Fixture.Create();
        world.SetPiston(Piston, Direction.East, "minecraft:sticky_piston", extended: true);
        world.SetPistonHead(new BlockPos(5, 64, 8), Direction.East);
        world.Set(6, 64, 8, "minecraft:white_glazed_terracotta");

        MovingPistonTracker tracker = world.Tracker();
        tracker.OnBlockEvent(Piston, Contract, (int)Direction.East);
        Assert.Equal(1, tracker.ActiveCount);
    }

    /// <summary>An end-to-end assertion, NOT a discriminator for <c>ShouldPullOnRetract</c>'s own air check. Verified by disabling that specific clause (together with the reaction clause, so the call actually reaches the resolver) and re-running this suite: the count stays 1, because <c>PistonStructureResolver</c>'s own <c>AddBlockLine</c> treats air as "nothing here, stop" before it ever calls <c>isPushable</c>, independently of anything <c>ShouldPullOnRetract</c> decided. What WOULD turn this red is removing <c>AddPushedBlocks</c>'s call into the resolver entirely.</summary>
    [Fact]
    public void A_sticky_retraction_over_air_pulls_nothing()
    {
        Fixture world = Fixture.Create();
        world.SetPiston(Piston, Direction.East, "minecraft:sticky_piston", extended: true);
        world.SetPistonHead(new BlockPos(5, 64, 8), Direction.East);

        MovingPistonTracker tracker = world.Tracker();
        tracker.OnBlockEvent(Piston, Contract, (int)Direction.East);
        Assert.Equal(1, tracker.ActiveCount);
    }

    /// <summary>One extend event can add at most 13 entities: 1 head plus <c>PistonStructureResolver.MaxPushDepth</c> pushed blocks. Six independent pistons, each pushing a full 12-block line, offer 6 * 13 = 78 potential entities in one burst with nothing retiring in between (no tick is run), well past <c>MaxTracked</c>. The list must never grow past the ceiling. Removing the cap in <c>MovingPistonTracker.Add</c> turns this from 64 into 78 and fails the assertion.</summary>
    [Fact]
    public void The_tracked_list_never_grows_past_the_ceiling_even_when_one_burst_would_add_more()
    {
        const int rows = 6; // 6 * 13 = 78 potential entities > MaxTracked.
        Fixture world = Fixture.Create();
        for (int row = 0; row < rows; row++)
        {
            var piston = new BlockPos(0, 64, row);
            world.SetPiston(piston, Direction.East, "minecraft:piston");
            for (int i = 0; i < 12; i++)
                world.Set(1 + i, 64, row, "minecraft:stone");

        }

        MovingPistonTracker tracker = world.Tracker();
        for (int row = 0; row < rows; row++)
            tracker.OnBlockEvent(new BlockPos(0, 64, row), Extend, (int)Direction.East);

        Assert.Equal(64, tracker.ActiveCount);
    }

    private sealed class Fixture
    {
        private readonly IBlockDataSource _data;

        private Fixture(Umpk.Game.World.World world, IBlockDataSource data)
        {
            World = world;
            _data = data;
        }

        public Umpk.Game.World.World World { get; }

        public static Fixture Create()
        {
            Registry<BlockDefinition> blocks = JavaGameData.Registries(Protocol).Blocks;
            var data = new RegistryBlockDataSource(blocks, isLegacy: false);
            var dimensionTypes = Registry.FromEntries(
                RegistryIds.DimensionType,
                [0],
                [Identifier.Minecraft("overworld")],
                [new DimensionTypeDefinition(-64, 384, true)]);
            var dimension = new DimensionState(dimensionTypes[0], Identifier.Minecraft("overworld"));
            var biomes = new RegistryBuilder<BiomeDefinition>(RegistryIds.Biome, 1)
                .Add(0, Identifier.Minecraft("plains"), new BiomeDefinition())
                .Build();
            var world = new Umpk.Game.World.World(dimension, data, biomes);
            world.LoadColumn(new ChunkPos(0, 0));
            return new Fixture(world, data);
        }

        public MovingPistonTracker Tracker() => new(
            JavaGameData.BlockShapes(Protocol),
            JavaGameData.BlockPushData(Protocol),
            () => World,
            WorldBorderState.ContainmentEraForProtocol(Protocol));

        public int State(string block)
        {
            Assert.True(_data.Blocks.TryGet(Identifier.Parse(block), out RegistryEntry<BlockDefinition> entry), block);
            return entry.Value.DefaultStateId;
        }

        public void Set(int x, int y, int z, string block) =>
            World.SetBlockStateId(new BlockPos(x, y, z), State(block));

        public void SetPiston(BlockPos pos, Direction facing, string block, bool extended = false) =>
            World.SetBlockStateId(
                pos, StateWith(block, ("facing", Name(facing)), ("extended", extended ? "true" : "false")));

        public void SetPistonHead(BlockPos pos, Direction facing) =>
            World.SetBlockStateId(pos, StateWith("minecraft:piston_head", ("facing", Name(facing)), ("short", "false")));

        private int StateWith(string block, params (string Property, string Value)[] wanted)
        {
            Assert.True(_data.Blocks.TryGet(Identifier.Parse(block), out RegistryEntry<BlockDefinition> entry), block);
            for (int state = entry.Value.MinStateId; state <= entry.Value.MaxStateId; state++)
            {
                bool all = true;
                foreach ((string property, string value) in wanted)
                    if (!_data.TryGetPropertyValue(state, property, out string got) || got != value)
                    {
                        all = false;
                        break;
                    }

                if (all)
                    return state;

            }

            Assert.Fail($"no {block} state with the requested properties");
            return 0;
        }

        private static string Name(Direction direction) => direction switch
        {
            Direction.Down => "down",
            Direction.Up => "up",
            Direction.North => "north",
            Direction.South => "south",
            Direction.West => "west",
            _ => "east",
        };
    }
}
