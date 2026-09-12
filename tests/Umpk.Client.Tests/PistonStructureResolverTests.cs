using Umpk.Client.Internal;
using Umpk.Client.Navigation;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>The piston structure resolver, driven over a real <see cref="Umpk.Game.World.World"/> built on a real per-version block table and the real measured piston data.</summary>
/// <remarks>Every case here exercises pushability, and every one of them needs a value from <c>block-push.json</c> that is NOT the default: a table of zeroes would push the anvil, push the chest, carry the leaves instead of breaking them and push the glazed terracotta sideways.</remarks>
public sealed class PistonStructureResolverTests
{
    /// <summary>1.21.11: measured, and the protocol the batch-12 piston work was verified live on.</summary>
    private const int Protocol = 774;

    /// <summary>1.13.2: flattened and not measured.</summary>
    private const int UnmeasuredProtocol = 404;

    private static readonly BlockPos Piston = new(4, 64, 8);

    [Fact]
    public void A_line_of_two_normal_blocks_is_pushed_in_vanilla_order()
    {
        Fixture world = Fixture.Create(Protocol);
        world.SetPiston(Piston, Direction.East);
        world.Set(5, 64, 8, "minecraft:stone");
        world.Set(6, 64, 8, "minecraft:dirt");

        PistonStructureResolver resolver = world.Extend(Piston, Direction.East);
        Assert.True(resolver.Resolve());

        // addBlockLine adds the origin first, then walks forward: {5,6}.
        Assert.Equal([new BlockPos(5, 64, 8), new BlockPos(6, 64, 8)], resolver.ToPush);
        Assert.Empty(resolver.ToDestroy);
        Assert.Equal(Direction.East, resolver.PushDirection);
    }

    /// <summary><c>isPushable</c>'s BLOCK arm returns false, and <c>addBlockLine</c>'s forward walk turns that into a failed resolution: NOTHING moves, not even the blocks in front of the anvil.</summary>
    [Fact]
    public void An_anvil_in_the_line_fails_the_whole_resolution()
    {
        Fixture world = Fixture.Create(Protocol);
        world.SetPiston(Piston, Direction.East);
        world.Set(5, 64, 8, "minecraft:stone");
        world.Set(6, 64, 8, "minecraft:anvil");

        Assert.False(world.Extend(Piston, Direction.East).Resolve());
    }

    /// <summary>Obsidian's push reaction is NORMAL. It is refused by NAME in <c>isPushable</c>, which is why the resolver cannot be driven off the reaction table alone.</summary>
    [Fact]
    public void Obsidian_fails_the_resolution_even_though_its_reaction_is_normal()
    {
        Assert.Equal(
            PistonPushReaction.Normal,
            Reaction(JavaGameData.BlockPushData(Protocol), "minecraft:obsidian"));

        Fixture world = Fixture.Create(Protocol);
        world.SetPiston(Piston, Direction.East);
        world.Set(5, 64, 8, "minecraft:stone");
        world.Set(6, 64, 8, "minecraft:obsidian");

        Assert.False(world.Extend(Piston, Direction.East).Resolve());
    }

    /// <summary>A chest's reaction is NORMAL and it is breakable, so the ONLY thing that stops it being pushed is the resolver's refusal to push a state with a block entity.</summary>
    [Fact]
    public void A_block_entity_fails_the_resolution_despite_a_normal_reaction()
    {
        IBlockPushSource push = JavaGameData.BlockPushData(Protocol);
        Assert.Equal(PistonPushReaction.Normal, Reaction(push, "minecraft:chest"));
        Assert.True(push.TryGet(Identifier.Minecraft("chest"), out BlockPushInfo chest) && chest.HasBlockEntity);

        Fixture world = Fixture.Create(Protocol);
        world.SetPiston(Piston, Direction.East);
        world.Set(5, 64, 8, "minecraft:stone");
        world.Set(6, 64, 8, "minecraft:chest");

        Assert.False(world.Extend(Piston, Direction.East).Resolve());
    }

    /// <summary>A DESTROY block ahead of the line is BROKEN, not carried: <c>addBlockLine</c> puts it in <c>toDestroy</c> and stops. Vanilla creates no moving block entity for it, so a player beside it must not be pushed by it.</summary>
    [Fact]
    public void Leaves_ahead_of_the_line_are_destroyed_and_not_pushed()
    {
        Fixture world = Fixture.Create(Protocol);
        world.SetPiston(Piston, Direction.East);
        world.Set(5, 64, 8, "minecraft:stone");
        world.Set(6, 64, 8, "minecraft:oak_leaves");

        PistonStructureResolver resolver = world.Extend(Piston, Direction.East);
        Assert.True(resolver.Resolve());
        Assert.Equal([new BlockPos(5, 64, 8)], resolver.ToPush);
        Assert.Equal([new BlockPos(6, 64, 8)], resolver.ToDestroy);
    }

    /// <summary>A DESTROY block AT the start position takes <c>resolve</c>'s own early arm: the resolution succeeds with an EMPTY push list.</summary>
    [Fact]
    public void Leaves_at_the_start_position_leave_nothing_to_push()
    {
        Fixture world = Fixture.Create(Protocol);
        world.SetPiston(Piston, Direction.East);
        world.Set(5, 64, 8, "minecraft:oak_leaves");

        PistonStructureResolver resolver = world.Extend(Piston, Direction.East);
        Assert.True(resolver.Resolve());
        Assert.Empty(resolver.ToPush);
        Assert.Equal([new BlockPos(5, 64, 8)], resolver.ToDestroy);
    }

    /// <summary>PUSH_ONLY is <c>pushDirection == facing</c>. Pushed along its own axis the glazed terracotta moves; the SAME block reached from a perpendicular branch does not.</summary>
    [Fact]
    public void Glazed_terracotta_is_pushable_along_the_push_direction_only()
    {
        Fixture world = Fixture.Create(Protocol);
        world.SetPiston(Piston, Direction.East);
        world.Set(5, 64, 8, "minecraft:white_glazed_terracotta");

        PistonStructureResolver straight = world.Extend(Piston, Direction.East);
        Assert.True(straight.Resolve());
        Assert.Equal([new BlockPos(5, 64, 8)], straight.ToPush);

        // Same block, reached through a slime block's perpendicular branch. addBranchingBlocks calls addBlockLine(side, direction) with the BRANCH direction, and isPushable is handed that as its `facing`, so pushDirection (EAST) != facing (UP) and the terracotta is left behind.
        Fixture branch = Fixture.Create(Protocol);
        branch.SetPiston(Piston, Direction.East);
        branch.Set(5, 64, 8, "minecraft:slime_block");
        branch.Set(5, 65, 8, "minecraft:white_glazed_terracotta");

        PistonStructureResolver sideways = branch.Extend(Piston, Direction.East);
        Assert.True(sideways.Resolve());
        Assert.Equal([new BlockPos(5, 64, 8)], sideways.ToPush);
    }

    /// <summary>The sticky branch: <c>resolve</c> re-walks its own push list and recurses out of every slime block through <c>addBranchingBlocks</c>.</summary>
    [Fact]
    public void A_slime_block_drags_its_perpendicular_neighbour()
    {
        Fixture world = Fixture.Create(Protocol);
        world.SetPiston(Piston, Direction.East);
        world.Set(5, 64, 8, "minecraft:slime_block");
        world.Set(5, 65, 8, "minecraft:stone");

        PistonStructureResolver resolver = world.Extend(Piston, Direction.East);
        Assert.True(resolver.Resolve());
        Assert.Contains(new BlockPos(5, 65, 8), resolver.ToPush);

        // The control: an ordinary block does not drag its neighbour.
        Fixture plain = Fixture.Create(Protocol);
        plain.SetPiston(Piston, Direction.East);
        plain.Set(5, 64, 8, "minecraft:stone");
        plain.Set(5, 65, 8, "minecraft:stone");

        PistonStructureResolver control = plain.Extend(Piston, Direction.East);
        Assert.True(control.Resolve());
        Assert.Equal([new BlockPos(5, 64, 8)], control.ToPush);
    }

    /// <summary>Honey and slime refuse to stick to EACH OTHER (<c>canStickToEachOther</c>, ) while sticking to everything else. Two honey blocks drag; honey beside slime does not.</summary>
    [Fact]
    public void Honey_and_slime_do_not_stick_to_each_other()
    {
        Fixture sticks = Fixture.Create(Protocol);
        sticks.SetPiston(Piston, Direction.East);
        sticks.Set(5, 64, 8, "minecraft:honey_block");
        sticks.Set(5, 65, 8, "minecraft:honey_block");
        PistonStructureResolver dragged = sticks.Extend(Piston, Direction.East);
        Assert.True(dragged.Resolve());
        Assert.Contains(new BlockPos(5, 65, 8), dragged.ToPush);

        Fixture repels = Fixture.Create(Protocol);
        repels.SetPiston(Piston, Direction.East);
        repels.Set(5, 64, 8, "minecraft:honey_block");
        repels.Set(5, 65, 8, "minecraft:slime_block");
        PistonStructureResolver left = repels.Extend(Piston, Direction.East);
        Assert.True(left.Resolve());
        Assert.DoesNotContain(new BlockPos(5, 65, 8), left.ToPush);
    }

    /// <summary><c>MAX_PUSH_DEPTH</c> is 12, and 13 is one too many. Both sides asserted, so a resolver with no limit and a resolver with an off-by-one limit both fail.</summary>
    [Fact]
    public void Twelve_blocks_push_and_thirteen_do_not()
    {
        Fixture twelve = Fixture.Create(Protocol);
        twelve.SetPiston(Piston, Direction.East);
        for (int i = 0; i < 12; i++)
            twelve.Set(5 + i, 64, 8, "minecraft:stone");

        PistonStructureResolver ok = twelve.Extend(Piston, Direction.East);
        Assert.True(ok.Resolve());
        Assert.Equal(12, ok.ToPush.Count);

        Fixture thirteen = Fixture.Create(Protocol);
        thirteen.SetPiston(Piston, Direction.East);
        for (int i = 0; i < 13; i++)
            thirteen.Set(5 + i, 64, 8, "minecraft:stone");

        Assert.False(thirteen.Extend(Piston, Direction.East).Resolve());
    }

    /// <summary>A protocol the dataset never measured must resolve NOTHING, whatever the world looks like. The same fixture on a measured band pushes, which is what makes this a discriminator rather than a test that passes because the fixture is empty.</summary>
    [Fact]
    public void An_unmeasured_protocol_resolves_nothing()
    {
        Fixture measured = Fixture.Create(Protocol);
        measured.SetPiston(Piston, Direction.East);
        measured.Set(5, 64, 8, "minecraft:stone");
        Assert.True(measured.Extend(Piston, Direction.East).Resolve());

        Fixture unmeasured = Fixture.Create(UnmeasuredProtocol);
        unmeasured.SetPiston(Piston, Direction.East);
        unmeasured.Set(5, 64, 8, "minecraft:stone");
        Assert.False(unmeasured.Extend(Piston, Direction.East).Resolve());
    }

    /// <summary>A column the client was never sent is UNKNOWN, not air. Reading it as air would truncate the structure and under-push silently; the resolver refuses the whole resolution instead.</summary>
    [Fact]
    public void An_unloaded_column_refuses_the_resolution()
    {
        // The piston sits two blocks from the +x chunk boundary, so the line runs off the loaded column. Only the piston's own column is loaded.
        var piston = new BlockPos(14, 64, 8);
        Fixture world = Fixture.Create(Protocol);
        world.SetPiston(piston, Direction.East);
        world.Set(15, 64, 8, "minecraft:stone");

        Assert.Null(world.World.GetColumn(new BlockPos(16, 64, 8)));
        Assert.False(world.Extend(piston, Direction.East).Resolve());

        // Control: the same geometry with the next column loaded resolves.
        world.World.LoadColumn(new ChunkPos(1, 0));
        Assert.True(world.Extend(piston, Direction.East).Resolve());
    }

    /// <summary><c>Registries.Definitions.DimensionTypeDefinition.MaxY</c> is documented as the EXCLUSIVE upper Y bound (<c>MinY + Height</c>). The protocol's maximum buildable Y is INCLUSIVE. The fixture's dimension is MinY=-64, Height=384, so <c>Dimension.MaxY</c> is 320 and the top buildable block is y=319. A block sitting on that top row, pushed UP, must be refused. A resolver that instead compares against the EXCLUSIVE bound lets y=319 through and parks a moving block at y=320, one row above the world. Resolution must clamp the structure to the inclusive build-height boundary.</summary>
    [Fact]
    public void A_block_at_the_top_row_refuses_to_be_pushed_up()
    {
        Fixture world = Fixture.Create(Protocol);
        var piston = new BlockPos(4, 318, 8);
        world.SetPiston(piston, Direction.Up);
        world.Set(4, 319, 8, "minecraft:stone");

        Assert.False(world.Extend(piston, Direction.Up).Resolve());
    }

    /// <summary>The control one row down: the identical push, one block lower, still succeeds.</summary>
    [Fact]
    public void A_block_one_row_below_the_top_is_still_pushed_up()
    {
        Fixture world = Fixture.Create(Protocol);
        var piston = new BlockPos(4, 317, 8);
        world.SetPiston(piston, Direction.Up);
        world.Set(4, 318, 8, "minecraft:stone");

        PistonStructureResolver resolver = world.Extend(piston, Direction.Up);
        Assert.True(resolver.Resolve());
        Assert.Equal([new BlockPos(4, 318, 8)], resolver.ToPush);
    }

    /// <summary>The floor's mirror image, named and pinned alongside the ceiling cases above: MinY is already correctly INCLUSIVE on both sides of the comparison, so this was never broken, but it is the control that proves the ceiling fix did not flip the floor.</summary>
    [Fact]
    public void A_block_at_the_bottom_row_refuses_to_be_pushed_down()
    {
        Fixture world = Fixture.Create(Protocol);
        var piston = new BlockPos(4, -63, 8);
        world.SetPiston(piston, Direction.Down);
        world.Set(4, -64, 8, "minecraft:stone");

        Assert.False(world.Extend(piston, Direction.Down).Resolve());
    }

    /// <summary>The world border rejects an out-of-bounds position before checking whether the block there is air. A minigame or arena server routinely SHRINKS the border well inside spawn, which is the realistic case this covers, and is why "the border defaults to the vanilla maximum" was not a safe reason to skip it: a piston pushing a block that would land outside a narrowed border must refuse, exactly as it would against an anvil.</summary>
    [Fact]
    public void A_shrunk_border_refuses_a_push_that_would_cross_it()
    {
        Fixture world = Fixture.Create(Protocol);
        var piston = new BlockPos(4, 64, 8);
        world.SetPiston(piston, Direction.East);
        world.Set(5, 64, 8, "minecraft:stone");

        // Centered at x=0, diameter 10: bounds are the half-open box x in [-5, 5), z in [3, 13). The piston base at x=4 is inside (never border-checked anyway); the pushed block at x=5 is not, since the box is right-OPEN and 5 is exactly the excluded edge.
        world.World.SetBorder(new WorldBorderState(0, 8, 10, 10, 0, 5, 15));

        Assert.False(world.Extend(piston, Direction.East).Resolve());

        // Control: the identical fixture against the default (unshrunk) border still pushes, which is what makes the refusal above about the border and not about the fixture.
        Fixture control = Fixture.Create(Protocol);
        control.SetPiston(piston, Direction.East);
        control.Set(5, 64, 8, "minecraft:stone");
        Assert.True(control.Extend(piston, Direction.East).Resolve());
    }

    /// <summary>World-border inclusion is NOT one formula across this resolver's 17 measured protocols. 1.20.6 (protocols 498, 578, 755, 756, 759, 765, 766) use <c>(x + 1) &gt; minX &amp;&amp; x &lt; maxX</c>; 1.21 onward use <c>x &gt;= minX &amp;&amp; x &lt; maxX</c>. They agree whenever the border's own bounds are integers and disagree ONLY on the min-X/min-Z edge otherwise - an entirely ordinary <c>/worldborder</c> configuration (any odd size or off-center border). The concrete repro: a <c>size=101</c> border centered at the origin has <c>minX=minZ=-50.5</c>. Real 1.19 vanilla's own formula accepts a push at x=-51 (<c>-51+1 &gt; -50.5</c>); real 1.21.11 vanilla's does not (<c>-51 &gt;= -50.5</c> is false). A resolver using only the 1.21+ formula would refuse this push on EVERY protocol, including the 7 pre-1.21 ones where the server's own vanilla performs it - silently under-pushing, exactly what this resolver exists to avoid.</summary>
    [Fact]
    public void A_non_integer_border_pushes_its_own_min_edge_only_on_a_pre1_21_protocol()
    {
        const int PreEra = 759; // 1.19: world-border containment still uses (x + 1) > minX.

        Fixture legacy = Fixture.Create(PreEra);
        var piston = new BlockPos(-52, 64, 0);
        legacy.SetPiston(piston, Direction.East);
        legacy.Set(-51, 64, 0, "minecraft:stone");
        legacy.World.SetBorder(new WorldBorderState(0, 0, 101, 101, 0, 5, 15));

        Assert.True(legacy.Extend(piston, Direction.East).Resolve());
    }

    /// <summary>The paired control for <see cref="A_non_integer_border_pushes_its_own_min_edge_only_on_a_pre1_21_protocol"/>: identical coordinates and border on <see cref="Protocol"/> (774, 1.21.11, post-1.21) must be refused because the 1.21+ containment formula rejects x=-51. Together the cases prove the rule is era-specific rather than an unconditional wider boundary.</summary>
    [Fact]
    public void A_non_integer_border_refuses_the_same_min_edge_on_a_post1_21_protocol()
    {
        Fixture modern = Fixture.Create(Protocol);
        var piston = new BlockPos(-52, 64, 0);
        modern.SetPiston(piston, Direction.East);
        modern.Set(-51, 64, 0, "minecraft:stone");
        modern.World.SetBorder(new WorldBorderState(0, 0, 101, 101, 0, 5, 15));

        Assert.False(modern.Extend(piston, Direction.East).Resolve());
    }

    /// <summary>Retraction pulls the block two ahead of the piston BACK. The head still sits in the client's world copy at <c>piston + facing</c> and its reaction is BLOCK, so without vanilla's own "clear the head first" step, the resolution would refuse.</summary>
    [Fact]
    public void Retraction_pulls_the_block_two_ahead_past_the_head_it_clears()
    {
        Fixture world = Fixture.Create(Protocol);
        world.SetPiston(Piston, Direction.East, extended: true);
        world.SetPistonHead(new BlockPos(5, 64, 8), Direction.East);
        world.Set(6, 64, 8, "minecraft:stone");

        BlockPos head = Piston.Offset(Direction.East);
        PistonStructureResolver pulled = world.Retract(Piston, Direction.East, head);
        Assert.True(pulled.Resolve());
        Assert.Equal([new BlockPos(6, 64, 8)], pulled.ToPush);
        Assert.Equal(Direction.West, pulled.PushDirection);

        // Without the cleared head the forward walk reaches piston_head, whose reaction is BLOCK, and refuses. This is the assertion that proves the parameter is load-bearing.
        Assert.False(world.Retract(Piston, Direction.East, clearedHead: null).Resolve());
    }

    /// <summary>Vanilla's own clear-the-head step (<c>moveBlocks:256</c>) is CONDITIONAL: During retraction an existing piston head is cleared before resolution. If the client's world copy holds something other than <c>piston_head</c> at that position, vanilla leaves it alone and resolves against the REAL block. An unconditional substitution would instead read a real, solid block as air: here a stone block sits at the would-be head position, so a resolver that clears unconditionally silently treats it as nothing and returns an empty-looking two-block success, while the correct answer is a refusal (the forward walk reaches the piston's own base through the real stone and fails as any block would).</summary>
    [Fact]
    public void ClearedHead_IsOnlyHonouredWhenTheRealBlockThereIsAPistonHead()
    {
        Fixture world = Fixture.Create(Protocol);
        world.SetPiston(Piston, Direction.East, extended: true);
        world.Set(5, 64, 8, "minecraft:stone"); // NOT piston_head at the position clearedHead names.
        world.Set(6, 64, 8, "minecraft:stone");

        BlockPos head = Piston.Offset(Direction.East);
        PistonStructureResolver pulled = world.Retract(Piston, Direction.East, head);
        Assert.False(pulled.Resolve());
    }

    private static PistonPushReaction Reaction(IBlockPushSource push, string name)
    {
        Assert.True(push.TryGet(Identifier.Parse(name), out BlockPushInfo info), name);
        return info.Reaction;
    }

    /// <summary>A world on one protocol's real block table, plus that protocol's measured push data.</summary>
    private sealed class Fixture
    {
        private readonly IBlockDataSource _data;
        private readonly IBlockPushSource _push;
        private readonly WorldBorderContainmentEra _borderEra;

        private Fixture(Umpk.Game.World.World world, IBlockDataSource data, IBlockPushSource push, WorldBorderContainmentEra borderEra)
        {
            World = world;
            _data = data;
            _push = push;
            _borderEra = borderEra;
        }

        public Umpk.Game.World.World World { get; }

        public static Fixture Create(int protocol)
        {
            Registry<BlockDefinition> blocks = JavaGameData.Registries(protocol).Blocks;
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
            return new Fixture(
                world,
                data,
                JavaGameData.BlockPushData(protocol),
                Umpk.Game.World.WorldBorderState.ContainmentEraForProtocol(protocol));
        }

        public void Set(int x, int y, int z, string block) =>
            World.SetBlockStateId(new BlockPos(x, y, z), DefaultState(block));

        public void SetPiston(BlockPos pos, Direction facing, bool extended = false) =>
            World.SetBlockStateId(pos, StateWith("minecraft:piston", ("facing", Name(facing)), ("extended", extended ? "true" : "false")));

        public void SetPistonHead(BlockPos pos, Direction facing) =>
            World.SetBlockStateId(pos, StateWith("minecraft:piston_head", ("facing", Name(facing)), ("short", "false")));

        public PistonStructureResolver Extend(BlockPos piston, Direction facing) =>
            new(World, _push, piston, facing, extending: true, _borderEra);

        public PistonStructureResolver Retract(BlockPos piston, Direction facing, BlockPos? clearedHead) =>
            new(World, _push, piston, facing, extending: false, _borderEra, clearedHead);

        private int DefaultState(string block)
        {
            Assert.True(_data.Blocks.TryGet(Identifier.Parse(block), out RegistryEntry<BlockDefinition> entry), block);
            return entry.Value.DefaultStateId;
        }

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
