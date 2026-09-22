using Umpk;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Protocol.Java;
using Xunit;

namespace Umpk.Data.Java.Tests;

/// <summary>Pins the per-block attributes the generated tables now carry: state-property names WITH their value domains, physics scalars, and per-state semantic flags.</summary>
/// <remarks>These assertions cover concrete values and states across several protocol bands, including property resolution, physics scalars, and distinctions between solid, fluid, climbable, and replaceable blocks.</remarks>
public sealed class BlockAttributeTests
{
    /// <summary>1.14, 1.16.5, 1.20.6, 1.21.5 and 26.2: the oldest band with property names and four later ones.</summary>
    public static TheoryData<int> BandsWithProperties => new() { 477, 754, 766, 770, 776 };

    /// <summary>1.13 and four later bands: every band that has kelp and seagrass at all.</summary>
    public static TheoryData<int> PostFlatteningBands => new() { 393, 477, 754, 766, 776 };

    /// <summary>Every band, including the pre-flattening and 1.13 ones that carry no property names.</summary>
    public static TheoryData<int> AllBands
    {
        get
        {
            TheoryData<int> data = [];
            foreach (JavaVersion version in JavaVersions.All)
                data.Add(version.Version.Protocol);

            return data;
        }
    }

    private static BlockDefinition Block(int protocol, string name)
    {
        Registry<BlockDefinition> blocks = JavaGameData.Registries(protocol).Blocks;
        Assert.True(
            blocks.TryGet(Identifier.Minecraft(name), out RegistryEntry<BlockDefinition> entry),
            $"protocol {protocol}: minecraft:{name} is not in the block registry.");
        return entry.Value;
    }

    private static string Property(int protocol, string block, int stateId, string property)
    {
        BlockDefinition definition = Block(protocol, block);
        Assert.True(
            definition.TryGetPropertyValue(stateId, property, out string value),
            $"protocol {protocol}: minecraft:{block} state {stateId} has no resolved '{property}'.");
        return value;
    }

    /// <summary>The protocol-763 state-id permutation for ladder facing and waterlogging.</summary>
    /// <remarks>These ids are literal wire observations rather than values produced by this library, so the test detects a consistently wrong encode/decode property ordering.</remarks>
    [Fact]
    public void The_state_ids_a_live_server_sent_decode_to_the_facings_the_server_named()
    {
        Assert.Equal("north", Property(763, "ladder", 4655, "facing"));
        Assert.Equal("false", Property(763, "ladder", 4655, "waterlogged"));
        Assert.Equal("west", Property(763, "ladder", 4659, "facing"));
        Assert.Equal("false", Property(763, "ladder", 4659, "waterlogged"));
    }

    [Theory]
    [MemberData(nameof(BandsWithProperties))]
    public void Wheat_Age_Resolves_Across_Its_Whole_Range(int protocol)
    {
        // Wheat has one property (age 0-7), so its state offset is its age.
        BlockDefinition wheat = Block(protocol, "wheat");
        Assert.Equal(8, wheat.StateCount);

        BlockPropertyDefinition age = Assert.Single(wheat.Properties);
        Assert.Equal("age", age.Name);
        Assert.Equal(["0", "1", "2", "3", "4", "5", "6", "7"], age.Values);

        Assert.Equal("0", Property(protocol, "wheat", wheat.MinStateId, "age"));
        Assert.Equal("3", Property(protocol, "wheat", wheat.MinStateId + 3, "age"));
        Assert.Equal("7", Property(protocol, "wheat", wheat.MaxStateId, "age"));

        // A property this block does not have must fail rather than answer.
        Assert.False(wheat.TryGetPropertyValue(wheat.MinStateId, "facing", out _));
    }

    [Theory]
    [MemberData(nameof(BandsWithProperties))]
    public void OakStairs_Multi_Property_State_Decomposes(int protocol)
    {
        // Four properties of sizes 4 x 2 x 5 x 2, which only decodes correctly if the odometer order (name-sorted, LAST varying fastest) and every individual domain order are right.
        BlockDefinition stairs = Block(protocol, "oak_stairs");
        Assert.Equal(80, stairs.StateCount);
        Assert.Equal(
            ["facing", "half", "shape", "waterlogged"],
            stairs.Properties.Select(p => p.Name));

        int def = stairs.DefaultStateId;
        Assert.Equal("north", Property(protocol, "oak_stairs", def, "facing"));
        Assert.Equal("bottom", Property(protocol, "oak_stairs", def, "half"));
        Assert.Equal("straight", Property(protocol, "oak_stairs", def, "shape"));
        Assert.Equal("false", Property(protocol, "oak_stairs", def, "waterlogged"));

        // The last property varies fastest, so the neighbouring state is the waterlogged twin. This is the assertion that fails if the boolean domain is read as (false, true) instead of vanilla's (true, false), or if the odometer runs the other way.
        Assert.Equal("true", Property(protocol, "oak_stairs", def - 1, "waterlogged"));
        Assert.Equal("north", Property(protocol, "oak_stairs", def - 1, "facing"));

        // Stepping a whole shape cycle moves the next property up.
        Assert.Equal("inner_left", Property(protocol, "oak_stairs", def + 2, "shape"));
    }

    [Theory]
    [MemberData(nameof(BandsWithProperties))]
    public void Facing_Resolves_To_The_Right_One_Of_Three_Vanilla_Domains(int protocol)
    {
        // "facing" is three different properties in vanilla (6-way, 5-way hopper, 4-way horizontal). Picking the wrong one silently rotates every value, so each is pinned on a real block.
        Assert.Equal("north", Property(protocol, "dispenser", Block(protocol, "dispenser").DefaultStateId, "facing"));
        Assert.Equal("down", Property(protocol, "hopper", Block(protocol, "hopper").DefaultStateId, "facing"));
        Assert.Equal("north", Property(protocol, "ladder", Block(protocol, "ladder").DefaultStateId, "facing"));

        BlockDefinition dispenser = Block(protocol, "dispenser");
        BlockPropertyDefinition facing = dispenser.Properties.Single(p => p.Name == "facing");
        Assert.Equal(["north", "east", "south", "west", "up", "down"], facing.Values);

        BlockPropertyDefinition hopperFacing = Block(protocol, "hopper").Properties.Single(p => p.Name == "facing");
        Assert.Equal(["down", "north", "south", "west", "east"], hopperFacing.Values);

        // The four-way horizontal domain follows declaration order: north, south, west, east. Checking all four values detects permutations that a north-only assertion cannot.
        BlockPropertyDefinition horizontal = Block(protocol, "ladder").Properties.Single(p => p.Name == "facing");
        Assert.Equal(["north", "south", "west", "east"], horizontal.Values);

        // And through real states rather than a domain listing: ladder is [facing(4), waterlogged(2)], so the facing steps every two states from the north pair.
        BlockDefinition ladder = Block(protocol, "ladder");
        Assert.Equal("south", Property(protocol, "ladder", ladder.MinStateId + 2, "facing"));
        Assert.Equal("west", Property(protocol, "ladder", ladder.MinStateId + 4, "facing"));
        Assert.Equal("east", Property(protocol, "ladder", ladder.MinStateId + 6, "facing"));
    }

    [Theory]
    [MemberData(nameof(BandsWithProperties))]
    public void Physics_Scalars_Are_The_Vanilla_Values(int protocol)
    {
        // Pin non-default values as well as the ordinary stone controls.
        Assert.Equal(0.98f, Block(protocol, "ice").Friction, 3);
        Assert.Equal(0.98f, Block(protocol, "packed_ice").Friction, 3);
        Assert.Equal(0.8f, Block(protocol, "slime_block").Friction, 3);
        Assert.Equal(0.6f, Block(protocol, "stone").Friction, 3);

        Assert.Equal(0.4f, Block(protocol, "soul_sand").SpeedFactor, 3);
        Assert.Equal(1.0f, Block(protocol, "stone").SpeedFactor, 3);

        if (protocol >= 735) // honey_block is 1.15+
        {
            Assert.Equal(0.4f, Block(protocol, "honey_block").SpeedFactor, 3);
            Assert.Equal(0.5f, Block(protocol, "honey_block").JumpFactor, 3);
        }

        Assert.Equal(1.0f, Block(protocol, "stone").JumpFactor, 3);
    }

    /// <summary>EXACTLY two blocks in the whole game slow a walk, on every one of the 50 protocols: soul sand always, and honey from 1.15 (protocol 735), both at 0.4.</summary>
    /// <remarks>
    /// <para>An exhaustive sweep is required because a downstream consumer relies on the closure and not on the two examples. <c>ActionCosts.SpeedFactorCostMultiplier</c> charges a calibrated 1.691 at 0.4 and falls back to <c>1 / factor</c> for anything else; that distinction is valid only while 0.4 is the only sub-unit factor the dataset carries, and "is 0.4 the only one" is a claim about all 1000-odd blocks, not about two of them.</para>
    /// <para>The expectation is a literal pair of names and a literal protocol boundary, stated here rather than read out of the table under test, so a dataset that grew a third slow block fails this row instead of quietly widening it.</para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllBands))]
    public void Exactly_Two_Blocks_Slow_A_Walk(int protocol)
    {
        List<string> slow = [];
        foreach (RegistryEntry<BlockDefinition> entry in JavaGameData.Registries(protocol).Blocks)
            if (entry.Value.SpeedFactor != 1.0f)
            {
                slow.Add(entry.Id.ToString());
                Assert.Equal(0.4f, entry.Value.SpeedFactor, 3);
            }

        slow.Sort(StringComparer.Ordinal);

        // 573 is 1.15, the version honey blocks arrive in; the eighteen bands below it have soul sand and nothing else.
        string[] expected = protocol >= 573
            ? ["minecraft:honey_block", "minecraft:soul_sand"]
            : ["minecraft:soul_sand"];

        Assert.Equal(expected, slow);
    }

    /// <summary>EXACTLY one block in the whole game shortens a jump: honey, at 0.5, from 1.15 onward. Soul sand is NOT one of them, which is the entire difference between course rows J8 and J10.</summary>
    /// <remarks>The same exhaustive shape as <see cref="Exactly_Two_Blocks_Slow_A_Walk"/> and for the same reason: <c>MoveHelper.CanTakeOffForAJump</c> refuses every jump-requiring move from a floor whose factor is under 1.0, which is exact only while 0.5 is the sole sub-unit factor. Honey's apex, 0.383852, is below the auto-step, so refusing costs nothing there. A factor nearer 1.0 would be refused too, conservatively; this row is what says the game has none.</remarks>
    [Theory]
    [MemberData(nameof(AllBands))]
    public void Exactly_One_Block_Shortens_A_Jump(int protocol)
    {
        List<string> shortened = [];
        foreach (RegistryEntry<BlockDefinition> entry in JavaGameData.Registries(protocol).Blocks)
            if (entry.Value.JumpFactor != 1.0f)
            {
                shortened.Add(entry.Id.ToString());
                Assert.Equal(0.5f, entry.Value.JumpFactor, 3);
            }

        string[] expected = protocol >= 573 ? ["minecraft:honey_block"] : [];
        Assert.Equal(expected, shortened);

        // And the negative that the pathfinding gate leans on directly.
        Assert.Equal(1.0f, Block(protocol, "soul_sand").JumpFactor, 3);
    }

    /// <summary>The whole climbable set, on every one of the 50 protocols, is vanilla's own <c>#minecraft:climbable</c> tag intersected with the blocks that era actually has.</summary>
    /// <remarks>
    /// <para><b>The tag is data, not an inferred behavior.</b> The 1.16 set contains ladder, vine, scaffolding, and both forms of weeping and twisting vines. The 1.17 set adds both cave-vine entries. Nether and cave vines were therefore climbable from the tick they existed, which is why the curated list needs no era switch of its own: the curated flag is intersected with the version's own block table (<c>BlockAttributeResolver.ResolveBlockAttributes</c>), so a name that era does not carry contributes nothing.</para>
    /// <para>The expectation uses literal sets and protocol boundaries, independent of the table under test. The sweep covers every block in each registry.</para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllBands))]
    public void Climbable_Is_Vanillas_Climbable_Tag_For_Every_WireLayout(int protocol)
    {
        List<string> climbable = [];
        foreach (RegistryEntry<BlockDefinition> entry in JavaGameData.Registries(protocol).Blocks)
            if ((entry.Value.Flags & BlockFlags.Climbable) != 0)
                climbable.Add(entry.Id.ToString());

        climbable.Sort(StringComparer.Ordinal);

        // 393 is 1.13 (the flattening), 477 is 1.14 (scaffolding), 735 is 1.16 (nether vines), 755 is 1.17 (cave vines). Below 393 the registry is the pre-flattening mirror, which names the same two blocks the flat list's first two name.
        string[] expected =
            protocol >= 755
                ? [
                    "minecraft:cave_vines",
                    "minecraft:cave_vines_plant",
                    "minecraft:ladder",
                    "minecraft:scaffolding",
                    "minecraft:twisting_vines",
                    "minecraft:twisting_vines_plant",
                    "minecraft:vine",
                    "minecraft:weeping_vines",
                    "minecraft:weeping_vines_plant",
                ]
            : protocol >= 735
                ? [
                    "minecraft:ladder",
                    "minecraft:scaffolding",
                    "minecraft:twisting_vines",
                    "minecraft:twisting_vines_plant",
                    "minecraft:vine",
                    "minecraft:weeping_vines",
                    "minecraft:weeping_vines_plant",
                ]
            : protocol >= 477
                ? ["minecraft:ladder", "minecraft:scaffolding", "minecraft:vine"]
                : ["minecraft:ladder", "minecraft:vine"];

        Assert.Equal(expected, climbable);
    }

    /// <summary>Every block in the game whose <c>getFluidState</c> is an unconditional water source, on every one of the 50 protocols. Five blocks, and <c>minecraft:bubble_column</c> is the fifth.</summary>
    /// <remarks>
    /// <para>The <c>Waterlogged</c> flag is what <c>MoveHelper.IsWater</c> and <c>PlayerPhysics.IsWater</c> both read, so this list decides buoyancy, swim passability, fall-damage absorption and the breath model for every block on it. The exhaustive set prevents a block from silently joining or leaving this behavior.</para>
    /// <para><b>Bubble columns require both water and lift behavior.</b> Classifying a column as water without its lift also drains the bot's air. Both halves are modeled: <c>PlayerPhysics</c> applies its lift and <c>BreathModel.IsSubmerged</c> applies the air-drain exemption, so the block belongs in the set.</para>
    /// <para>Protocol 393 introduces kelp, seagrass, and bubble columns; earlier bands contain none of these blocks.</para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllBands))]
    public void IntrinsicallyWaterloggedBlocks_AreTheFiveWithAnUnconditionalWaterFluidState(int protocol)
    {
        List<string> containsWater = [];
        foreach (RegistryEntry<BlockDefinition> entry in JavaGameData.Registries(protocol).Blocks)
        {
            // EVERY state, which is what separates the curated list from the property. A block with a `waterlogged` property carries the flag on half its states and not the other half - and a coral's DEFAULT state is the wet one, so reading the default alone would sweep in the whole coral family. An unconditional getFluidState override cannot be switched off, so its block is waterlogged on all states.
            bool all = true;
            for (int state = entry.Value.MinStateId; state <= entry.Value.MaxStateId && all; state++)
                all = (entry.Value.FlagsForState(state) & BlockFlags.Waterlogged) != 0;

            if (all)
                containsWater.Add(entry.Id.ToString());

        }

        containsWater.Sort(StringComparer.Ordinal);

        string[] expected = protocol >= 393
            ? [
                "minecraft:bubble_column",
                "minecraft:kelp",
                "minecraft:kelp_plant",
                "minecraft:seagrass",
                "minecraft:tall_seagrass",
            ]
            : [];

        Assert.Equal(expected, containsWater);
    }

    [Theory]
    [MemberData(nameof(BandsWithProperties))]
    public void Flags_Distinguish_Air_Fluid_Solid_And_Passable(int protocol)
    {
        // These blocks exercise distinct collision and semantic classifications.
        BlockDefinition air = Block(protocol, "air");
        Assert.True((air.Flags & BlockFlags.Air) != 0);
        Assert.Equal(BlockFlags.None, air.Flags & (BlockFlags.BlocksMotion | BlockFlags.Solid));

        BlockDefinition water = Block(protocol, "water");
        Assert.True((water.Flags & BlockFlags.Fluid) != 0);
        Assert.Equal(BlockFlags.None, water.Flags & BlockFlags.BlocksMotion);

        BlockDefinition stone = Block(protocol, "stone");
        Assert.True((stone.Flags & BlockFlags.Solid) != 0);
        Assert.True((stone.Flags & BlockFlags.BlocksMotion) != 0);

        // A slab blocks motion but is not a full solid cube.
        BlockDefinition slab = Block(protocol, "oak_slab");
        Assert.True((slab.FlagsForState(slab.DefaultStateId) & BlockFlags.BlocksMotion) != 0);
        Assert.Equal(BlockFlags.None, slab.FlagsForState(slab.DefaultStateId) & BlockFlags.Solid);

        // A flower has no collision box at all.
        BlockDefinition flower = Block(protocol, "dandelion");
        Assert.Equal(
            BlockFlags.None,
            flower.FlagsForState(flower.DefaultStateId) & (BlockFlags.BlocksMotion | BlockFlags.Solid));

        // Ladders are climbable.
        Assert.True((Block(protocol, "ladder").Flags & BlockFlags.Climbable) != 0);
    }

    /// <summary>Every band that has <c>minecraft:powder_snow</c>: 1.17 (755) onward.</summary>
    public static TheoryData<int> BandsWithPowderSnow => new() { 755, 758, 763, 766, 770, 776 };

    /// <summary>Bands from before 1.17, which have no powder snow at all.</summary>
    public static TheoryData<int> BandsWithoutPowderSnow => new() { 47, 340, 393, 477, 578, 754 };

    /// <summary>The shape table reports powder snow as empty, and the flags DataGen derives from it carry neither <c>BlocksMotion</c> nor <c>Solid</c>.</summary>
    /// <remarks>
    /// <para>A shape table has no entity context, so it records the empty shape. Leather-boot behavior is applied at the collision call site by <c>Umpk.Physics.PowderSnowCollision</c>.</para>
    /// <para>If the table returned a full cube, powder snow would incorrectly support entities without leather boots.</para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(BandsWithPowderSnow))]
    public void PowderSnow_HasAnEmptyShapeAndNoCollisionFlags(int protocol)
    {
        BlockDefinition powderSnow = Block(protocol, "powder_snow");
        Assert.Equal(
            BlockFlags.None,
            powderSnow.FlagsForState(powderSnow.DefaultStateId)
                & (BlockFlags.BlocksMotion | BlockFlags.Solid | BlockFlags.Air | BlockFlags.Fluid));

        Assert.True(
            JavaGameData.BlockShapes(protocol).GetCollisionShapes(powderSnow.DefaultStateId).IsEmpty,
            $"protocol {protocol}: powder snow's tabled collision shape is not empty.");
    }

    /// <summary>The era gate, and it is the DATASET rather than a protocol literal in engine code. Powder snow arrived in 1.17, so on every earlier band the identifier resolves to nothing and every boots-conditional arm in the engine and the planner is unreachable by construction.</summary>
    [Theory]
    [MemberData(nameof(BandsWithoutPowderSnow))]
    public void PowderSnow_IsAbsentBeforeSeventeen(int protocol)
    {
        Assert.False(
            JavaGameData.Registries(protocol).Blocks.TryGet(
                Identifier.Minecraft("powder_snow"), out RegistryEntry<BlockDefinition> _),
            $"protocol {protocol} was not expected to have minecraft:powder_snow.");
    }

    [Theory]
    [MemberData(nameof(BandsWithProperties))]
    public void Waterlogged_Is_A_Per_State_Flag_Read_From_The_Property(int protocol)
    {
        BlockDefinition stairs = Block(protocol, "oak_stairs");
        Assert.Equal(BlockFlags.None, stairs.FlagsForState(stairs.DefaultStateId) & BlockFlags.Waterlogged);
        Assert.True((stairs.FlagsForState(stairs.DefaultStateId - 1) & BlockFlags.Waterlogged) != 0);
    }

    /// <summary>Kelp, kelp plant, seagrass, tall seagrass, and bubble columns contain water.</summary>
    /// <remarks>
    /// <para>These blocks contain a full water source in every state, with no property to condition it on. The same five blocks and only those five carry an unconditional water fluid state across the supported eras. Every other water-source result in the supported set is conditional on a <c>waterlogged</c> property.</para>
    /// <para><c>minecraft:bubble_column</c> belongs in the set because the engine models both its lift and its air-drain exemption. The closure over all 50 protocols is <see cref="IntrinsicallyWaterloggedBlocks_AreTheFiveWithAnUnconditionalWaterFluidState"/>.</para>
    /// <para>The axis is <c>Waterlogged</c> and not the <c>fluid</c> list, because these are blocks that CONTAIN water rather than blocks that ARE water. The fluid list also sets <c>Replaceable</c> and suppresses the shape-derived <c>BlocksMotion</c>/<c>Solid</c> classification entirely, which would be a lie about four blocks whose collision shape is genuinely empty for a different reason.</para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(PostFlatteningBands))]
    public void Kelp_And_Seagrass_Carry_Water(int protocol)
    {
        foreach (string name in new[] { "kelp", "kelp_plant", "seagrass", "tall_seagrass" })
        {
            BlockDefinition block = Block(protocol, name);
            for (int state = block.MinStateId; state <= block.MaxStateId; state++)
                Assert.True(
                    (block.FlagsForState(state) & BlockFlags.Waterlogged) != 0,
                    $"protocol {protocol}: minecraft:{name} state {state} should carry water");

            // Contained, not composed: they are not fluids and they are not replaceable-by-fluid.
            Assert.Equal(BlockFlags.None, block.Flags & (BlockFlags.Fluid | BlockFlags.Air));

            // And their empty collision shape still reads as empty: no BlocksMotion, no Solid.
            Assert.Equal(BlockFlags.None, block.Flags & (BlockFlags.BlocksMotion | BlockFlags.Solid));
        }

        // The fifth. Every one of its two states carries water, and none of them is a fluid, air, or a collision box - the same three claims the four above make.
        BlockDefinition bubbles = Block(protocol, "bubble_column");
        for (int state = bubbles.MinStateId; state <= bubbles.MaxStateId; state++)
            Assert.True(
                (bubbles.FlagsForState(state) & BlockFlags.Waterlogged) != 0,
                $"protocol {protocol}: minecraft:bubble_column state {state} should carry water");

        Assert.Equal(
            BlockFlags.None,
            bubbles.Flags & (BlockFlags.Fluid | BlockFlags.Air | BlockFlags.BlocksMotion | BlockFlags.Solid));
    }

    /// <summary>The eleven pre-flattening bands, pinned by literal protocol number rather than derived from the dataset, because "the dataset has no kelp there" is exactly the claim under test. None of the five unconditionally-wet blocks exists before 1.13, so the change is a no-op on 47 through 340 and every one of those bands' block tables must still say so.</summary>
    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(108)]
    [InlineData(109)]
    [InlineData(110)]
    [InlineData(210)]
    [InlineData(315)]
    [InlineData(316)]
    [InlineData(335)]
    [InlineData(338)]
    [InlineData(340)]
    public void Pre_Flattening_Bands_Have_No_Kelp_Or_Seagrass(int protocol)
    {
        Registry<BlockDefinition> blocks = JavaGameData.Registries(protocol).Blocks;
        foreach (string name in new[] { "kelp", "kelp_plant", "seagrass", "tall_seagrass", "bubble_column" })
            Assert.False(
                blocks.TryGet(Identifier.Minecraft(name), out _),
                $"protocol {protocol}: minecraft:{name} should not exist before the flattening");

        // The band is not empty of water, though, which is what makes this a real control.
        Assert.True((Block(protocol, "water").Flags & BlockFlags.Fluid) != 0);
    }

    [Theory]
    [MemberData(nameof(AllBands))]
    public void Every_Emitted_Property_Value_Count_Matches_The_Block_State_Count(int protocol)
    {
        // The structural invariant the whole scheme rests on: where a block's domains were emitted at all, their product must be exactly the block's own state count. A block that could not be proven carries its names with empty domains, which is the honest degradation, and is skipped.
        Registry<BlockDefinition> blocks = JavaGameData.Registries(protocol).Blocks;
        int resolved = 0;
        foreach (RegistryEntry<BlockDefinition> entry in blocks)
        {
            BlockDefinition definition = entry.Value;
            if (definition.Properties.Count == 0 || definition.Properties.Any(p => p.Values.Count == 0))
                continue;

            long product = 1;
            foreach (BlockPropertyDefinition property in definition.Properties)
                product *= property.Values.Count;

            Assert.True(
                product == definition.StateCount,
                $"protocol {protocol}: {entry.Id} domains multiply to {product} but it owns {definition.StateCount} states.");
            resolved++;
        }

        // Every flattened band carries property names. Pre-flattening bands have no named properties on the wire and must resolve none.
        int minimumResolved = protocol switch
        {
            >= 477 => 380,
            >= 393 => 330,
            _ => 0,
        };

        if (minimumResolved == 0)
            Assert.Equal(0, resolved);

        else
            Assert.True(
                resolved > minimumResolved,
                $"protocol {protocol}: only {resolved} blocks resolved their property domains.");

    }

    [Theory]
    [InlineData(47)]   // 1.8
    [InlineData(340)]  // 1.12.2, the last pre-flattening band
    public void Bands_Without_Property_Data_Report_No_Properties(int protocol)
    {
        // The honest degradation, asserted rather than assumed. Pre-flattening states are (id << 4) | meta with no named properties anywhere in the protocol, and the data must not invent any. The 1.13 bands do carry properties; see The_1_13_Bands_Carry_Property_Names.
        Registry<BlockDefinition> blocks = JavaGameData.Registries(protocol).Blocks;
        foreach (RegistryEntry<BlockDefinition> entry in blocks)
            Assert.Empty(entry.Value.Properties);

    }

    /// <summary>The 1.13 bands' property names and representative boundary values.</summary>
    [Theory]
    [InlineData(393)]  // 1.13
    [InlineData(401)]  // 1.13.1
    [InlineData(404)]  // 1.13.2
    public void The_1_13_Bands_Carry_Property_Names(int protocol)
    {
        BlockDefinition stairs = Block(protocol, "oak_stairs");
        Assert.Equal(["facing", "half", "shape", "waterlogged"], stairs.Properties.Select(p => p.Name));
        Assert.Equal("north", Property(protocol, "oak_stairs", stairs.DefaultStateId, "facing"));
        Assert.Equal("bottom", Property(protocol, "oak_stairs", stairs.DefaultStateId, "half"));

        BlockDefinition wheat = Block(protocol, "wheat");
        BlockPropertyDefinition age = Assert.Single(wheat.Properties);
        Assert.Equal("age", age.Name);
        Assert.Equal("0", Property(protocol, "wheat", wheat.MinStateId, "age"));
        Assert.Equal("7", Property(protocol, "wheat", wheat.MaxStateId, "age"));
    }

    /// <summary>The seven names introduced by the 1.13.1 palette resolve with their property domains.</summary>
    /// <remarks>Protocol 401 gives <c>waterlogged</c> to the five live corals and the conduit, and <c>unstable</c> to TNT.</remarks>
    [Fact]
    public void The_1_13_1_Band_Resolves_The_Names_It_Used_To_Withhold()
    {
        foreach (string name in new[]
        {
            "conduit", "tube_coral", "brain_coral", "bubble_coral", "fire_coral", "horn_coral",
        })
        {
            BlockDefinition definition = Block(401, name);
            Assert.Equal(2, definition.StateCount);
            BlockPropertyDefinition waterlogged = Assert.Single(definition.Properties);
            Assert.Equal("waterlogged", waterlogged.Name);

            // Boolean domains are ordered true then false. These underwater blocks default to the first, waterlogged state. The flag follows the resolved property value.
            Assert.Equal(definition.MinStateId, definition.DefaultStateId);
            Assert.Equal("true", Property(401, name, definition.DefaultStateId, "waterlogged"));
            Assert.Equal("false", Property(401, name, definition.MaxStateId, "waterlogged"));
            Assert.True((definition.FlagsForState(definition.MinStateId) & BlockFlags.Waterlogged) != 0);
            Assert.Equal(
                BlockFlags.None,
                definition.FlagsForState(definition.MaxStateId) & BlockFlags.Waterlogged);
        }

        // TNT defaults to the second, false state, providing the opposite domain-order case.
        BlockDefinition tnt = Block(401, "tnt");
        Assert.Equal(2, tnt.StateCount);
        Assert.Equal("unstable", Assert.Single(tnt.Properties).Name);
        Assert.Equal(tnt.MinStateId + 1, tnt.DefaultStateId);
        Assert.Equal("true", Property(401, "tnt", tnt.MinStateId, "unstable"));
        Assert.Equal("false", Property(401, "tnt", tnt.DefaultStateId, "unstable"));
    }

    [Theory]
    [InlineData(47)]
    [InlineData(340)]
    public void Legacy_Bands_Still_Carry_Real_Flags_And_Friction(int protocol)
    {
        // Property values are impossible before the flattening, but the semantic flags and the physics scalars are not: those come from the block identifier and the curated tables.
        Assert.True((Block(protocol, "air").Flags & BlockFlags.Air) != 0);
        Assert.True((Block(protocol, "water").Flags & BlockFlags.Fluid) != 0);
        Assert.Equal(0.98f, Block(protocol, "ice").Friction, 3);
        Assert.Equal(0.6f, Block(protocol, "stone").Friction, 3);
        Assert.True((Block(protocol, "ladder").Flags & BlockFlags.Climbable) != 0);
    }
}
