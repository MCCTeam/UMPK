using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Xunit;

namespace Umpk.Pathfinding.Tests.Moves;

public sealed class WadeCurrentCostTests
{
    private const int FloorY = 63;
    private const int FeetY = 64;
    private const int LaneZ = 4;

    /// <summary>A walled one-deep lane whose water runs +x over x=0..12, so a body standing in it has its feet in water and its head in air. Moving +x is DOWNSTREAM and -x is UPSTREAM.</summary>
    private static FixtureWorld Lane(bool wet = true)
    {
        var world = new FixtureWorld();
        world.Fill(-4, 56, LaneZ - 3, 20, 72, LaneZ + 3, FixtureWorld.Stone);
        world.Fill(-3, FeetY, LaneZ - 2, 19, 71, LaneZ + 2, FixtureWorld.Air);
        world.Floor(-3, 19, LaneZ - 2, LaneZ + 2, FloorY);
        if (wet)
            world.FlowingRun(0, FeetY, LaneZ, 12, 1, 0, layers: 1);

        return world;
    }

    /// <summary>A one-deep sheet wide enough in z that a move ACROSS it is a real crossing and not a wall hug.</summary>
    private static FixtureWorld Sheet()
    {
        var world = new FixtureWorld();
        world.Fill(-4, 56, -4, 20, 72, 20, FixtureWorld.Stone);
        world.Fill(-3, FeetY, -3, 19, 71, 19, FixtureWorld.Air);
        world.Floor(-3, 19, -3, 19, FloorY);
        for (int z = 0; z <= 16; z++)
            world.FlowingRun(0, FeetY, z, 12, 1, 0, layers: 1);

        return world;
    }

    /// <summary>T1. The same walk, both ways down a lane, is not the same walk.</summary>
    [Fact]
    public void UpstreamWade_CostsMoreThanDownstream()
    {
        CalculationContext ctx = FixtureContext.Around(Lane(), 6, FeetY, LaneZ);

        double downstream = Traverse(ctx, 6, FeetY, LaneZ, 1, 0);
        double upstream = Traverse(ctx, 6, FeetY, LaneZ, -1, 0);

        Assert.True(
            upstream > downstream * 2.0,
            $"upstream {upstream:F4} against downstream {downstream:F4}: the planner cannot tell the "
            + "two apart");
    }

    /// <summary>T2. A wade against a current is not the same thing as the same walk on dry land.</summary>
    [Fact]
    public void AWadeAgainstACurrent_CostsMoreThanTheSameDryWalk()
    {
        CalculationContext wet = FixtureContext.Around(Lane(), 6, FeetY, LaneZ);
        CalculationContext dry = FixtureContext.Around(Lane(wet: false), 6, FeetY, LaneZ);

        double upstream = Traverse(wet, 6, FeetY, LaneZ, -1, 0);
        double control = Traverse(dry, 6, FeetY, LaneZ, -1, 0);

        Assert.True(
            upstream > control * 2.0,
            $"wet {upstream:F4} against dry {control:F4}, ratio {upstream / control:F4}");
    }

    /// <summary>T3, a GUARD that passes before and after. A downstream wade is never cheaper than the same walk in still water.</summary>
    /// <remarks>Without this, the next person "completes" the model with the discount its own measurement justifies (0.6533 dead downstream) and silently breaks A*. <c>SprintOneBlock</c> IS the heuristic's per-block rate, so a wade edge under 1.0x is an inadmissible edge.</remarks>
    [Fact]
    public void ADownstreamWade_IsNeverCheaperThanStillWater()
    {
        CalculationContext wet = FixtureContext.Around(Lane(), 6, FeetY, LaneZ);
        CalculationContext dry = FixtureContext.Around(Lane(wet: false), 6, FeetY, LaneZ);

        double downstream = Traverse(wet, 6, FeetY, LaneZ, 1, 0);
        double control = Traverse(dry, 6, FeetY, LaneZ, 1, 0);

        Assert.Equal(control, downstream, 12);
    }

    /// <summary>T3 again, one level down and exhaustively: the multiplier itself is never under 1.0, for any flow direction against any move direction.</summary>
    /// <remarks>Swept rather than sampled because the bound is what the admissibility proof rests on, and the Diagonal family has EXACTLY zero margin: <c>SprintOneBlock * DiagonalMultiplier</c> equals <c>h(1,0,1)</c> to the last bit, so one ULP under 1.0 breaks the first diagonal it touches.</remarks>
    [Fact]
    public void TheWadeMultiplierIsNeverBelowOne()
    {
        for (int flowDeg = 0; flowDeg < 360; flowDeg += 3)
        {
            double fr = flowDeg * Math.PI / 180.0;
            var flow = new Vec3d(Math.Cos(fr), 0.0, Math.Sin(fr));
            for (int moveDeg = 0; moveDeg < 360; moveDeg += 3)
            {
                double mr = moveDeg * Math.PI / 180.0;
                var move = new Vec3d(Math.Cos(mr), 0.0, Math.Sin(mr));
                double multiplier = ActionCosts.WadeCurrentCostMultiplier(flow, move);

                Assert.True(
                    multiplier >= 1.0,
                    $"flow {flowDeg} deg against move {moveDeg} deg was priced {multiplier:R}, under 1.0");
                Assert.True(
                    multiplier <= ActionCosts.WadeMaxCurrentCostMultiplier,
                    $"flow {flowDeg} deg against move {moveDeg} deg was priced {multiplier:R}, over the bound");
            }
        }
    }

    /// <summary>T5. A 90-degree crossing wade costs exactly 1.0, and this is not an oversight.</summary>
    /// <remarks>The swim model charges a crossing 1.4289 because a SWIMMER holds a crab angle and spends thrust cancelling the cross-track push. The wade executor was measured holding yaw 0 and covering 0.1526 blocks a tick across a sheet, exactly its still-water rate, so charging it the crab's cost would invent a cost the engine does not charge. This pins the wade against a copy-paste of the swim model, which <c>FromScaledComponents</c> makes one keystroke away.</remarks>
    [Fact]
    public void A90DegreeCrossingWade_IsNotChargedForTime()
    {
        Assert.Equal(
            1.0, ActionCosts.WadeCurrentCostMultiplier(new Vec3d(0, 0, 1), new Vec3d(1, 0, 0)), 12);

        CalculationContext ctx = FixtureContext.Around(Sheet(), 6, FeetY, 8);
        double across = Traverse(ctx, 6, FeetY, 8, 0, 1);

        Assert.Equal(ActionCosts.SprintOneBlock, across, 12);
    }

    [Theory]
    [InlineData(0, 1.0)]           // dead downstream, raw 0.5772, CLAMPED
    [InlineData(45, 1.0)]          // oblique downstream, raw 0.6580, CLAMPED
    [InlineData(90, 1.0)]          // a crossing costs nothing in time
    [InlineData(135, 2.0742)]      // measured 2.0741
    [InlineData(180, 3.7369)]      // measured 3.7371
    public void TheModelReproducesTheMeasuredCurve(int degreesBetweenFlowAndMove, double expected)
    {
        double radians = degreesBetweenFlowAndMove * Math.PI / 180.0;
        var flow = new Vec3d(Math.Cos(radians), 0.0, Math.Sin(radians));

        double actual = ActionCosts.WadeCurrentCostMultiplier(flow, new Vec3d(1, 0, 0));

        Assert.Equal(expected, actual, 4);
    }

    /// <summary>A dry cell is byte-identical, and it costs no flow sample to find that out. This is the argument that makes the 97 dry rows of the course untouchable by this pricing, so it is asserted and not merely stated.</summary>
    [Fact]
    public void ADryWalkIsByteIdenticalAndTakesNoFlowSample()
    {
        CalculationContext dry = FixtureContext.Around(Lane(wet: false), 6, FeetY, LaneZ);

        double cost = dry.WadeCostThrough(6, FeetY, LaneZ, -1, 0, 0);

        Assert.Equal(dry.SprintCost, cost);
        Assert.Equal(0, dry.FlowSamplesTaken);
    }

    [Theory]
    [InlineData(1, 0, 0)]     // walk out of the column
    [InlineData(1, 1, 0)]     // step UP out of it: the case that was charged 2.0742
    [InlineData(1, -1, 0)]    // step down out of it
    [InlineData(0, 0, 1)]     // walk out the other way
    public void AVerticalCurrentDoesNotPriceAWade(int stepX, int stepY, int stepZ)
    {
        double multiplier = ActionCosts.WadeCurrentCostMultiplier(
            new Vec3d(0, -1, 0), new Vec3d(stepX, stepY, stepZ));

        Assert.Equal(1.0, multiplier, 12);
    }

    /// <summary>The same claim through the move expander over a plunge basin, verifying the complete pricing path rather than only the multiplier function.</summary>
    [Fact]
    public void AStepOutOfAPlungeBasinIsNotChargedForTheFallingColumn()
    {
        var world = new FixtureWorld();
        world.Fill(-4, 50, -4, 12, 80, 4, FixtureWorld.Stone);
        // A 1x1 shaft with a falling column in it, its floor at y=59, and a ledge one step up at x=2 that the body walks out onto.
        world.Fill(0, 60, 0, 0, 78, 0, FixtureWorld.Air);
        world.Fill(1, 60, 0, 1, 62, 0, FixtureWorld.Air);
        world.Fill(2, 61, 0, 8, 63, 0, FixtureWorld.Air);
        world.Floor(2, 8, 0, 0, 60);
        world.FallingColumn(0, 60, 0, height: 18);

        CalculationContext ctx = FixtureContext.Build(
            world, new BlockPos(0, 60, 0), new BlockPos(8, 61, 0), margin: 6);

        Assert.NotEqual(Vec3d.Zero, ctx.WaterFlowAt(0, 60, 0));
        Assert.Equal(ctx.SprintCost, ctx.WadeCostThrough(0, 60, 0, 1, 1, 0), 12);
        Assert.Equal(ctx.SprintCost, ctx.WadeCostThrough(0, 60, 0, 1, 0, 0), 12);
    }

    /// <summary>T12. Depth strider makes a current cheaper, one case per level, against the measured table.</summary>
    /// <remarks>The levels are asserted as the measured upstream multipliers rather than re-derived from the ratios the implementation uses, so the table cannot be "simplified" into a formula that happens to agree at the ends. A first-principles <c>k(e) = k0 (1 - e)</c> would give 1.9469 at level I where the engine measures 1.3571.</remarks>
    [Theory]
    [InlineData(0, 3.7371)]
    [InlineData(1, 1.3571)]
    [InlineData(2, 1.1324)]
    [InlineData(3, 1.0870)]
    [InlineData(5, 1.0870)] // vanilla re-caps its own enchantment contribution at 3.
    public void DepthStriderMakesACurrentCheaper(int level, double expectedUpstreamMultiplier)
    {
        double actual = ActionCosts.WadeCurrentCostMultiplier(
            new Vec3d(1, 0, 0), new Vec3d(-1, 0, 0), level);

        Assert.Equal(expectedUpstreamMultiplier, actual, 3);
    }

    /// <summary>Every level stays on the admissible side of the bound, so the capability cannot introduce the discount the bare model is clamped against.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NoDepthStriderLevelCanMakeAWadeCheaperThanStillWater(int level)
    {
        for (int flowDeg = 0; flowDeg < 360; flowDeg += 5)
        {
            double fr = flowDeg * Math.PI / 180.0;
            var flow = new Vec3d(Math.Cos(fr), 0.0, Math.Sin(fr));
            for (int moveDeg = 0; moveDeg < 360; moveDeg += 5)
            {
                double mr = moveDeg * Math.PI / 180.0;
                double multiplier = ActionCosts.WadeCurrentCostMultiplier(
                    flow, new Vec3d(Math.Cos(mr), 0.0, Math.Sin(mr)), level);

                Assert.True(
                    multiplier >= 1.0,
                    $"DS {level}, flow {flowDeg} deg against move {moveDeg} deg: {multiplier:R} is under 1.0");
            }
        }
    }

    /// <summary>The captured capability reaches the cost calculation and reduces the wading price.</summary>
    [Fact]
    public void TheCaptureIsWhatMakesTheWadeCheap()
    {
        FixtureWorld world = Lane();
        CalculationContext bare = FixtureContext.Around(world, 6, FeetY, LaneZ);
        CalculationContext booted = FixtureContext.Around(
            world, 6, FeetY, LaneZ, capabilities: CapabilitiesWithDepthStrider(3));

        Assert.Equal(0, bare.Capabilities.DepthStriderLevel);
        Assert.Equal(3, booted.Capabilities.DepthStriderLevel);

        double bareUpstream = Traverse(bare, 6, FeetY, LaneZ, -1, 0);
        double bootedUpstream = Traverse(booted, 6, FeetY, LaneZ, -1, 0);

        Assert.True(
            bootedUpstream < bareUpstream / 3.0,
            $"booted {bootedUpstream:F4} against bare {bareUpstream:F4}: the capability did not reach "
            + "the cost");
        Assert.True(bootedUpstream >= ActionCosts.SprintOneBlock, "a booted wade became a bargain");
    }

    /// <summary>A capture wearing real component-era boots carrying a real depth-strider instance, built the way a 766+ wire decode leaves one. Nothing here stands in for the readout: this drives the same <see cref="EnchantmentReadout"/> the live capture builds.</summary>
    private static PathfinderCapabilities CapabilitiesWithDepthStrider(int level)
    {
        Registry<ItemDefinition> items = new RegistryBuilder<ItemDefinition>(RegistryIds.Item)
            .Add(1, Identifier.Minecraft("diamond_boots"), new ItemDefinition(1))
            .Build();
        Registry<EnchantmentDefinition> enchants =
            new RegistryBuilder<EnchantmentDefinition>(RegistryIds.Enchantment)
                .Add(1, Identifier.Minecraft("depth_strider"), new EnchantmentDefinition(MaxLevel: 3))
                .Build();
        Assert.True(items.TryGet(1, out RegistryEntry<ItemDefinition> boots));
        Assert.True(
            enchants.TryGet(Identifier.Minecraft("depth_strider"), out RegistryEntry<EnchantmentDefinition> ds));

        ItemStack stack = new ItemStack(boots, 1).With(
            DataComponents.Enchantments, new EnchantmentsComponent([new EnchantmentInstance(ds, level)]));

        return new PathfinderCapabilities
        {
            EffectsKnown = false,
            InventoryKnown = true,
            VitalsKnown = false,
            Items =
            [
                new CapabilityItem(
                    Identifier.Minecraft("diamond_boots"),
                    Count: 1,
                    MenuSlot: 8,
                    Enchantments: new EnchantmentReadout(
                        stack, "V1_8", NoLegacyIds.Instance, sessionCanNameEnchantments: true)),
            ],
        };
    }

    /// <summary>A bridge source with no legacy table at all: this suite only builds component-era stacks, and a source that answered anything would be pretending the legacy path was under test.</summary>
    private sealed class NoLegacyIds : ILegacyItemBridgeSource
    {
        public static readonly NoLegacyIds Instance = new();

        public bool TryMapNbtKey(string era, string nbtKey, out Identifier componentId)
        {
            componentId = default;
            return false;
        }

        public bool TryMapEnchantmentId(string era, int numericId, out Identifier enchantmentId)
        {
            enchantmentId = default;
            return false;
        }
    }

    /// <summary>The cost of the level walk out of a cell along a step, read off the real expander.</summary>
    private static double Traverse(CalculationContext ctx, int x, int y, int z, int dx, int dz)
    {
        var expander = new JumpExpander();
        Span<MoveNeighbor> buffer = new MoveNeighbor[expander.MaxNeighbors];
        int count = expander.Expand(ctx, x, y, z, buffer);

        for (int i = 0; i < count; i++)
        {
            MoveNeighbor neighbor = buffer[i];
            if (neighbor.MoveType == MoveType.Traverse
                && neighbor.DestX == x + dx
                && neighbor.DestY == y
                && neighbor.DestZ == z + dz)
                return neighbor.Cost;

        }

        Assert.Fail($"no Traverse to ({x + dx}, {y}, {z + dz}) was emitted at all.");
        return 0.0;
    }
}
