using Umpk.Game.Registries;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Data.Java.Tests;

/// <summary>Pre-flattening collision geometry is resolved PER METADATA, not per block id.</summary>
/// <remarks>Every assertion names a variant whose correct box differs from metadata zero and checks both the expected value and the difference. This prevents a block-id-only lookup from collapsing all sixteen metadata states onto one shape.</remarks>
public sealed class MetadataCollisionShapeTests
{
    /// <summary>Every supported pre-flattening protocol.</summary>
    public static TheoryData<int> LegacyProtocols => [47, 107, 108, 109, 110, 210, 315, 316, 335, 338, 340];

    /// <summary>The bands with expanded legacy shape coverage.</summary>
    public static TheoryData<int> ExtractedLegacyProtocols => [107, 108, 109, 110, 210, 315, 316, 335, 338, 340];

    /// <summary>Three 1.8 half-slab ids plus the purpur slab added in 1.9.</summary>
    public static TheoryData<int, int> SlabIdsOnEveryLegacyBand
    {
        get
        {
            var data = new TheoryData<int, int>();
            foreach (int protocol in new[] { 47, 107, 108, 109, 110, 210, 315, 316, 335, 338, 340 })
                foreach (int blockId in new[] { 44, 126, 182 })
                    data.Add(protocol, blockId);

            return data;
        }
    }

    /// <summary>A top slab is the top half of the block and a bottom slab is the bottom half, so they are DIFFERENT boxes. This is the assertion the whole stream exists for.</summary>
    /// <remarks>Metadata bit 3 selects the top half; lower metadata bits select the material.</remarks>
    [Theory]
    [MemberData(nameof(SlabIdsOnEveryLegacyBand))]
    public void A_top_slab_is_a_different_box_from_a_bottom_slab(int protocol, int blockId)
    {
        ReadOnlySpan<Aabb> bottom = Shape(protocol, blockId, meta: 0);
        ReadOnlySpan<Aabb> top = Shape(protocol, blockId, meta: 8);

        AssertSingleBox(bottom, 0, 0, 0, 1, 0.5, 1);
        AssertSingleBox(top, 0, 0.5, 0, 1, 1, 1);
        Assert.NotEqual(bottom[0].MaxY, top[0].MaxY);
        Assert.NotEqual(bottom[0].MinY, top[0].MinY);
    }

    /// <summary>The variant bits (meta 0-7) pick stone/sandstone/wood, not the half, so 7 is bottom.</summary>
    /// <remarks>Overlaps <c>A_top_slab_is_a_different_box_from_a_bottom_slab</c> for meta 0 and 8 but is not redundant with it: metas 7 and 15 are the far end of each half's variant range, which is where an off-by-one in the meta mask (7 vs 8) would show and the 0/8 pair would not.</remarks>
    [Theory]
    [MemberData(nameof(LegacyProtocols))]
    public void Slab_variant_bits_do_not_move_the_half(int protocol)
    {
        AssertSingleBox(Shape(protocol, 44, meta: 7), 0, 0, 0, 1, 0.5, 1);
        AssertSingleBox(Shape(protocol, 44, meta: 15), 0, 0.5, 0, 1, 1, 1);
    }

    /// <summary>A ladder answers a different box for each of its four facings, and the three non-default ones are asserted explicitly so a permuted or collapsed facing domain cannot pass.</summary>
    /// <remarks>Metadata 2, 3, 4, and 5 select north, south, west, and east respectively.</remarks>
    [Theory]
    [MemberData(nameof(ExtractedLegacyProtocols))]
    public void A_ladder_answers_a_different_box_for_each_facing(int protocol)
    {
        AssertSingleBox(Shape(protocol, 65, meta: 2), 0, 0, 0.8125, 1, 1, 1);   // north
        AssertSingleBox(Shape(protocol, 65, meta: 3), 0, 0, 0, 1, 1, 0.1875);   // south
        AssertSingleBox(Shape(protocol, 65, meta: 4), 0.8125, 0, 0, 1, 1, 1);   // west
        AssertSingleBox(Shape(protocol, 65, meta: 5), 0, 0, 0, 0.1875, 1, 1);   // east

        // Four facings require four distinct boxes.
        var seen = new HashSet<string>();
        foreach (int meta in new[] { 2, 3, 4, 5 })
            Assert.True(seen.Add(Describe(Shape(protocol, 65, meta))), $"meta {meta} repeats a box");

    }

    /// <summary>Metas 6-15 wrap through the same six facings, so meta 10 is west just as meta 4 is.</summary>
    [Theory]
    [MemberData(nameof(ExtractedLegacyProtocols))]
    public void Ladder_facing_wraps_every_six_metas(int protocol)
    {
        Assert.Equal(Describe(Shape(protocol, 65, meta: 4)), Describe(Shape(protocol, 65, meta: 10)));
        Assert.Equal(Describe(Shape(protocol, 65, meta: 5)), Describe(Shape(protocol, 65, meta: 11)));

        // meta 0 and 1 are DOWN and UP, which vanilla clamps to NORTH rather than leaving invalid.
        Assert.Equal(Describe(Shape(protocol, 65, meta: 2)), Describe(Shape(protocol, 65, meta: 0)));
    }

    /// <summary>An OPEN fence gate has no collision at all, which is the difference between a bot walking through a gate it opened and a bot standing in front of it forever.</summary>
    /// <remarks>Bit 2 selects the open state; lower bits select the horizontal axis.</remarks>
    [Theory]
    [MemberData(nameof(ExtractedLegacyProtocols))]
    public void An_open_fence_gate_has_no_collision(int protocol)
    {
        AssertSingleBox(Shape(protocol, 107, meta: 0), 0, 0, 0.375, 1, 1.5, 0.625);  // closed, south
        AssertSingleBox(Shape(protocol, 107, meta: 1), 0.375, 0, 0, 0.625, 1.5, 1);  // closed, west
        Assert.True(Shape(protocol, 107, meta: 4).IsEmpty, "an open gate still collided");
        Assert.True(Shape(protocol, 107, meta: 5).IsEmpty, "an open gate still collided");
    }

    /// <summary>Snow layers collide at (meta &amp; 7) * 0.125, so one layer has no collision and eight is a step you can walk up.</summary>
    /// <remarks>The low three metadata bits encode one through eight layers.</remarks>
    [Theory]
    [MemberData(nameof(ExtractedLegacyProtocols))]
    public void Snow_layers_grow_an_eighth_of_a_block_at_a_time(int protocol)
    {
        Assert.True(Shape(protocol, 78, meta: 0).IsEmpty, "one snow layer must not collide");
        for (int meta = 1; meta <= 7; meta++)
            AssertSingleBox(Shape(protocol, 78, meta), 0, 0, 0, 1, meta * 0.125, 1);

    }

    /// <summary>A trapdoor's half and open flags each move the box, and an open one stands on a wall rather than lying on the floor.</summary>
    /// <remarks>The low two bits select facing, bit 2 selects open, and bit 3 selects the half.</remarks>
    [Theory]
    [MemberData(nameof(ExtractedLegacyProtocols))]
    public void A_trapdoor_moves_with_its_half_and_its_open_flag(int protocol)
    {
        AssertSingleBox(Shape(protocol, 96, meta: 0), 0, 0, 0, 1, 0.1875, 1);       // closed, bottom
        AssertSingleBox(Shape(protocol, 96, meta: 8), 0, 0.8125, 0, 1, 1, 1);       // closed, top
        AssertSingleBox(Shape(protocol, 96, meta: 4), 0, 0, 0.8125, 1, 1, 1);       // open, north
        AssertSingleBox(Shape(protocol, 96, meta: 6), 0.8125, 0, 0, 1, 1, 1);       // open, west
        AssertSingleBox(Shape(protocol, 167, meta: 8), 0, 0.8125, 0, 1, 1, 1);      // iron, closed top
    }

    /// <summary>The supplement may only REFINE coverage. Every metadata variant of every block id the base table covers must still resolve to a shape, because a legacy meta that quietly became uncovered would drop a player through a floor rather than merely stand them slightly wrong.</summary>
    /// <remarks>Unlisted metadata variants inherit their block's base shape.</remarks>
    [Theory]
    [MemberData(nameof(LegacyProtocols))]
    public void No_metadata_variant_lost_the_coverage_its_block_had(int protocol)
    {
        var shapes = (JavaBlockShapes)JavaGameData.BlockShapes(protocol);
        List<int> lost = [];
        for (int blockId = 0; blockId < 256; blockId++)
        {
            if (!shapes.Covers(blockId << 4))
                continue;

            for (int meta = 1; meta < 16; meta++)
                if (!shapes.Covers((blockId << 4) | meta))
                    lost.Add((blockId << 4) | meta);

        }

        Assert.Empty(lost);
    }

    /// <summary>A block id the base table does not cover is still uncovered for every meta, so the honest flag-derived fallback keeps answering rather than a shape appearing out of nowhere.</summary>
    [Fact]
    public void An_uncovered_block_id_stays_uncovered_for_every_meta()
    {
        var shapes = (JavaBlockShapes)JavaGameData.BlockShapes(47);
        for (int meta = 0; meta < 16; meta++)
        {
            Assert.False(shapes.Covers((85 << 4) | meta), $"1.8 fence meta {meta}");
            AssertSingleBox(shapes.GetCollisionShapes((85 << 4) | meta), 0, 0, 0, 1, 1, 1);
        }
    }

    /// <summary>A state id beyond the table answers the fallback rather than throwing or reading out of range.</summary>
    [Theory]
    [MemberData(nameof(LegacyProtocols))]
    public void A_state_id_past_the_end_of_the_table_falls_back(int protocol)
        => AssertSingleBox(JavaGameData.BlockShapes(protocol).GetCollisionShapes(0xFFFFF), 0, 0, 0, 1, 1, 1);

    /// <summary>A NEGATIVE state id is the other out-of-range branch (<c>Lookup</c> rejects it before the bounds check) and must answer the same unit cube, not an empty shape.</summary>
    /// <remarks>Negative ids must use the same unit-cube fallback as other out-of-range ids.</remarks>
    [Theory]
    [MemberData(nameof(LegacyProtocols))]
    public void A_negative_state_id_falls_back_to_the_unit_cube(int protocol)
        => AssertSingleBox(JavaGameData.BlockShapes(protocol).GetCollisionShapes(-1), 0, 0, 0, 1, 1, 1);

    /// <summary>The fourteen pre-flattening stair block ids, which must all resolve identically.</summary>
    public static TheoryData<int> StairBlockIds
        => [53, 67, 108, 109, 114, 128, 134, 135, 136, 156, 163, 164, 180, 203];

    /// <summary>The seven pre-flattening door block ids.</summary>
    public static TheoryData<int> DoorBlockIds => [64, 71, 193, 194, 195, 196, 197];

    /// <summary>A STRAIGHT stair answers a different pair of boxes for each facing and each half, and all eight are distinct, so a permuted facing domain or a dropped half bit cannot pass.</summary>
    /// <remarks>Metadata bit 2 selects the half; the low two bits select east, west, south, or north. Metadata zero is east/bottom, so seven assertions exercise a shape different from the default pair.</remarks>
    [Theory]
    [MemberData(nameof(ExtractedLegacyProtocols))]
    public void A_straight_stair_carries_vanillas_boxes_for_each_facing_and_half(int protocol)
    {
        double[] bottomSlab = [0, 0, 0, 1, 0.5, 1];
        double[] topSlab = [0, 0.5, 0, 1, 1, 1];

        AssertShape(Shape(protocol, 53, meta: 0), bottomSlab, [0.5, 0.5, 0, 1, 1, 1]);      // east
        AssertShape(Shape(protocol, 53, meta: 1), bottomSlab, [0, 0.5, 0, 0.5, 1, 1]);      // west
        AssertShape(Shape(protocol, 53, meta: 2), bottomSlab, [0, 0.5, 0.5, 1, 1, 1]);      // south
        AssertShape(Shape(protocol, 53, meta: 3), bottomSlab, [0, 0.5, 0, 1, 1, 0.5]);      // north
        AssertShape(Shape(protocol, 53, meta: 4), topSlab, [0.5, 0, 0, 1, 0.5, 1]);         // east, top
        AssertShape(Shape(protocol, 53, meta: 5), topSlab, [0, 0, 0, 0.5, 0.5, 1]);         // west, top
        AssertShape(Shape(protocol, 53, meta: 6), topSlab, [0, 0, 0.5, 1, 0.5, 1]);         // south, top
        AssertShape(Shape(protocol, 53, meta: 7), topSlab, [0, 0, 0, 1, 0.5, 0.5]);         // north, top

        var seen = new HashSet<string>();
        for (int meta = 0; meta < 8; meta++)
            Assert.True(seen.Add(Describe(Shape(protocol, 53, meta))), $"meta {meta} repeats a box pair");

        // Bit 3 is ignored, so metadata values 8-15 repeat the same eight states.
        for (int meta = 0; meta < 8; meta++)
            Assert.Equal(Describe(Shape(protocol, 53, meta)), Describe(Shape(protocol, 53, meta + 8)));

    }

    /// <summary>A stair's corner <c>shape</c> is not in the metadata, so no corner geometry may appear here.</summary>
    /// <remarks>Corner shape depends on the stair blocks in front of and behind this one. That is neighbour state the pre-flattening wire does not carry, exactly like a fence connection. An inner corner is THREE boxes and an outer corner is a slab plus a quarter; asserting that every meta is a two-box straight form and that there are only eight of them prevents inferring corner geometry from metadata that does not encode it.</remarks>
    [Theory]
    [MemberData(nameof(ExtractedLegacyProtocols))]
    public void A_stair_never_invents_the_neighbour_derived_corner_shapes(int protocol)
    {
        var distinct = new HashSet<string>();
        for (int meta = 0; meta < 16; meta++)
        {
            ReadOnlySpan<Aabb> shape = Shape(protocol, 53, meta);
            Assert.Equal(2, shape.Length);
            distinct.Add(Describe(shape));
        }

        Assert.Equal(8, distinct.Count);
    }

    /// <summary>All fourteen stair ids share one rule, so a missing family entry cannot hide.</summary>
    /// <remarks>Meta 3 is north/bottom and meta 6 is south/top: neither is the meta-0 pair, so a stair id that was left out of the supplement answers the east/bottom pair here and fails.</remarks>
    [Theory]
    [MemberData(nameof(StairBlockIds))]
    public void Every_stair_id_resolves_the_same_way(int blockId)
    {
        AssertShape(Shape(340, blockId, meta: 3), [0, 0, 0, 1, 0.5, 1], [0, 0.5, 0, 1, 1, 0.5]);
        AssertShape(Shape(340, blockId, meta: 6), [0, 0.5, 0, 1, 1, 1], [0, 0, 0.5, 1, 0.5, 1]);
    }

    /// <summary>A CLOSED door answers vanilla's box for each of its four facings, and the three non-default ones are asserted explicitly.</summary>
    /// <remarks>The low two metadata bits select east, south, west, and north respectively.</remarks>
    [Theory]
    [MemberData(nameof(ExtractedLegacyProtocols))]
    public void A_closed_door_carries_vanillas_box_for_each_facing(int protocol)
    {
        AssertSingleBox(Shape(protocol, 64, meta: 0), 0, 0, 0, 0.1875, 1, 1);       // east
        AssertSingleBox(Shape(protocol, 64, meta: 1), 0, 0, 0, 1, 1, 0.1875);       // south
        AssertSingleBox(Shape(protocol, 64, meta: 2), 0.8125, 0, 0, 1, 1, 1);       // west
        AssertSingleBox(Shape(protocol, 64, meta: 3), 0, 0, 0.8125, 1, 1, 1);       // north

        var seen = new HashSet<string>();
        for (int meta = 0; meta < 4; meta++)
            Assert.True(seen.Add(Describe(Shape(protocol, 64, meta))), $"meta {meta} repeats a box");

    }

    /// <summary>All seven door ids share one rule.</summary>
    /// <remarks>Meta 2 is the WEST panel, which is not the meta-0 box, so a missing id fails here.</remarks>
    [Theory]
    [MemberData(nameof(DoorBlockIds))]
    public void Every_door_id_resolves_the_same_way(int blockId)
        => AssertSingleBox(Shape(340, blockId, meta: 2), 0.8125, 0, 0, 1, 1, 1);

    /// <summary>The door limitation, pinned: an OPEN lower door and the whole UPPER half are neighbour-derived, so they must keep answering the block's meta-0 box rather than an invented one.</summary>
    /// <remarks>Hinge and powered state come from the block above, while facing and open state for the upper half come from the block below. An open door's two hinge answers are boxes on OPPOSITE sides of the block (facing EAST open gives <c>f</c> for hinge LEFT and <c>g</c> for hinge RIGHT), so choosing one would be inventing geometry from metadata alone.</remarks>
    [Theory]
    [MemberData(nameof(ExtractedLegacyProtocols))]
    public void An_open_or_upper_door_keeps_the_blocks_meta_0_box(int protocol)
    {
        string metaZero = Describe(Shape(protocol, 64, meta: 0));
        for (int meta = 4; meta < 16; meta++)
            Assert.Equal(metaZero, Describe(Shape(protocol, 64, meta)));

    }

    /// <summary>An EXTENDED piston base is a quarter shorter on the side its head went, which is the difference between a bot standing on an extended piston and standing a quarter block inside it.</summary>
    /// <remarks>Bit 3 selects extended; the low three bits select one of six valid facings.</remarks>
    [Theory]
    [MemberData(nameof(ExtractedLegacyProtocols))]
    public void An_extended_piston_base_is_shorter_on_its_facing_side(int protocol)
    {
        AssertSingleBox(Shape(protocol, 33, meta: 8), 0, 0.25, 0, 1, 1, 1);         // extended down
        AssertSingleBox(Shape(protocol, 33, meta: 9), 0, 0, 0, 1, 0.75, 1);         // extended up
        AssertSingleBox(Shape(protocol, 33, meta: 10), 0, 0, 0.25, 1, 1, 1);        // extended north
        AssertSingleBox(Shape(protocol, 33, meta: 11), 0, 0, 0, 1, 1, 0.75);        // extended south
        AssertSingleBox(Shape(protocol, 33, meta: 12), 0.25, 0, 0, 1, 1, 1);        // extended west
        AssertSingleBox(Shape(protocol, 33, meta: 13), 0, 0, 0, 0.75, 1, 1);        // extended east
        AssertSingleBox(Shape(protocol, 29, meta: 12), 0.25, 0, 0, 1, 1, 1);        // sticky, west

        // Retracted: still the full cube, for every facing.
        for (int meta = 0; meta <= 5; meta++)
            AssertSingleBox(Shape(protocol, 33, meta), 0, 0, 0, 1, 1, 1);

        // Facings above five are invalid and retain the metadata-zero answer.
        AssertSingleBox(Shape(protocol, 33, meta: 14), 0, 0, 0, 1, 1, 1);
        AssertSingleBox(Shape(protocol, 33, meta: 15), 0, 0, 0, 1, 1, 1);
    }

    /// <summary>A piston head carries its plate on the facing face and its arm running back from it.</summary>
    /// <remarks>The arm extends 0.25 into the piston base; wire states always use the long arm.</remarks>
    [Theory]
    [MemberData(nameof(ExtractedLegacyProtocols))]
    public void A_piston_head_carries_its_plate_and_arm_for_each_facing(int protocol)
    {
        AssertShape(Shape(protocol, 34, meta: 1),
            [0, 0.75, 0, 1, 1, 1], [0.375, -0.25, 0.375, 0.625, 0.75, 0.625]);          // up
        AssertShape(Shape(protocol, 34, meta: 2),
            [0, 0, 0, 1, 1, 0.25], [0.375, 0.375, 0.25, 0.625, 0.625, 1.25]);           // north
        AssertShape(Shape(protocol, 34, meta: 3),
            [0, 0, 0.75, 1, 1, 1], [0.375, 0.375, -0.25, 0.625, 0.625, 0.75]);          // south
        AssertShape(Shape(protocol, 34, meta: 4),
            [0, 0, 0, 0.25, 1, 1], [0.25, 0.375, 0.375, 1.25, 0.625, 0.625]);           // west
        AssertShape(Shape(protocol, 34, meta: 5),
            [0.75, 0, 0, 1, 1, 1], [-0.25, 0.375, 0.375, 0.75, 0.625, 0.625]);          // east

        // The sticky bit is meta 8 and moves nothing.
        Assert.Equal(Describe(Shape(protocol, 34, meta: 4)), Describe(Shape(protocol, 34, meta: 12)));
    }

    /// <summary>An anvil is narrow across the axis it faces, so a west anvil is not a south one.</summary>
    /// <remarks>The low two bits select facing; higher damage bits do not change geometry.</remarks>
    [Theory]
    [MemberData(nameof(ExtractedLegacyProtocols))]
    public void An_anvil_turns_with_its_facing_axis(int protocol)
    {
        AssertSingleBox(Shape(protocol, 145, meta: 0), 0.125, 0, 0, 0.875, 1, 1);   // south
        AssertSingleBox(Shape(protocol, 145, meta: 1), 0, 0, 0.125, 1, 1, 0.875);   // west
        AssertSingleBox(Shape(protocol, 145, meta: 2), 0.125, 0, 0, 0.875, 1, 1);   // north
        AssertSingleBox(Shape(protocol, 145, meta: 3), 0, 0, 0.125, 1, 1, 0.875);   // east

        // The damage bits ride above the facing and must not move the box.
        AssertSingleBox(Shape(protocol, 145, meta: 7), 0, 0, 0.125, 1, 1, 0.875);   // east, damaged
    }

    /// <summary>Each bite eats an eighth of the cake off its -X side.</summary>
    /// <remarks>The bite count is the metadata value and caps at six; higher values retain metadata zero.</remarks>
    [Theory]
    [MemberData(nameof(ExtractedLegacyProtocols))]
    public void A_cake_loses_an_eighth_of_itself_per_bite(int protocol)
    {
        for (int bites = 0; bites <= 6; bites++)
            AssertSingleBox(Shape(protocol, 92, bites),
                (1 + (bites * 2)) / 16.0, 0, 0.0625, 0.9375, 0.5, 0.9375);

        AssertSingleBox(Shape(protocol, 92, meta: 15), 0.0625, 0, 0.0625, 0.9375, 0.5, 0.9375);
    }

    /// <summary>An end portal frame with an eye grows a second box on top of it.</summary>
    /// <remarks>Bit 2 selects the eye; facing does not change the collision boxes.</remarks>
    [Theory]
    [MemberData(nameof(ExtractedLegacyProtocols))]
    public void An_end_portal_frame_with_an_eye_carries_a_second_box(int protocol)
    {
        AssertSingleBox(Shape(protocol, 120, meta: 0), 0, 0, 0, 1, 0.8125, 1);
        AssertShape(Shape(protocol, 120, meta: 4),
            [0, 0, 0, 1, 0.8125, 1], [0.3125, 0.8125, 0.3125, 0.6875, 1, 0.6875]);
        AssertShape(Shape(protocol, 120, meta: 7),
            [0, 0, 0, 1, 0.8125, 1], [0.3125, 0.8125, 0.3125, 0.6875, 1, 0.6875]);
    }

    /// <summary>A wall skull mounts behind its facing, the opposite convention from a ladder.</summary>
    /// <remarks>Metadata values 2 through 5 select the four wall facings. Values 6 and 7 wrap to down and up, and the no-drop bit does not alter geometry.</remarks>
    [Theory]
    [MemberData(nameof(ExtractedLegacyProtocols))]
    public void A_wall_skull_mounts_behind_its_facing(int protocol)
    {
        AssertSingleBox(Shape(protocol, 144, meta: 0), 0.25, 0, 0.25, 0.75, 0.5, 0.75);     // down
        AssertSingleBox(Shape(protocol, 144, meta: 2), 0.25, 0.25, 0.5, 0.75, 0.75, 1);     // north
        AssertSingleBox(Shape(protocol, 144, meta: 3), 0.25, 0.25, 0, 0.75, 0.75, 0.5);     // south
        AssertSingleBox(Shape(protocol, 144, meta: 4), 0.5, 0.25, 0.25, 1, 0.75, 0.75);     // west
        AssertSingleBox(Shape(protocol, 144, meta: 5), 0, 0.25, 0.25, 0.5, 0.75, 0.75);     // east

        var seen = new HashSet<string>();
        foreach (int meta in new[] { 2, 3, 4, 5 })
            Assert.True(seen.Add(Describe(Shape(protocol, 144, meta))), $"meta {meta} repeats a box");

        // meta & 7 of 6 and 7 folds back to DOWN and UP, and the nodrop bit moves nothing.
        Assert.Equal(Describe(Shape(protocol, 144, meta: 0)), Describe(Shape(protocol, 144, meta: 6)));
        Assert.Equal(Describe(Shape(protocol, 144, meta: 4)), Describe(Shape(protocol, 144, meta: 12)));
    }

    /// <summary>An end rod lies down when it points sideways.</summary>
    /// <remarks>The six facings wrap every six metadata values.</remarks>
    [Theory]
    [MemberData(nameof(ExtractedLegacyProtocols))]
    public void An_end_rod_lies_down_when_it_faces_sideways(int protocol)
    {
        AssertSingleBox(Shape(protocol, 198, meta: 0), 0.375, 0, 0.375, 0.625, 1, 0.625);   // down
        AssertSingleBox(Shape(protocol, 198, meta: 2), 0.375, 0.375, 0, 0.625, 0.625, 1);   // north
        AssertSingleBox(Shape(protocol, 198, meta: 4), 0, 0.375, 0.375, 1, 0.625, 0.625);   // west

        // Metadata 10 wraps to west just as metadata 4 does.
        Assert.Equal(Describe(Shape(protocol, 198, meta: 4)), Describe(Shape(protocol, 198, meta: 10)));
        Assert.Equal(Describe(Shape(protocol, 198, meta: 0)), Describe(Shape(protocol, 198, meta: 6)));
    }

    /// <summary>The supplement must not create a NEW state that answers no boxes while its block's flags still say it blocks motion. Exactly one family may do that, and it is the fence gate.</summary>
    /// <remarks>
    /// <para><c>BlockAttributeResolver.CollisionRefs</c> still derives legacy <c>BlocksMotion</c> per BLOCK id from the meta-0 shape, so a state whose supplemented shape is EMPTY disagrees with its own flag: physics walks through it while <c>MoveHelper.IsPassable</c> still calls it impassable. That divergence was introduced deliberately for the open fence gate and documented in <c>JavaBlockShapes</c>. None of the families added since produces an empty shape, and this test is what fails if one ever does.</para>
    /// <para>The block ids are the six fence gates: 107 oak plus 183-187 spruce, birch, jungle, dark oak and acacia.</para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(LegacyProtocols))]
    public void Only_the_fence_gate_lets_a_meta_answer_no_boxes_at_all(int protocol)
    {
        var shapes = (JavaBlockShapes)JavaGameData.BlockShapes(protocol);
        List<int> emptied = [];
        for (int blockId = 0; blockId < 256; blockId++)
        {
            if (!shapes.Covers(blockId << 4) || shapes.GetCollisionShapes(blockId << 4).IsEmpty)
                continue;

            for (int meta = 1; meta < 16; meta++)
                if (shapes.GetCollisionShapes((blockId << 4) | meta).IsEmpty)
                {
                    emptied.Add(blockId);
                    break;
                }

        }

        // Every legacy band covers the six fence-gate ids and reports no box for their open states.
        Assert.Equal([107, 183, 184, 185, 186, 187], emptied);
    }

    /// <summary>The other half of the flag divergence, pinned for the same reason: exactly one supplemented family stops being a FULL CUBE on a meta its block-level flags still call solid.</summary>
    /// <remarks>Legacy flags are derived from metadata zero. A piston base is a full cube while retracted and 12/16 deep while extended, so its per-state shape can differ from the block-level solid flag. Block ids 29 and 33 are the only affected families.</remarks>
    [Theory]
    [MemberData(nameof(LegacyProtocols))]
    public void Only_the_piston_base_stops_being_a_full_cube_on_a_later_meta(int protocol)
    {
        var shapes = (JavaBlockShapes)JavaGameData.BlockShapes(protocol);
        List<int> shrank = [];
        for (int blockId = 0; blockId < 256; blockId++)
        {
            if (!shapes.Covers(blockId << 4) || !IsUnitCube(shapes.GetCollisionShapes(blockId << 4)))
                continue;

            for (int meta = 1; meta < 16; meta++)
                if (!IsUnitCube(shapes.GetCollisionShapes((blockId << 4) | meta)))
                {
                    shrank.Add(blockId);
                    break;
                }

        }

        // Every legacy band reports the shortened extended shape for piston ids 29 and 33.
        Assert.Equal([29, 33], shrank);
    }

    // Protocol 47 has its own assertions because some dimensions differ from later legacy bands.

    /// <summary>1.8's ladder is 2/16 deep and 1.9's is 3/16, so this is the discriminator that says the 1.8 boxes use protocol 47's dimensions rather than protocol 107's.</summary>
    /// <remarks>Protocol 47 uses a 0.125-deep ladder; protocol 107 uses 0.1875.</remarks>
    [Fact]
    public void The_1_8_ladder_is_two_sixteenths_deep_not_the_1_9_three()
    {
        AssertSingleBox(Shape(47, 65, meta: 2), 0, 0, 0.875, 1, 1, 1);   // north
        AssertSingleBox(Shape(47, 65, meta: 3), 0, 0, 0, 1, 1, 0.125);   // south
        AssertSingleBox(Shape(47, 65, meta: 4), 0.875, 0, 0, 1, 1, 1);   // west
        AssertSingleBox(Shape(47, 65, meta: 5), 0, 0, 0, 0.125, 1, 1);   // east

        // Four DISTINCT boxes, so an assertion that only checked north could not pass by accident.
        var seen = new HashSet<string>();
        foreach (int meta in new[] { 2, 3, 4, 5 })
            Assert.True(seen.Add(Describe(Shape(47, 65, meta))), $"meta {meta} repeats a box");

        // And the whole point: 47 does NOT answer 107's box. A rule that hardcoded 1.9's 0.1875 instead of reading the band's own meta-0 box would make these two equal.
        Assert.NotEqual(Describe(Shape(107, 65, meta: 3)), Describe(Shape(47, 65, meta: 3)));
    }

    /// <summary>The 1.8 trapdoor: closed bottom, closed top and all four open panels, at 1.8's own 3/16.</summary>
    /// <remarks>Protocol 47 uses a 0.1875-thick panel with facing, open, and half metadata bits.</remarks>
    [Fact]
    public void The_1_8_trapdoor_opens_onto_each_facing_and_flips_half()
    {
        AssertSingleBox(Shape(47, 96, meta: 0), 0, 0, 0, 1, 0.1875, 1);       // closed, bottom
        AssertSingleBox(Shape(47, 96, meta: 8), 0, 0.8125, 0, 1, 1, 1);       // closed, top
        AssertSingleBox(Shape(47, 96, meta: 4), 0, 0, 0.8125, 1, 1, 1);       // open, north
        AssertSingleBox(Shape(47, 96, meta: 5), 0, 0, 0, 1, 1, 0.1875);       // open, south
        AssertSingleBox(Shape(47, 96, meta: 6), 0.8125, 0, 0, 1, 1, 1);       // open, west
        AssertSingleBox(Shape(47, 96, meta: 7), 0, 0, 0, 0.1875, 1, 1);       // open, east

        // The iron trapdoor is a separate block id with the same geometry.
        AssertSingleBox(Shape(47, 167, meta: 4), 0, 0, 0.8125, 1, 1, 1);
    }

    /// <summary>The 1.8 fence gate: 1.5 blocks tall, turned by its facing AXIS, and gone entirely when open.</summary>
    /// <remarks>Lower bits select the horizontal axis and bit 2 selects the open state.</remarks>
    [Fact]
    public void The_1_8_fence_gate_turns_with_its_axis_and_vanishes_when_open()
    {
        AssertSingleBox(Shape(47, 107, meta: 0), 0, 0, 0.375, 1, 1.5, 0.625);   // south, closed
        AssertSingleBox(Shape(47, 107, meta: 2), 0, 0, 0.375, 1, 1.5, 0.625);   // north, closed
        AssertSingleBox(Shape(47, 107, meta: 1), 0.375, 0, 0, 0.625, 1.5, 1);   // west, closed
        AssertSingleBox(Shape(47, 107, meta: 3), 0.375, 0, 0, 0.625, 1.5, 1);   // east, closed
        Assert.True(Shape(47, 107, meta: 4).IsEmpty, "an open fence gate has no collision box");
        Assert.True(Shape(47, 187, meta: 4).IsEmpty, "acacia gates behave the same");
    }

    /// <summary>The 1.8 stair, both halves and all four facings, as the slab plus the raised step.</summary>
    /// <remarks>A straight stair is two boxes; neighbour-derived corner shape is not encoded here.</remarks>
    [Fact]
    public void The_1_8_stair_is_a_slab_plus_a_raised_step_per_facing()
    {
        AssertShape(Shape(47, 53, meta: 0), [0, 0, 0, 1, 0.5, 1], [0.5, 0.5, 0, 1, 1, 1]);     // east
        AssertShape(Shape(47, 53, meta: 1), [0, 0, 0, 1, 0.5, 1], [0, 0.5, 0, 0.5, 1, 1]);     // west
        AssertShape(Shape(47, 53, meta: 2), [0, 0, 0, 1, 0.5, 1], [0, 0.5, 0.5, 1, 1, 1]);     // south
        AssertShape(Shape(47, 53, meta: 3), [0, 0, 0, 1, 0.5, 1], [0, 0.5, 0, 1, 1, 0.5]);     // north

        // meta | 4 is the upside-down form, which is the Y mirror of the same facing.
        AssertShape(Shape(47, 53, meta: 4), [0, 0.5, 0, 1, 1, 1], [0.5, 0, 0, 1, 0.5, 1]);

        // Every stair id in 1.8 answers the same geometry; 180 is the red sandstone stair, the newest.
        Assert.Equal(Describe(Shape(47, 53, meta: 2)), Describe(Shape(47, 180, meta: 2)));
    }

    /// <summary>The 1.8 door, closed lower half only, which is all the metadata can say.</summary>
    /// <remarks>Open and upper-half geometry requires neighbour state and retains metadata zero here.</remarks>
    [Fact]
    public void The_1_8_door_answers_each_closed_lower_facing()
    {
        AssertSingleBox(Shape(47, 64, meta: 0), 0, 0, 0, 0.1875, 1, 1);   // east
        AssertSingleBox(Shape(47, 64, meta: 1), 0, 0, 0, 1, 1, 0.1875);   // south
        AssertSingleBox(Shape(47, 64, meta: 2), 0.8125, 0, 0, 1, 1, 1);   // west
        AssertSingleBox(Shape(47, 64, meta: 3), 0, 0, 0.8125, 1, 1, 1);   // north
        AssertSingleBox(Shape(47, 197, meta: 3), 0, 0, 0.8125, 1, 1, 1);  // dark oak, same geometry
    }

    /// <summary>The 1.8 snow layer, whose collision height is one eighth per layer BELOW the first.</summary>
    /// <remarks>The low three bits encode one through eight layers; one layer has no collision.</remarks>
    [Fact]
    public void The_1_8_snow_layer_grows_an_eighth_per_layer_and_starts_at_nothing()
    {
        Assert.True(Shape(47, 78, meta: 0).IsEmpty, "one layer of snow has no collision");
        AssertSingleBox(Shape(47, 78, meta: 1), 0, 0, 0, 1, 0.125, 1);
        AssertSingleBox(Shape(47, 78, meta: 3), 0, 0, 0, 1, 0.375, 1);
        AssertSingleBox(Shape(47, 78, meta: 7), 0, 0, 0, 1, 0.875, 1);

        // meta 8-15 repeat 0-7, because LAYERS reads only the low three bits.
        Assert.True(Shape(47, 78, meta: 8).IsEmpty);
        AssertSingleBox(Shape(47, 78, meta: 11), 0, 0, 0, 1, 0.375, 1);
    }

    /// <summary>The remaining 1.8 families, each on a meta whose box is NOT the block's meta-0 box.</summary>
    /// <remarks>Each assertion selects metadata whose geometry differs from metadata zero.</remarks>
    [Fact]
    public void The_1_8_cake_skull_anvil_piston_and_end_frame_move_off_their_meta_zero_box()
    {
        // cake: each bite eats two sixteenths off minX.
        AssertSingleBox(Shape(47, 92, meta: 0), 0.0625, 0, 0.0625, 0.9375, 0.5, 0.9375);
        AssertSingleBox(Shape(47, 92, meta: 1), 0.1875, 0, 0.0625, 0.9375, 0.5, 0.9375);
        AssertSingleBox(Shape(47, 92, meta: 6), 0.8125, 0, 0.0625, 0.9375, 0.5, 0.9375);

        // skull: meta 0 is DOWN, which vanilla's switch answers with the FLOOR cube; meta 2 is NORTH.
        AssertSingleBox(Shape(47, 144, meta: 0), 0.25, 0, 0.25, 0.75, 0.5, 0.75);
        AssertSingleBox(Shape(47, 144, meta: 2), 0.25, 0.25, 0.5, 0.75, 0.75, 1);
        AssertSingleBox(Shape(47, 144, meta: 5), 0, 0.25, 0.25, 0.5, 0.75, 0.75);

        // anvil: meta 0 is SOUTH, a Z axis, so it is the X-narrowed box; meta 1 is WEST.
        AssertSingleBox(Shape(47, 145, meta: 0), 0.125, 0, 0, 0.875, 1, 1);
        AssertSingleBox(Shape(47, 145, meta: 1), 0, 0, 0.125, 1, 1, 0.875);

        // piston: retracted is a cube, extended gives up 0.25 on the facing side.
        Assert.True(IsUnitCube(Shape(47, 33, meta: 0)));
        AssertSingleBox(Shape(47, 33, meta: 8), 0, 0.25, 0, 1, 1, 1);      // down, extended
        AssertSingleBox(Shape(47, 33, meta: 9), 0, 0, 0, 1, 0.75, 1);      // up, extended
        AssertSingleBox(Shape(47, 33, meta: 12), 0.25, 0, 0, 1, 1, 1);     // west, extended
        AssertSingleBox(Shape(47, 33, meta: 13), 0, 0, 0, 0.75, 1, 1);     // east, extended
        Assert.True(IsUnitCube(Shape(47, 33, meta: 14)), "meta 14 is a facing vanilla refuses to build");

        // end portal frame: the eye is a second box on top.
        AssertSingleBox(Shape(47, 120, meta: 0), 0, 0, 0, 1, 0.8125, 1);
        AssertShape(Shape(47, 120, meta: 4),
            [0, 0, 0, 1, 0.8125, 1], [0.3125, 0.8125, 0.3125, 0.6875, 1, 0.6875]);
    }

    private static bool IsUnitCube(ReadOnlySpan<Aabb> shape)
        => shape.Length == 1
            && shape[0].MinX == 0 && shape[0].MinY == 0 && shape[0].MinZ == 0
            && shape[0].MaxX == 1 && shape[0].MaxY == 1 && shape[0].MaxZ == 1;

    private static ReadOnlySpan<Aabb> Shape(int protocol, int blockId, int meta)
        => JavaGameData.BlockShapes(protocol).GetCollisionShapes((blockId << 4) | meta);

    private static void AssertShape(ReadOnlySpan<Aabb> shape, params double[][] boxes)
    {
        Assert.Equal(boxes.Length, shape.Length);
        for (int i = 0; i < boxes.Length; i++)
        {
            double[] want = boxes[i];
            Assert.Equal(want[0], shape[i].MinX, 6);
            Assert.Equal(want[1], shape[i].MinY, 6);
            Assert.Equal(want[2], shape[i].MinZ, 6);
            Assert.Equal(want[3], shape[i].MaxX, 6);
            Assert.Equal(want[4], shape[i].MaxY, 6);
            Assert.Equal(want[5], shape[i].MaxZ, 6);
        }
    }

    private static string Describe(ReadOnlySpan<Aabb> shape)
    {
        var parts = new List<string>();
        foreach (Aabb box in shape)
            parts.Add($"{box.MinX},{box.MinY},{box.MinZ},{box.MaxX},{box.MaxY},{box.MaxZ}");

        return string.Join("|", parts);
    }

    private static void AssertSingleBox(ReadOnlySpan<Aabb> shape,
        double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
    {
        Assert.Equal(1, shape.Length);
        Assert.Equal(minX, shape[0].MinX, 6);
        Assert.Equal(minY, shape[0].MinY, 6);
        Assert.Equal(minZ, shape[0].MinZ, 6);
        Assert.Equal(maxX, shape[0].MaxX, 6);
        Assert.Equal(maxY, shape[0].MaxY, 6);
        Assert.Equal(maxZ, shape[0].MaxZ, 6);
    }
}
