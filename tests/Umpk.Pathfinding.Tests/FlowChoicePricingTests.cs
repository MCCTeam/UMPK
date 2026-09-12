using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Pathfinding.Tests;

public sealed class FlowChoicePricingTests
{
    private readonly ITestOutputHelper _output;

    public FlowChoicePricingTests(ITestOutputHelper output) => _output = output;

    /// <summary>The horizontal-current multiplier used by E19's counterfactual comparison.</summary>
    private const double TheHorizontalMultiplierTheRowWasBuiltOn = 3.5;

    /// <summary>Shaft A's falling column: eight cells, y=91 to y=98.</summary>
    private const int ShaftAFallingCells = 8;

    /// <summary>The measured vertical price, stated as a literal rather than re-derived from the constant it is checking. The value 1.1274 agrees within 0.3 percent with the measured 27 ticks over eight blocks versus 24 ticks in still water, a ratio of 1.125.</summary>
    [Fact]
    public void AFallingColumn_CostsAboutAnEighthMoreToClimbAndAtenthLessToRide()
    {
        var down = new Vec3d(0, -1, 0);

        double climb = ActionCosts.CurrentCostMultiplier(down, new Vec3d(0, 1, 0));
        double ride = ActionCosts.CurrentCostMultiplier(down, down);

        Assert.Equal(1.1274, climb, 4);
        Assert.Equal(0.8985, ride, 4);

        // And it is nowhere near the horizontal row the same flow would be charged if the axis were ignored, which is the number E19's build note is written around.
        Assert.True(
            climb < TheHorizontalMultiplierTheRowWasBuiltOn / 3.0,
            $"a vertical climb is priced x{climb:F4}, not far enough under the horizontal "
            + $"x{TheHorizontalMultiplierTheRowWasBuiltOn:F1}");
    }

    /// <summary>The snapshot really does carry the settle. A falling cell mid-column reads as water, keeps its <c>level=8</c> state, and the engine's own flow sampler answers a straight <c>(0, -1, 0)</c> through the planning view - so the planner is pricing a current it can actually see.</summary>
    [Fact]
    public void ThePlanningView_CarriesTheFallingColumnsFlow()
    {
        PlanningWorldView view = Capture(BuildE19(settled: true));

        BlockState midA = view.GetBlock(new BlockPos(390, 95, 770));
        Vec3d flowA = PlayerPhysics.GetWaterFlow(view, new BlockPos(390, 95, 770));
        Vec3d flowB = PlayerPhysics.GetWaterFlow(view, new BlockPos(402, 95, 773));

        Assert.True(MoveHelper.IsWater(midA), "shaft A's mid-column cell is not water in the snapshot");
        Assert.Equal(0.0, flowA.X, 6);
        Assert.Equal(-1.0, flowA.Y, 6);
        Assert.Equal(0.0, flowA.Z, 6);

        // Shaft B is sources all the way, so its flow is exactly zero and the two shafts really are distinguishable by the model.
        Assert.Equal(0.0, flowB.Y, 6);
    }

    /// <summary>The route comparison itself, and the crossover. The planner prefers the falling column, and it would have preferred the still shaft at the horizontal price - so the row's expectation and the measured price cannot both stand.</summary>
    [Fact]
    public void E19_PrefersTheFallingColumn_AndWouldNotHaveAtTheHorizontalPrice()
    {
        PathResult viaA = Plan(BuildE19(settled: true));
        PathResult viaB = Plan(BuildE19(settled: true, sealShaftA: true));

        Assert.Equal(PathStatus.Success, viaA.Status);
        Assert.Equal(PathStatus.Success, viaB.Status);
        Assert.True(ClimbsShaftA(viaA), "the route through the open world did not use shaft A");
        Assert.False(ClimbsShaftA(viaB), "the sealed-A world still routed through shaft A");

        // Compare the falling-cell price with the horizontal-current counterfactual.
        double surchargeNow =
            ShaftAFallingCells * ActionCosts.SwimOneBlock
            * (ActionCosts.CurrentCostMultiplier(new Vec3d(0, -1, 0), new Vec3d(0, 1, 0)) - 1.0);
        double surchargeThen =
            ShaftAFallingCells * ActionCosts.SwimOneBlock * (TheHorizontalMultiplierTheRowWasBuiltOn - 1.0);
        double detour = viaB.Cost - (viaA.Cost - surchargeNow);

        _output.WriteLine(
            $"A={viaA.Cost:F2} B={viaB.Cost:F2} surchargeNow={surchargeNow:F2} "
            + $"surchargeThen={surchargeThen:F2} detour={detour:F2}");

        Assert.True(
            viaA.Cost < viaB.Cost,
            $"the falling column costs {viaA.Cost:F2} and the still shaft {viaB.Cost:F2}");
        Assert.True(
            viaA.Cost - surchargeNow + surchargeThen > viaB.Cost,
            $"at the horizontal x{TheHorizontalMultiplierTheRowWasBuiltOn:F1} the falling column would "
            + $"cost {viaA.Cost - surchargeNow + surchargeThen:F2} against the still shaft's "
            + $"{viaB.Cost:F2}, so the row would still have chosen A and its premise never held");

        // The row is not merely losing, it is unreachable. For the still shaft to win, the climb's surcharge would have to exceed the detour, and at the measured 0.1274 per cell that needs a column this deep - which is far past the depth at which the breath validator refuses the row outright (E19's own build note puts that at sixteen cells).
        double cellsNeeded = detour
            / (ActionCosts.SwimOneBlock
               * (ActionCosts.CurrentCostMultiplier(new Vec3d(0, -1, 0), new Vec3d(0, 1, 0)) - 1.0));
        _output.WriteLine($"cells the column would need for the still shaft to win: {cellsNeeded:F1}");
        Assert.True(
            cellsNeeded > 16,
            $"the still shaft would win at a column of {cellsNeeded:F1} cells, which is inside the depth "
            + "E19 can afford, so the row is still discriminating and this finding is wrong");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ADepthStriderCapture_DoesNotChangeTheVerticalClimbPrice(int level)
    {
        PathfinderCapabilities booted = DepthStriderBoots(level);
        Assert.Equal(level, booted.DepthStriderLevel);

        // The price of one falling cell, climbed, straight off the swim model.
        var down = new Vec3d(0, -1, 0);
        var up = new Vec3d(0, 1, 0);
        double bareCell = ActionCosts.SwimOneBlock * ActionCosts.CurrentCostMultiplier(down, up);

        // Verify the same price through a context that consumes the captured capability.
        FixtureWorld world = BuildE19(settled: true);
        var bareCtx = new CalculationContext(Capture(world), PathfinderOptions.Default);
        var bootedCtx = new CalculationContext(Capture(world), PathfinderOptions.Default, booted);

        double bare = bareCtx.SwimCostThrough(390, 94, 770, 0, 1, 0);
        double geared = bootedCtx.SwimCostThrough(390, 94, 770, 0, 1, 0);

        Assert.Equal(bareCell, bare, 12);
        Assert.Equal(bare, geared, 12);

        // And the route the row discriminates on does not move either.
        PathResult viaA = PlanWith(BuildE19(settled: true), booted);
        Assert.Equal(PathStatus.Success, viaA.Status);
        Assert.True(ClimbsShaftA(viaA), "a booted capture stopped routing through shaft A");
    }

    /// <summary>A capture wearing real component-era boots carrying a real depth-strider instance, so the level reaches the context the way a live capture's would.</summary>
    private static PathfinderCapabilities DepthStriderBoots(int level)
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

    /// <summary>A bridge source with no legacy table: this suite builds only component-era stacks.</summary>
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

    private static PathResult PlanWith(FixtureWorld world, PathfinderCapabilities capabilities)
        => PathPlanner.FindPath(
            Capture(world),
            PathfinderOptions.Default,
            new BlockPos(389, 89, 773),
            new Goals.GoalBlock(new BlockPos(392, 100, 773)),
            capabilities: capabilities);

    private static bool ClimbsShaftA(PathResult result)
    {
        foreach (PathNode node in result.Path)
            if (node.X == 390 && node.Z == 770 && node.Y > 90)
                return true;

        return false;
    }

    private static PathResult Plan(FixtureWorld world)
        => PathPlanner.FindPath(
            Capture(world),
            PathfinderOptions.Default,
            new BlockPos(389, 89, 773),
            new Goals.GoalBlock(new BlockPos(392, 100, 773)));

    private static PlanningWorldView Capture(FixtureWorld world)
        => world.Capture(new BlockPos(380, 80, 764), new BlockPos(415, 122, 782), margin: 4);

    /// <summary>
    /// E19's build, at its absolute coordinates. <c>Plot(6, 12)</c> anchors at <c>ox=384, oz=768</c>, and the generator's <c>fill</c> takes <c>y = 99 + dy</c>:
    /// <code>
    /// p.fill(0, -15, 0, 27, 4, 10, "stone")   the mass,           x 384..411, y  84..103, z 768..778 p.fill(4, -10, 5, 20, -9, 5, "water")   the bottom lane,    x 388..404, y  89..90,  z 773 p.fill(6, -10, 2, 6, -9, 4, "water")    the spur to A,      x 390,      y  89..90,  z 770..772 p.fill(6, -8, 2, 6, -1, 2, "air")       shaft A's column,   x 390,      y  91..98,  z 770 p.setblock(6, 0, 2, "water")            shaft A's source,   x 390,      y  99,      z 770 p.fill(18, -8, 5, 18, 0, 5, "water")    shaft B,            x 402,      y  91..99,  z 773 p.fill(4, 1, 5, 20, 2, 5, "air")        the top corridor,   x 388..404, y 100..101, z 773 p.fill(6, 1, 2, 6, 2, 4, "air")         the top spur off A, x 390,      y 100..101, z 770..772
    /// </code>
    /// The settle is what the source at y=99 does to the empty column under it: vanilla turns every cell below into <c>water[level=8]</c>, the falling state, and the fall stops on the lane's own source water at y=90.
    /// </summary>
    private static FixtureWorld BuildE19(bool settled, bool sealShaftA = false)
    {
        var world = new FixtureWorld();
        world.Fill(380, 80, 764, 415, 122, 782, FixtureWorld.Air);
        world.Fill(384, 84, 768, 411, 103, 778, FixtureWorld.Stone);
        world.Fill(388, 89, 773, 404, 90, 773, FixtureWorld.Water);
        world.Fill(390, 89, 770, 390, 90, 772, FixtureWorld.Water);
        world.Fill(390, 91, 770, 390, 98, 770, FixtureWorld.Air);
        world.Set(390, 99, 770, FixtureWorld.Water);
        world.Fill(402, 91, 773, 402, 99, 773, FixtureWorld.Water);
        world.Fill(388, 100, 773, 404, 101, 773, FixtureWorld.Air);
        world.Fill(390, 100, 770, 390, 101, 772, FixtureWorld.Air);

        if (settled)
            world.FallingColumn(390, 91, 770, ShaftAFallingCells);

        if (sealShaftA)
        {
            // The counterfactual: the same world with A's column filled in, so the search has to price the still shaft and the two totals are comparable.
            world.Fill(390, 91, 770, 390, 99, 770, FixtureWorld.Stone);
        }

        return world;
    }
}
