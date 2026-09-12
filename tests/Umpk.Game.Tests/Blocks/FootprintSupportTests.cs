using System.Globalization;
using Umpk.Game.Blocks;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Game.Tests.Blocks;

/// <summary><see cref="BlockSupport.FootprintSupportHeight"/> and <see cref="BlockSupport.StepLadder"/> against the engine's own answers, recorded by driving a <c>PlayerPhysics</c> body over a <c>PlanningWorldView</c> built from these same boxes.</summary>
/// <remarks>
/// <para>Every collision box below is an independent literal protocol-774 shape. Shape ids are named per row.</para>
/// <para>Every expectation below is a recorded engine result rather than a re-derivation from these boxes:</para>
/// <list type="bullet">
/// <item><see cref="SweptLadder_AgreesWithTheEngine"/> covers all 116 shape/direction pairs.</item>
/// <item><see cref="RestElevation_IsWhereTheBodySettles"/> drops a body on each shape for 60 ticks.</item>
/// <item><see cref="NonZeroSourceElevation_AgreesWithTheEngine"/> and
/// <see cref="TheSingleCellLadder_MissesFiveOfTheSixNonZeroSourceRows"/> cover raised starts.</item>
/// </list>
/// </remarks>
public sealed class FootprintSupportTests
{
    /// <summary>Half the player's collision width: <c>PhysicsConstants.PlayerWidth</c> 0.6 over two.</summary>
    private const double Half = 0.3;

    private static Aabb B(double x0, double y0, double z0, double x1, double y1, double z1)
        => new(x0, y0, z0, x1, y1, z1);

    private static readonly Aabb[] Nothing = [];

    // Independent literal protocol-774 collision boxes.

    private static readonly Aabb[] Stone = [B(0, 0, 0, 1, 1, 1)];                            // shape 21
    private static readonly Aabb[] BottomSlab = [B(0, 0, 0, 1, 0.5, 1)];                     // shape 27
    private static readonly Aabb[] TopSlab = [B(0, 0.5, 0, 1, 1, 1)];                        // shape 26
    private static readonly Aabb[] Carpet = [B(0, 0, 0, 1, 0.0625, 1)];                      // shape 117
    private static readonly Aabb[] DirtPath = [B(0, 0, 0, 1, 0.9375, 1)];                    // shape 242
    private static readonly Aabb[] Farmland = [B(0, 0, 0, 1, 0.9375, 1)];                    // shape 242
    private static readonly Aabb[] SoulSand = [B(0, 0, 0, 1, 0.875, 1)];                     // shape 278
    private static readonly Aabb[] Honey = [B(0.0625, 0, 0.0625, 0.9375, 0.9375, 0.9375)];   // shape 135
    private static readonly Aabb[] LilyPad = [B(0.0625, 0, 0.0625, 0.9375, 0.09375, 0.9375)]; // shape 271

    /// <summary><c>minecraft:snow</c> refs <c>[0,226,321,240,27,322,244,278]</c> over <c>layers</c> 1..8: layers=1 is shape 0, the EMPTY shape, and 2..8 are flat tops at <c>(layers - 1) / 8</c>.</summary>
    private static Aabb[] Snow(int layers)
        => layers == 1 ? Nothing : [B(0, 0, 0, 1, (layers - 1) / 8.0, 1)];

    // Straight bottom stairs, shapes 33 (north) / 43 (south) / 49 (west) / 51 (east).
    private static readonly Aabb[] StairBottomNorth = [B(0, 0, 0, 1, 0.5, 1), B(0, 0.5, 0, 1, 1, 0.5)];
    private static readonly Aabb[] StairBottomSouth = [B(0, 0, 0, 1, 0.5, 1), B(0, 0.5, 0.5, 1, 1, 1)];
    private static readonly Aabb[] StairBottomWest = [B(0, 0, 0, 1, 0.5, 1), B(0, 0.5, 0, 0.5, 1, 1)];
    private static readonly Aabb[] StairBottomEast = [B(0, 0, 0, 1, 0.5, 1), B(0.5, 0.5, 0, 1, 1, 1)];

    // Straight TOP stairs (upside down), shapes 28 (north) / 38 (south) / 48 (west) / 50 (east).
    private static readonly Aabb[] StairTopNorth = [B(0, 0, 0, 1, 1, 0.5), B(0, 0.5, 0.5, 1, 1, 1)];
    private static readonly Aabb[] StairTopSouth = [B(0, 0, 0.5, 1, 1, 1), B(0, 0.5, 0, 1, 1, 0.5)];
    private static readonly Aabb[] StairTopWest = [B(0, 0, 0, 0.5, 1, 1), B(0.5, 0.5, 0, 1, 1, 1)];
    private static readonly Aabb[] StairTopEast = [B(0.5, 0, 0, 1, 1, 1), B(0, 0.5, 0, 0.5, 1, 1)];

    // Corner bottom stairs, facing=north, shapes 34 / 35 / 36 / 37.
    private static readonly Aabb[] StairInnerLeftN =
        [B(0, 0, 0, 1, 0.5, 1), B(0, 0.5, 0, 0.5, 1, 1), B(0.5, 0.5, 0, 1, 1, 0.5)];

    private static readonly Aabb[] StairInnerRightN =
        [B(0, 0, 0, 1, 0.5, 1), B(0, 0.5, 0, 1, 1, 0.5), B(0.5, 0.5, 0.5, 1, 1, 1)];

    private static readonly Aabb[] StairOuterLeftN = [B(0, 0, 0, 1, 0.5, 1), B(0, 0.5, 0, 0.5, 1, 0.5)];
    private static readonly Aabb[] StairOuterRightN = [B(0, 0, 0, 1, 0.5, 1), B(0.5, 0.5, 0, 1, 1, 0.5)];

    private static Aabb[] Shape(string name) => name switch
    {
        "stone" => Stone,
        "oak_slab" => BottomSlab,
        "oak_slab_top" => TopSlab,
        "white_carpet" => Carpet,
        "dirt_path" => DirtPath,
        "farmland" => Farmland,
        "soul_sand" => SoulSand,
        "honey_block" => Honey,
        "lily_pad" => LilyPad,
        "snow1" => Snow(1),
        "snow2" => Snow(2),
        "snow3" => Snow(3),
        "snow4" => Snow(4),
        "snow5" => Snow(5),
        "snow6" => Snow(6),
        "snow7" => Snow(7),
        "snow8" => Snow(8),
        "stairs_bottom_north" => StairBottomNorth,
        "stairs_bottom_south" => StairBottomSouth,
        "stairs_bottom_west" => StairBottomWest,
        "stairs_bottom_east" => StairBottomEast,
        "stairs_top_north" => StairTopNorth,
        "stairs_top_south" => StairTopSouth,
        "stairs_top_west" => StairTopWest,
        "stairs_top_east" => StairTopEast,
        "stairs_bottom_inner_left_n" => StairInnerLeftN,
        "stairs_bottom_inner_right_n" => StairInnerRightN,
        "stairs_bottom_outer_left_n" => StairOuterLeftN,
        "stairs_bottom_outer_right_n" => StairOuterRightN,
        "air" => Nothing,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "no such fixture shape"),
    };

    /// <summary>For each of 29 shapes on a bare floor plane and each cardinal entry, records the support ladder and engine outcome. The source cell is air and the body enters at elevation zero.</summary>
    public static TheoryData<string, int, int, string, string> M7Table()
    {
        var data = new TheoryData<string, int, int, string, string>();

        void Flat(string shape, string ladder, string outcome)
        {
            data.Add(shape, 1, 0, ladder, outcome);
            data.Add(shape, -1, 0, ladder, outcome);
            data.Add(shape, 0, 1, ladder, outcome);
            data.Add(shape, 0, -1, ladder, outcome);
        }

        Flat("stone", "0,1", "BLOCKED");
        Flat("oak_slab", "0,0.5", "walk@0.5");
        Flat("oak_slab_top", "0,1", "BLOCKED");
        Flat("white_carpet", "0,0.0625", "walk@0.0625");
        Flat("dirt_path", "0,0.9375", "BLOCKED");
        Flat("farmland", "0,0.9375", "BLOCKED");
        Flat("soul_sand", "0,0.875", "BLOCKED");
        Flat("honey_block", "0,0.9375", "BLOCKED");
        Flat("lily_pad", "0,0.09375", "walk@0.09375");
        Flat("snow1", "0", "walk@0");
        Flat("snow2", "0,0.125", "walk@0.125");
        Flat("snow3", "0,0.25", "walk@0.25");
        Flat("snow4", "0,0.375", "walk@0.375");
        Flat("snow5", "0,0.5", "walk@0.5");
        Flat("snow6", "0,0.625", "BLOCKED");
        Flat("snow7", "0,0.75", "BLOCKED");
        Flat("snow8", "0,0.875", "BLOCKED");

        // An upside-down stair is a full block from below: standable, never walk-on-able.
        Flat("stairs_top_north", "0,1", "BLOCKED");
        Flat("stairs_top_south", "0,1", "BLOCKED");
        Flat("stairs_top_west", "0,1", "BLOCKED");
        Flat("stairs_top_east", "0,1", "BLOCKED");

        // Both inner corners raise a full edge band, so they block from all four cardinals.
        Flat("stairs_bottom_inner_left_n", "0,1", "BLOCKED");
        Flat("stairs_bottom_inner_right_n", "0,1", "BLOCKED");

        // A straight bottom stair walks from exactly one direction: the one its low shelf faces.
        void Directional(string shape, string dir1, string dir2 = "")
        {
            foreach ((string name, int dx, int dz) in Cardinals)
            {
                bool low = name == dir1 || name == dir2;
                data.Add(shape, dx, dz, low ? "0,0.5,1" : "0,1", low ? "walk@1" : "BLOCKED");
            }
        }

        Directional("stairs_bottom_north", "-Z");
        Directional("stairs_bottom_south", "+Z");
        Directional("stairs_bottom_west", "-X");
        Directional("stairs_bottom_east", "+X");

        // An outer corner raises one quarter, so it walks from the two cardinals that miss it.
        Directional("stairs_bottom_outer_left_n", "-X", "-Z");
        Directional("stairs_bottom_outer_right_n", "+X", "-Z");

        return data;
    }

    private static readonly (string Name, int Dx, int Dz)[] Cardinals =
        [("+X", 1, 0), ("-X", -1, 0), ("+Z", 0, 1), ("-Z", 0, -1)];

    [Theory]
    [MemberData(nameof(M7Table))]
    public void SweptLadder_AgreesWithTheEngine(string shape, int dx, int dz, string ladder, string outcome)
    {
        Span<double> levels = stackalloc double[BlockSupport.StepLadderSamples];
        BlockSupport.StepLadder(Shape(shape), Nothing, dx, dz, Half, levels, out int count);

        Assert.Equal(ladder, Join(levels[..count]));
        Assert.Equal(outcome, Outcome(levels[..count], from: 0.0));
    }

    // M1: rest elevation

    /// <summary>Where a body dropped on the shape settles (M1). The three rows <see cref="BlockSupport.FullCoverSupportHeight"/> answers <c>null</c> for are the point of this test: honey and lily pads are inset 1/16 on each side and a bottom stair tops out over half its footprint, and the physics stands on all three.</summary>
    [Theory]
    [InlineData("stone", 1.0)]
    [InlineData("oak_slab", 0.5)]
    [InlineData("oak_slab_top", 1.0)]
    [InlineData("white_carpet", 0.0625)]
    [InlineData("dirt_path", 0.9375)]
    [InlineData("soul_sand", 0.875)]
    [InlineData("honey_block", 0.9375)]      // FullCoverSupportHeight: null
    [InlineData("lily_pad", 0.09375)]        // FullCoverSupportHeight: null
    [InlineData("snow1", 0.0)]               // the empty shape: a pass-through, not a floor
    [InlineData("snow2", 0.125)]
    [InlineData("snow8", 0.875)]
    [InlineData("stairs_bottom_north", 1.0)] // FullCoverSupportHeight: null
    [InlineData("stairs_bottom_east", 1.0)]  // FullCoverSupportHeight: null
    [InlineData("stairs_top_north", 1.0)]
    [InlineData("stairs_bottom_outer_left_n", 1.0)]
    [InlineData("stairs_bottom_inner_right_n", 1.0)]
    public void RestElevation_IsWhereTheBodySettles(string shape, double expected)
        => Assert.Equal(expected, BlockSupport.FootprintSupportHeight(Shape(shape), 0.5, 0.5, Half));

    /// <summary>M1b: off-centre, the answer moves for an outer corner only, and it moves once the body's centre clears the raised quarter by its own half-width.</summary>
    [Theory]
    [InlineData(0.5, 1.0)]
    [InlineData(0.79, 1.0)]
    [InlineData(0.81, 0.5)]
    public void OuterCornerStair_DropsToTheShelfOnceTheFootprintLeavesTheQuarter(double cx, double expected)
        => Assert.Equal(expected, BlockSupport.FootprintSupportHeight(StairOuterLeftN, cx, 0.25, Half));

    /// <summary>Six source/destination pairs with nonzero source elevation. Each records a body sprinting from the source band to the destination band without jumping; all six are walked.</summary>
    public static TheoryData<string, string, double, string, string> R4Matrix()
    {
        var data = new TheoryData<string, string, double, string, string>();
        foreach ((string source, string destination, double elevation, string ladder, string engine) in R4Rows)
            data.Add(source, destination, elevation, ladder, engine);

        return data;
    }

    private static readonly (string Source, string Destination, double SourceElevation, string Ladder, string Engine)[] R4Rows =
    [
        ("snow8", "stone", 0.875, "0.875,1", "walk@1"),
        ("dirt_path", "stone", 0.9375, "0.9375,1", "walk@1"),
        ("oak_slab", "stone", 0.5, "0.5,1", "walk@1"),
        ("soul_sand", "stone", 0.875, "0.875,1", "walk@1"),
        ("snow5", "snow8", 0.5, "0.5,0.875", "walk@0.875"),
        ("oak_slab", "stairs_bottom_east", 0.5, "0.5,1", "walk@1"),
    ];

    [Theory]
    [MemberData(nameof(R4Matrix))]
    public void NonZeroSourceElevation_AgreesWithTheEngine(
        string source, string destination, double sourceElevation, string ladder, string engine)
    {
        Assert.Equal(sourceElevation, BlockSupport.FootprintSupportHeight(Shape(source), 0.5, 0.5, Half));

        Span<double> levels = stackalloc double[BlockSupport.StepLadderSamples];
        BlockSupport.StepLadder(Shape(destination), Shape(source), 1, 0, Half, levels, out int count);

        Assert.Equal(ladder, Join(levels[..count]));
        Assert.Equal(engine, Outcome(levels[..count], sourceElevation));
    }

    /// <summary>The ablation that earns the source cell its place, kept permanently. Sampling the DESTINATION cell alone - the form the support design specified, and the form all 116 M7 cases are blind to because every one of them starts from elevation 0 - agrees with the engine on exactly one of the same six rows. Its ladder opens at 0 where the body is standing at 0.875, so the next shelf reads as a full-block rise and the entry is refused.</summary>
    [Fact]
    public void TheSingleCellLadder_MissesFiveOfTheSixNonZeroSourceRows()
    {
        var agreed = new List<string>();
        var missed = new List<string>();
        double[] levels = new double[BlockSupport.StepLadderSamples];
        foreach ((string source, string destination, double sourceElevation, _, string engine) in R4Rows)
        {
            BlockSupport.StepLadder(Shape(destination), Nothing, 1, 0, Half, levels, out int count);
            string single = Outcome(levels.AsSpan(0, count), sourceElevation);
            (single == engine ? agreed : missed).Add($"{source}->{destination} {single} vs {engine}");
        }

        Assert.Equal(5, missed.Count);
        Assert.Equal("oak_slab->stairs_bottom_east walk@1 vs walk@1", Assert.Single(agreed));
    }

    // the furniture sweep

    /// <summary>The blocks admitted by footprint support, checked shape by shape. Every box is the literal version-774 shape named in the row; the <c>standable</c> column is <c>CanWalkOn</c>'s own arithmetic, <c>height is &gt; 0.0 and &lt;= 1.0</c>, and the reason says what that height IS in the block.</summary>
    /// <remarks>Cauldrons, composters, chests, hoppers, brewing stands, enchanting tables, and end portal frames all support a centred body. The sweep also pins the two exclusions: an empty collision shape and any shape reaching above its own cell.</remarks>
    public static TheoryData<string, double, bool, string> FurnitureSweep() => new()
    {
        { "cauldron", 0.25, true, "walls reach 1.0 at the edges; a centred body sits on the inner floor" },
        { "composter", 0.125, true, "the same concave shape: walls at the rim, a 1/8 floor inside" },
        { "chest", 0.875, true, "one box inset 1/16 a side, lid top 0.875, no full cover anywhere" },
        { "hopper", 0.6875, true, "the funnel collar; the 1.0 rim is at the cell edges, off the footprint" },
        { "brewing_stand", 0.875, true, "the central post overlaps a centred footprint at 0.875" },
        { "enchanting_table", 0.75, true, "a flat full-footprint 3/4 top; already standable before this" },
        { "end_portal_frame_eye", 1.0, true, "the eye is a centred 0.25..0.75 box topping the frame at 1.0" },
        { "end_portal_frame", 0.8125, true, "no eye: a flat full-footprint 13/16 top" },
        { "anvil", 1.0, true, "the neck and face run up the cell's middle to 1.0" },
        { "grindstone", 0.875, true, "the wheel housing spans the centre; this facing tops at 7/8" },
        { "campfire", 0.4375, true, "a flat full-footprint 7/16 slab, and under the 0.6 auto-step" },
        { "honey_block", 0.9375, true, "inset 1/16 a side, which is why full cover refuses it" },
        { "lily_pad", 0.09375, true, "the same inset at 3/32; a pad is a floor and the body stays dry" },
        { "stairs_bottom_north", 1.0, true, "the raised octet always overlaps a centred footprint" },
        { "snow1", 0.0, false, "shape 0, the EMPTY shape: a pass-through, nothing to stand on" },
        { "fence_gate_open", 0.0, false, "an open gate is Shapes.empty(): the same pass-through" },
        { "fence_post", 1.5, false, "the post runs to 1.5, half a block into the cell above" },
        { "fence_gate_closed", 1.5, false, "a closed gate is a 1.5 panel; defect #11's family" },
    };

    [Theory]
    [MemberData(nameof(FurnitureSweep))]
    public void FurnitureShapes_OfferWhatTheirBoxesSay(string shape, double height, bool standable, string why)
    {
        double actual = BlockSupport.FootprintSupportHeight(Furniture(shape), 0.5, 0.5, Half);

        Assert.Equal(height, actual);
        Assert.Equal(standable, actual is > 0.0 and <= 1.0);
        Assert.False(string.IsNullOrEmpty(why));
    }

    /// <summary>Independent protocol-774 furniture boxes, with the shape id noted per row.</summary>
    private static Aabb[] Furniture(string name) => name switch
    {
        // shape 144: eight corner posts and two side walls to 1.0, over a 3/16-to-1/4 inner floor.
        "cauldron" =>
        [
            B(0, 0, 0, 0.125, 1, 0.25),
            B(0, 0, 0.75, 0.125, 1, 1),
            B(0.125, 0, 0, 0.25, 1, 0.125),
            B(0.125, 0, 0.875, 0.25, 1, 1),
            B(0.75, 0, 0, 1, 1, 0.125),
            B(0.75, 0, 0.875, 1, 1, 1),
            B(0.875, 0, 0.125, 1, 1, 0.25),
            B(0.875, 0, 0.75, 1, 1, 0.875),
            B(0, 0.1875, 0.25, 1, 0.25, 0.75),
            B(0.125, 0.1875, 0.125, 0.875, 0.25, 0.25),
            B(0.125, 0.1875, 0.75, 0.875, 0.25, 0.875),
            B(0.25, 0.1875, 0, 0.75, 1, 0.125),
            B(0.25, 0.1875, 0.875, 0.75, 1, 1),
            B(0, 0.25, 0.25, 0.125, 1, 0.75),
            B(0.875, 0.25, 0.25, 1, 1, 0.75),
        ],

        // shape 227.
        "composter" =>
        [
            B(0, 0, 0, 1, 0.125, 1),
            B(0, 0.125, 0, 0.125, 1, 1),
            B(0.125, 0.125, 0, 1, 1, 0.125),
            B(0.125, 0.125, 0.875, 1, 1, 1),
            B(0.875, 0.125, 0.125, 1, 1, 0.875),
        ],

        "chest" => [B(0.0625, 0, 0.0625, 0.9375, 0.875, 0.9375)],                       // shape 145

        // shape 259 (facing=down).
        "hopper" =>
        [
            B(0.375, 0, 0.375, 0.625, 0.6875, 0.625),
            B(0.25, 0.25, 0.25, 0.375, 0.6875, 0.75),
            B(0.375, 0.25, 0.25, 0.75, 0.6875, 0.375),
            B(0.375, 0.25, 0.625, 0.75, 0.6875, 0.75),
            B(0.625, 0.25, 0.375, 0.75, 0.6875, 0.625),
            B(0, 0.625, 0, 0.25, 0.6875, 1),
            B(0.25, 0.625, 0, 1, 0.6875, 0.25),
            B(0.25, 0.625, 0.75, 1, 0.6875, 1),
            B(0.75, 0.625, 0.25, 1, 0.6875, 0.75),
            B(0, 0.6875, 0, 0.125, 1, 1),
            B(0.125, 0.6875, 0, 1, 1, 0.125),
            B(0.125, 0.6875, 0.875, 1, 1, 1),
            B(0.875, 0.6875, 0.125, 1, 1, 0.875),
        ],

        // shape 134: a 1/8 base plate and the central post.
        "brewing_stand" => [B(0.0625, 0, 0.0625, 0.9375, 0.125, 0.9375), B(0.4375, 0.125, 0.4375, 0.5625, 0.875, 0.5625)],
        "enchanting_table" => [B(0, 0, 0, 1, 0.75, 1)],                                 // shape 244
        "end_portal_frame_eye" => [B(0, 0, 0, 1, 0.8125, 1), B(0.25, 0.8125, 0.25, 0.75, 1, 0.75)], // shape 245
        "end_portal_frame" => [B(0, 0, 0, 1, 0.8125, 1)],                               // shape 246

        // shape 93.
        "anvil" =>
        [
            B(0.125, 0, 0.125, 0.875, 0.25, 0.875),
            B(0.25, 0.25, 0.1875, 0.75, 0.3125, 0.8125),
            B(0.375, 0.3125, 0.25, 0.625, 1, 0.75),
            B(0.1875, 0.625, 0, 0.375, 1, 1),
            B(0.375, 0.625, 0, 0.8125, 1, 0.25),
            B(0.375, 0.625, 0.75, 0.8125, 1, 1),
            B(0.625, 0.625, 0.25, 0.8125, 1, 0.75),
        ],

        // shape 253 (wall-mounted, face=wall facing=north).
        "grindstone" =>
        [
            B(0.25, 0.125, 0, 0.75, 0.875, 0.75),
            B(0.125, 0.3125, 0.1875, 0.25, 0.6875, 0.5625),
            B(0.75, 0.3125, 0.1875, 0.875, 0.6875, 0.5625),
            B(0.125, 0.375, 0.5625, 0.25, 0.625, 1),
            B(0.75, 0.375, 0.5625, 0.875, 0.625, 1),
        ],

        "campfire" => [B(0, 0, 0, 1, 0.4375, 1)],                                       // shape 143
        "fence_gate_open" => Nothing,                                                    // shape 0
        "fence_gate_closed" => [B(0, 0, 0.375, 1, 1.5, 0.625)],                          // shape 11
        "fence_post" => [B(0.375, 0, 0.375, 0.625, 1.5, 0.625)],                         // shape 20
        _ => Shape(name),
    };

    /// <summary>The step is one cardinal or it is nothing; a diagonal has no single entry face.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 0)]
    public void StepLadder_RefusesAnythingButOneCardinal(int dx, int dz)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            double[] levels = new double[BlockSupport.StepLadderSamples];
            BlockSupport.StepLadder(Stone, Nothing, dx, dz, Half, levels, out _);
        });
    }

    [Fact]
    public void StepLadder_RefusesAShortBuffer()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            double[] levels = new double[BlockSupport.StepLadderSamples - 1];
            BlockSupport.StepLadder(Stone, Nothing, 1, 0, Half, levels, out _);
        });
    }

    private static string Outcome(ReadOnlySpan<double> levels, double from)
        => BlockSupport.TryStepUp(levels, from, BlockSupport.PlayerStepHeight, out double rest)
            ? "walk@" + F(rest)
            : "BLOCKED";

    private static string Join(ReadOnlySpan<double> levels)
    {
        var parts = new List<string>(levels.Length);
        foreach (double level in levels)
            parts.Add(F(level));

        return string.Join(",", parts);
    }

    private static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
}
