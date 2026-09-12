using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Data.Java.Tests;

/// <summary>
/// The generated block-shape tables, read through the source the client and pathfinder consume.
/// <para>These tests pin representative geometry from the AABB pool and state-to-shape reference table, including the boundaries where the data deliberately falls back.</para>
/// </summary>
public sealed class BlockShapeTableTests
{
    /// <summary>Representative flattened bands across the supported eras.</summary>
    public static TheoryData<int> FlattenedProtocols => [393, 404, 477, 578, 736, 763, 770, 776];

    /// <summary>Pre-flattening bands with per-block-id shape coverage.</summary>
    public static TheoryData<int> ExtractedLegacyProtocols => [107, 110, 210, 315, 335, 340];

    [Theory]
    [MemberData(nameof(FlattenedProtocols))]
    public void Slab_is_a_bottom_half_box(int protocol)
    {
        // A bottom slab spans y 0 through 8/16. 1.13 named the smooth-stone slab minecraft:stone_slab; 1.14 renamed it.
        string slab = protocol < 477 ? "minecraft:stone_slab" : "minecraft:smooth_stone_slab";
        AssertSingleBox(DefaultShape(protocol, slab), 0, 0, 0, 1, 0.5, 1);
    }

    [Theory]
    [MemberData(nameof(FlattenedProtocols))]
    public void Carpet_is_one_sixteenth_tall(int protocol)
    {
        // Carpet spans y 0 through 1/16.
        AssertSingleBox(DefaultShape(protocol, "minecraft:white_carpet"), 0, 0, 0, 1, 0.0625, 1);
    }

    [Theory]
    [MemberData(nameof(FlattenedProtocols))]
    public void Unconnected_fence_is_a_post_with_a_gap_around_it(int protocol)
    {
        // The unconnected fence is a centered 4/16-wide post with collision height 24/16.
        AssertSingleBox(DefaultShape(protocol, "minecraft:oak_fence"), 0.375, 0, 0.375, 0.625, 1.5, 0.625);
    }

    [Theory]
    [MemberData(nameof(FlattenedProtocols))]
    public void Closed_fence_gate_is_a_one_and_a_half_block_wall(int protocol)
    {
        // The north-facing gate is 4/16 deep on Z and 24/16 tall.
        AssertSingleBox(DefaultShape(protocol, "minecraft:oak_fence_gate"), 0, 0, 0.375, 1, 1.5, 0.625);
    }

    [Theory]
    [MemberData(nameof(FlattenedProtocols))]
    public void Straight_bottom_stair_is_a_slab_plus_a_raised_half(int protocol)
    {
        // A straight stair is a half-height slab joined with the upper half on the facing side. The default state is facing=north, half=bottom, shape=straight.
        ReadOnlySpan<Aabb> shape = DefaultShape(protocol, "minecraft:oak_stairs");
        Assert.Equal(2, shape.Length);
        AssertBox(shape[0], 0, 0, 0, 1, 0.5, 1);
        AssertBox(shape[1], 0, 0.5, 0, 1, 1, 0.5);
    }

    /// <summary>For each ladder facing, the named state must carry the corresponding wall-aligned box.</summary>
    /// <remarks>
    /// <para>Shape boxes and property domains are independent tables, so agreeing on all four facings is a real constraint rather than a round trip. A ladder is the right probe because its four boxes are thin slabs on four different faces: no permutation can hide behind symmetry.</para>
    /// <para>The facing domain order is north, south, west, east. The four expected boxes are thin slabs aligned with distinct cell faces, so a permutation cannot hide behind symmetry.</para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(FlattenedProtocols))]
    public void A_ladder_carries_vanillas_box_for_each_named_facing(int protocol)
    {
        AssertSingleBox(FacingShape(protocol, "minecraft:ladder", "north"), 0, 0, 0.8125, 1, 1, 1);
        AssertSingleBox(FacingShape(protocol, "minecraft:ladder", "south"), 0, 0, 0, 1, 1, 0.1875);
        AssertSingleBox(FacingShape(protocol, "minecraft:ladder", "west"), 0.8125, 0, 0, 1, 1, 1);
        AssertSingleBox(FacingShape(protocol, "minecraft:ladder", "east"), 0, 0, 0, 0.1875, 1, 1);
    }

    /// <summary>Cross-checks a block whose four boxes sit on the opposite faces from a ladder's.</summary>
    /// <remarks>A wall skull mounts behind its facing. Only bands whose skull shape table is per-state are checked: the wall skulls gained a <c>powered</c> property at 1.20.3. Only bands with compatible per-state tables are checked.</remarks>
    [Theory]
    [InlineData(765)]
    [InlineData(770)]
    [InlineData(776)]
    public void A_wall_skull_carries_vanillas_box_for_each_named_facing(int protocol)
    {
        AssertSingleBox(FacingShape(protocol, "minecraft:skeleton_wall_skull", "north"), 0.25, 0.25, 0.5, 0.75, 0.75, 1);
        AssertSingleBox(FacingShape(protocol, "minecraft:skeleton_wall_skull", "south"), 0.25, 0.25, 0, 0.75, 0.75, 0.5);
        AssertSingleBox(FacingShape(protocol, "minecraft:skeleton_wall_skull", "west"), 0.5, 0.25, 0.25, 1, 0.75, 0.75);
        AssertSingleBox(FacingShape(protocol, "minecraft:skeleton_wall_skull", "east"), 0, 0.25, 0.25, 0.5, 0.75, 0.75);
    }

    [Theory]
    [MemberData(nameof(FlattenedProtocols))]
    public void A_full_block_is_still_a_unit_cube_and_air_still_collides_with_nothing(int protocol)
    {
        AssertSingleBox(DefaultShape(protocol, "minecraft:stone"), 0, 0, 0, 1, 1, 1);
        Assert.True(DefaultShape(protocol, "minecraft:air").IsEmpty);
    }

    /// <summary>Blocks renamed across protocol bands must retain their geometry after name-based reindexing.</summary>
    /// <remarks>The rename is "grass" in 1.20.1 and "short_grass" in 1.20.4; "grass_path" in 1.16.5 and "dirt_path" in 1.17.1; "chain" in 1.21.4 and "iron_chain" in 1.21.9. Short grass has no collision, a dirt path is 15/16 high, and a chain is a centered 3/16 column on its default Y axis.</remarks>
    [Theory]
    [MemberData(nameof(FlattenedProtocols))]
    public void A_renamed_block_keeps_its_geometry_instead_of_becoming_a_solid_cube(int protocol)
    {
        // The plant. 1.20.3 renamed it, so which name the band uses depends on the band.
        string shortGrass = protocol >= 765 ? "minecraft:short_grass" : "minecraft:grass";
        Assert.True(DefaultShape(protocol, shortGrass).IsEmpty);

        // The path block. 1.17 renamed it; 1.13 (protocol 393) has neither spelling of it yet.
        if (protocol >= 477)
        {
            string path = protocol >= 755 ? "minecraft:dirt_path" : "minecraft:grass_path";
            AssertSingleBox(DefaultShape(protocol, path), 0, 0, 0, 1, 0.9375, 1);
        }

        // The chain, added in 1.16 and renamed at 1.21.9.
        if (protocol >= 735)
        {
            string chain = protocol >= 773 ? "minecraft:iron_chain" : "minecraft:chain";
            AssertSingleBox(DefaultShape(protocol, chain), 0.40625, 0, 0.40625, 0.59375, 1, 0.59375);
        }
    }

    /// <summary>Shape-derived flags must not classify renamed short grass as a wall.</summary>
    [Theory]
    [MemberData(nameof(FlattenedProtocols))]
    public void A_renamed_plant_no_longer_reports_itself_as_a_solid_wall(int protocol)
    {
        string shortGrass = protocol >= 765 ? "minecraft:short_grass" : "minecraft:grass";
        Registry<BlockDefinition> blocks = JavaGameData.Registries(protocol).Blocks;
        Assert.True(blocks.TryGetValue(Identifier.Parse(shortGrass), out BlockDefinition? definition));

        BlockFlags flags = definition.FlagsForState(definition.DefaultStateId);
        Assert.False(flags.HasFlag(BlockFlags.BlocksMotion), $"protocol {protocol}: {shortGrass} blocks motion");
        Assert.False(flags.HasFlag(BlockFlags.Solid), $"protocol {protocol}: {shortGrass} is solid");
    }

    /// <summary>The chain's three axis variants, on the bands that have the <c>axis</c> property (1.16.2 on).</summary>
    /// <remarks>The X, Y, and Z variants are centered 3/16 columns along their corresponding axis. Protocols before the axis property answer every state with the Y-axis box.</remarks>
    [Theory]
    [InlineData(751)]
    [InlineData(763)]
    [InlineData(770)]
    [InlineData(776)]
    public void A_chain_carries_vanillas_box_for_each_axis(int protocol)
    {
        string chain = protocol >= 773 ? "minecraft:iron_chain" : "minecraft:chain";
        Registry<BlockDefinition> blocks = JavaGameData.Registries(protocol).Blocks;
        Assert.True(blocks.TryGetValue(Identifier.Parse(chain), out BlockDefinition? definition));

        // [axis(3), waterlogged(2)] name-sorted with waterlogged varying fastest.
        IBlockShapeSource shapes = JavaGameData.BlockShapes(protocol);
        AssertSingleBox(shapes.GetCollisionShapes(definition.MinStateId), 0, 0.40625, 0.40625, 1, 0.59375, 0.59375);
        AssertSingleBox(shapes.GetCollisionShapes(definition.MinStateId + 2), 0.40625, 0, 0.40625, 0.59375, 1, 0.59375);
        AssertSingleBox(shapes.GetCollisionShapes(definition.MinStateId + 4), 0.40625, 0.40625, 0, 0.59375, 0.59375, 1);
    }

    /// <summary>Literal protocol-763 state ids retain their expected geometry.</summary>
    /// <remarks>State 2005 is grass with no collision. State 6777 is a non-waterlogged Y-axis chain with a centered 3/16 column.</remarks>
    [Fact]
    public void The_state_ids_a_live_server_sent_carry_vanillas_geometry()
    {
        IBlockShapeSource shapes = JavaGameData.BlockShapes(763);
        Assert.True(shapes.GetCollisionShapes(2005).IsEmpty);
        AssertSingleBox(shapes.GetCollisionShapes(6777), 0.40625, 0, 0.40625, 0.59375, 1, 0.59375);
    }

    /// <summary>Per-band completeness for the flattened bands: every state of every block resolves from data, not from the fallback. A gap here would be a silent unit cube somewhere in the world.</summary>
    [Theory]
    [MemberData(nameof(FlattenedProtocols))]
    public void Flattened_bands_cover_every_block_state(int protocol)
    {
        var shapes = (JavaBlockShapes)JavaGameData.BlockShapes(protocol);
        Registry<BlockDefinition> blocks = JavaGameData.Registries(protocol).Blocks;

        List<int> uncovered = [];
        foreach (RegistryEntry<BlockDefinition> entry in blocks)
            for (int state = entry.Value.MinStateId; state <= entry.Value.MaxStateId; state++)
                if (!shapes.Covers(state))
                    uncovered.Add(state);

        Assert.Empty(uncovered);
        Assert.Equal(shapes.RefCount, shapes.CoveredCount);
    }

    /// <summary>The pre-flattening bands from 1.9 on cover every block id in their own block table. Coverage is still seeded per block id from its meta-0 shape; the per-metadata refinements that ride on top are pinned by <c>MetadataCollisionShapeTests</c>.</summary>
    [Theory]
    [MemberData(nameof(ExtractedLegacyProtocols))]
    public void Extracted_legacy_bands_cover_every_block_id(int protocol)
    {
        var shapes = (JavaBlockShapes)JavaGameData.BlockShapes(protocol);
        Registry<BlockDefinition> blocks = JavaGameData.Registries(protocol).Blocks;

        List<Identifier> uncovered = [];
        foreach (RegistryEntry<BlockDefinition> entry in blocks)
            if (!shapes.Covers(entry.Value.DefaultStateId))
                uncovered.Add(entry.Id);

        Assert.Empty(uncovered);
    }

    [Theory]
    [MemberData(nameof(ExtractedLegacyProtocols))]
    public void Extracted_legacy_bands_carry_real_slab_carpet_and_fence_geometry(int protocol)
    {
        AssertSingleBox(LegacyShape(protocol, blockId: 44), 0, 0, 0, 1, 0.5, 1);      // stone_slab
        AssertSingleBox(LegacyShape(protocol, blockId: 171), 0, 0, 0, 1, 0.0625, 1);  // carpet
        AssertSingleBox(LegacyShape(protocol, blockId: 85), 0.375, 0, 0.375, 0.625, 1.5, 0.625); // fence
    }

    /// <summary>Protocol 47 covers 109 block ids. Uncovered blocks intentionally use fallback geometry.</summary>
    /// <remarks>
    /// <para>Block id 182 is the red sandstone half slab and shares the half-height behavior of ids 44 and 126.</para>
    /// <para>A torch, flower, rail, pressure plate, and sign have no collision boxes and must not fall back to a solid unit cube.</para>
    /// </remarks>
    [Fact]
    public void The_1_8_band_covers_its_curated_block_ids_and_falls_back_elsewhere()
    {
        var shapes = (JavaBlockShapes)JavaGameData.BlockShapes(47);

        // Count block ids, not state entries: the table has one entry for each (id, metadata) state.
        int coveredBlockIds = 0;
        for (int blockId = 0; blockId < 256; blockId++)
            if (shapes.Covers(blockId << 4))
                coveredBlockIds++;

        Assert.Equal(109, coveredBlockIds);
        Assert.Equal(109 * 16, shapes.CoveredCount);

        // All three 1.8 half slabs use a half-height box.
        AssertSingleBox(LegacyShape(47, blockId: 44), 0, 0, 0, 1, 0.5, 1);   // stone_slab
        AssertSingleBox(LegacyShape(47, blockId: 126), 0, 0, 0, 1, 0.5, 1);  // wooden_slab
        AssertSingleBox(LegacyShape(47, blockId: 182), 0, 0, 0, 1, 0.5, 1);  // stone_slab2

        // And all three DOUBLE slabs stay full cubes, which is what they are.
        AssertSingleBox(LegacyShape(47, blockId: 43), 0, 0, 0, 1, 1, 1);
        AssertSingleBox(LegacyShape(47, blockId: 125), 0, 0, 0, 1, 1, 1);
        AssertSingleBox(LegacyShape(47, blockId: 181), 0, 0, 0, 1, 1, 1);

        // Torch, rail, flower, sign, pressure plate, and redstone wire have no collision box.
        foreach (int blockId in new[] { 50, 66, 38, 63, 70, 55 })
        {
            Assert.True(shapes.Covers(blockId << 4), $"block {blockId} should be covered");
            Assert.True(shapes.GetCollisionShapes(blockId << 4).IsEmpty, $"block {blockId} should have no box");
        }

        // Soul sand is 0.125 below full height.
        AssertSingleBox(LegacyShape(47, blockId: 88), 0, 0, 0, 1, 0.875, 1);

        // Cactus is inset 0.0625 on X and Z and below the top.
        AssertSingleBox(LegacyShape(47, blockId: 81), 0.0625, 0, 0.0625, 0.9375, 0.9375, 0.9375);

        // A cauldron has a 0.3125 floor and four 0.125 walls.
        ReadOnlySpan<Aabb> cauldron = LegacyShape(47, blockId: 118);
        Assert.Equal(5, cauldron.Length);
        AssertBox(cauldron[0], 0, 0, 0, 1, 0.3125, 1);

        // Not curated, and deliberately so: a fence's real box needs its NEIGHBOURS, which the pre-flattening wire does not carry, so it keeps the conservative flag-derived cube.
        Assert.False(shapes.Covers(85 << 4));
        AssertSingleBox(shapes.GetCollisionShapes(85 << 4), 0, 0, 0, 1, 1, 1);

        // Protocol 47 farmland collides as a full cube; the shorter collision begins in 1.9.
        Assert.False(shapes.Covers(60 << 4));
        AssertSingleBox(shapes.GetCollisionShapes(60 << 4), 0, 0, 0, 1, 1, 1);
    }

    /// <summary>An unsupported protocol has no tables and must be refused instead of returning flag-derived fallback geometry.</summary>
    [Fact]
    public void An_unsupported_protocol_is_refused_rather_than_answered_from_flags()
    {
        Assert.Throws<ArgumentOutOfRangeException>(static () => JavaGameData.BlockShapes(-1));
        Assert.Throws<ArgumentOutOfRangeException>(static () => JavaGameData.Registries(-1));
        Assert.Throws<ArgumentOutOfRangeException>(static () => JavaGameData.BlockPushData(-1));
        Assert.Throws<ArgumentOutOfRangeException>(static () => JavaGameData.SoundName(-1, 0));
    }

    [Fact]
    public void The_same_protocol_returns_the_same_cached_source()
        => Assert.Same(JavaGameData.BlockShapes(770), JavaGameData.BlockShapes(770));

    /// <summary>Outline shapes have no separate table in the dataset, so they answer with the collision shapes. This preserves the current approximation until a separate outline table exists.</summary>
    [Fact]
    public void Outline_shapes_currently_mirror_collision_shapes()
    {
        IBlockShapeSource shapes = JavaGameData.BlockShapes(770);
        int fence = DefaultStateId(770, "minecraft:oak_fence");
        Assert.Equal(shapes.GetCollisionShapes(fence).ToArray(), shapes.GetOutlineShapes(fence).ToArray());
    }

    private static ReadOnlySpan<Aabb> DefaultShape(int protocol, string blockName)
        => JavaGameData.BlockShapes(protocol).GetCollisionShapes(DefaultStateId(protocol, blockName));

    /// <summary>The collision shape of the state whose <c>facing</c> property table says it faces <paramref name="facing"/>, with every later property at its first value.</summary>
    private static ReadOnlySpan<Aabb> FacingShape(int protocol, string blockName, string facing)
    {
        Registry<BlockDefinition> blocks = JavaGameData.Registries(protocol).Blocks;
        Assert.True(
            blocks.TryGetValue(Identifier.Parse(blockName), out BlockDefinition? definition),
            $"protocol {protocol} has no block {blockName}");

        int index = -1;
        for (int i = 0; i < definition.Properties.Count; i++)
            if (definition.Properties[i].Name == "facing")
            {
                index = i;
                break;
            }

        Assert.True(index >= 0, $"protocol {protocol}: {blockName} has no 'facing' property");

        IReadOnlyList<string> values = definition.Properties[index].Values;
        int value = -1;
        for (int i = 0; i < values.Count; i++)
            if (values[i] == facing)
            {
                value = i;
                break;
            }

        Assert.True(value >= 0, $"protocol {protocol}: {blockName} facing domain has no '{facing}'");

        // Vanilla's StateDefinition is an odometer over the name-sorted properties with the LAST one varying fastest, so one step of `facing` is the product of every later property's size.
        int stride = 1;
        for (int i = index + 1; i < definition.Properties.Count; i++)
            stride *= definition.Properties[i].Values.Count;

        return JavaGameData.BlockShapes(protocol)
            .GetCollisionShapes(definition.MinStateId + (value * stride));
    }

    private static ReadOnlySpan<Aabb> LegacyShape(int protocol, int blockId)
        => JavaGameData.BlockShapes(protocol).GetCollisionShapes(blockId << 4);

    private static int DefaultStateId(int protocol, string blockName)
    {
        Registry<BlockDefinition> blocks = JavaGameData.Registries(protocol).Blocks;
        Assert.True(
            blocks.TryGetValue(Identifier.Parse(blockName), out BlockDefinition? definition),
            $"protocol {protocol} has no block {blockName}");
        return definition.DefaultStateId;
    }

    private static void AssertSingleBox(ReadOnlySpan<Aabb> shape,
        double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
    {
        Assert.Equal(1, shape.Length);
        AssertBox(shape[0], minX, minY, minZ, maxX, maxY, maxZ);
    }

    private static void AssertBox(in Aabb box,
        double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
    {
        Assert.Equal(minX, box.MinX, 6);
        Assert.Equal(minY, box.MinY, 6);
        Assert.Equal(minZ, box.MinZ, 6);
        Assert.Equal(maxX, box.MaxX, 6);
        Assert.Equal(maxY, box.MaxY, 6);
        Assert.Equal(maxZ, box.MaxZ, 6);
    }
}
