using Umpk.Game.Blocks;
using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Xunit;

namespace Umpk.Pathfinding.Tests;

/// <summary>With leather boots on, powder snow stops being a hazard and becomes a full solid cube to the planner: standable, and never enterable. Without them it stays exactly the hazard course row F7 pins.</summary>
/// <remarks>
/// <para><b>Why "a solid cube" is the right framing and "not a hazard" is not.</b> A booted body above the cell receives a full-cube collision shape, while a body inside the cell receives an empty shape. The plan may therefore stand on the cell but may never occupy it. Clearing the hazard alone gets the first half and inverts the second: powder snow carries no <c>BlocksMotion</c> flag, so <see cref="MoveHelper.CanWalkThrough"/> would otherwise treat its empty shape as passable.</para>
/// <para><b>Freezing does not need an arm.</b> Any leather wearable prevents freezing. Independently, a body standing on top is never inside powder snow because collision checks use a slightly shrunken body box. Both halves of the gate fail independently, so the life-safety supervisor is unchanged.</para>
/// </remarks>
public sealed class PowderSnowWalkabilityTests
{
    private const int FloorY = 99;

    private const int BodyY = 100;

    private static readonly Identifier LeatherBoots = Identifier.Minecraft("leather_boots");

    private static readonly Identifier NetheriteBoots = Identifier.Minecraft("netherite_boots");

    private static readonly BlockPos LaneStart = new(1, BodyY, 3);

    private static readonly BlockPos LaneGoal = new(12, BodyY, 3);

    /// <summary>The first X of the powder-snow section.</summary>
    private const int SnowStartX = 4;

    /// <summary>The last X of the powder-snow section.</summary>
    private const int SnowEndX = 9;

    private static FixtureWorld PowderSnowCorridor()
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 3, 13, FloorY, 3, FixtureWorld.Stone);
        world.Fill(SnowStartX, FloorY - 1, 3, SnowEndX, FloorY - 1, 3, FixtureWorld.Stone);
        world.Fill(SnowStartX, FloorY, 3, SnowEndX, FloorY, 3, FixtureWorld.PowderSnow);
        return world;
    }

    /// <summary>A capture that names leather boots in the feet slot, i.e. menu slot 8.</summary>
    private static PathfinderCapabilities Booted(Identifier? boots) => new()
    {
        EffectsKnown = false,
        InventoryKnown = true,
        VitalsKnown = false,
        Items = boots is null
            ? []
            : [new CapabilityItem(boots.Value, Count: 1, MenuSlot: 8, EnchantmentReadout.None)],
    };

    private static PathResult PlanCorridor(FixtureWorld world, PathfinderCapabilities capabilities)
    {
        PlanningWorldView view = world.Capture(LaneStart, LaneGoal, margin: 16);
        return PathPlanner.FindPath(
            view, PathfinderOptions.Default, LaneStart, new GoalBlock(LaneGoal), capabilities: capabilities);
    }

    /// <summary>THE CLAIM. Leather boots on, and the snow section is floor the plan walks straight over.</summary>
    [Fact]
    public void BootedPlayer_CrossesAPowderSnowCorridorWithNoDetour()
    {
        PathResult result = PlanCorridor(PowderSnowCorridor(), Booted(LeatherBoots));

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.Equal(LaneGoal.X, result.Path[^1].X);
        Assert.All(result.Path, node => Assert.Equal(BodyY, node.Y));

        // Not merely "a route exists": the body STOOD on the snow. Six cells is wider than any parkour span, but asserting the middle node as well says so directly rather than by arithmetic.
        Assert.Contains(result.Path, node => node.X is > SnowStartX and < SnowEndX && node.Y == BodyY);
    }

    [Fact]
    public void BarePlayer_RefusesThePowderSnowCorridor()
    {
        PathResult result = PlanCorridor(PowderSnowCorridor(), Booted(boots: null));

        Assert.NotEqual(PathStatus.Success, result.Status);
    }

    /// <summary>Leather boots and nothing else. The rule checks that exact item, not a material class or armour slot in general, so the best boots in the game do nothing here.</summary>
    [Fact]
    public void NetheriteBootsDoNotWalkOnPowderSnow()
    {
        PathResult result = PlanCorridor(PowderSnowCorridor(), Booted(NetheriteBoots));

        Assert.NotEqual(PathStatus.Success, result.Status);
    }

    /// <summary>The polarity of the UNKNOWN, which is the direction that kills. A producer with no inventory tracking reports an empty item list, which is indistinguishable by value from a barefoot player; the conservative reading is the only safe one, and it keeps the hazard.</summary>
    [Fact]
    public void PowderSnowStaysAHazardWhenTheInventoryIsUnknown()
    {
        CalculationContext ctx = ContextOverSnow(PathfinderCapabilities.None);

        Assert.False(ctx.PowderSnowWalkable);
        Assert.True(MoveHelper.IsHazardAt(ctx, 5, FloorY, 3));
        Assert.False(MoveHelper.CanWalkOn(ctx, 5, FloorY, 3));
    }

    /// <summary>The three arms, asked one at a time. Booted: not a hazard, a floor, and NOT passable. The last of those is the one an implementation that only clears the hazard gets backwards.</summary>
    [Fact]
    public void BootedPlayer_SeesPowderSnowAsAFloorThatCannotBeEntered()
    {
        CalculationContext ctx = ContextOverSnow(Booted(LeatherBoots));

        Assert.True(ctx.PowderSnowWalkable);
        Assert.False(MoveHelper.IsHazardAt(ctx, 5, FloorY, 3));
        Assert.True(MoveHelper.CanWalkOn(ctx, 5, FloorY, 3));
        Assert.False(MoveHelper.IsOpenGap(ctx, 5, FloorY, 3));
        Assert.False(MoveHelper.CanWalkThrough(ctx, 5, FloorY, 3));
        Assert.True(MoveHelper.CanStandAt(ctx, 5, BodyY, 3));
    }

    [Fact]
    public void BootedPlayer_MayNotPlantANodeInsideThePowderSnow()
    {
        CalculationContext ctx = ContextOverSnow(Booted(LeatherBoots));

        Assert.False(MoveHelper.CanWalkThrough(ctx, 5, FloorY, 3));
        Assert.False(MoveHelper.CanStandAt(ctx, 5, FloorY, 3));
    }

    /// <summary>The same three questions with nothing on the feet, which is <c>F7</c>'s world and must not have moved: a hazard, not a floor, not an open gap, not passable.</summary>
    [Fact]
    public void BarePlayer_SeesPowderSnowExactlyAsItAlwaysDid()
    {
        CalculationContext ctx = ContextOverSnow(Booted(boots: null));

        Assert.False(ctx.PowderSnowWalkable);
        Assert.True(MoveHelper.IsHazardAt(ctx, 5, FloorY, 3));
        Assert.False(MoveHelper.CanWalkOn(ctx, 5, FloorY, 3));
        Assert.False(MoveHelper.IsOpenGap(ctx, 5, FloorY, 3));
        Assert.False(MoveHelper.CanWalkThrough(ctx, 5, FloorY, 3));
        Assert.False(MoveHelper.CanStandAt(ctx, 5, BodyY, 3));
    }

    [Fact]
    public void BootsDoNotOverrideAnExplicitAvoidSet()
    {
        var options = PathfinderOptions.Default with
        {
            BlocksToAvoid = new HashSet<Identifier> { Identifier.Minecraft("powder_snow") },
        };
        CalculationContext ctx = FixtureContext.Around(
            PowderSnowCorridor(), 5, FloorY, 3, options, margin: 16, capabilities: Booted(LeatherBoots));

        Assert.True(ctx.PowderSnowWalkable);
        Assert.True(MoveHelper.IsHazardAt(ctx, 5, FloorY, 3));
        Assert.False(MoveHelper.CanWalkOn(ctx, 5, FloorY, 3));
    }

    [Fact]
    public void ElevationOverPowderSnowIsTheCellTopWithOrWithoutBoots()
    {
        FixtureWorld world = PowderSnowCorridor();
        PlanningWorldView view = world.Capture(LaneStart, LaneGoal, margin: 16);

        Assert.Equal(
            BodyY,
            PathSegmentBuilder.ResolveElevation(view, 5, BodyY, 3),
            6);

        foreach (PathfinderCapabilities capabilities in new[] { Booted(LeatherBoots), Booted(null) })
        {
            var ctx = new CalculationContext(view, PathfinderOptions.Default, capabilities);
            Assert.Equal(BodyY, ctx.SupportElevation(5, BodyY, 3), 6);
        }
    }

    /// <summary>A corridor whose only route DROPS onto the powder-snow lane from <paramref name="drop"/> blocks up. The upper shelf runs to dx 4 and there is nothing beside it, so the plan either takes the drop or there is no plan.</summary>
    private static FixtureWorld PowderSnowLedge(int drop)
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY + drop, 3, 4, FloorY + drop, 3, FixtureWorld.Stone);
        world.Fill(SnowStartX + 1, FloorY - 1, 3, 13, FloorY - 1, 3, FixtureWorld.Stone);
        world.Fill(SnowStartX + 1, FloorY, 3, 10, FloorY, 3, FixtureWorld.PowderSnow);
        world.Fill(11, FloorY, 3, 13, FloorY, 3, FixtureWorld.Stone);
        return world;
    }

    private static PathResult PlanLedge(int drop, PathfinderCapabilities capabilities)
    {
        FixtureWorld world = PowderSnowLedge(drop);
        var start = new BlockPos(1, BodyY + drop, 3);
        var goal = new BlockPos(12, BodyY, 3);
        PlanningWorldView view = world.Capture(start, goal, margin: 16);
        return PathPlanner.FindPath(
            view, PathfinderOptions.Default, start, new GoalBlock(goal), capabilities: capabilities);
    }

    [Fact]
    public void BootedPlayer_WillNotDropMoreThanTwoBlocksOntoPowderSnow()
        => Assert.NotEqual(PathStatus.Success, PlanLedge(3, Booted(LeatherBoots)).Status);

    /// <summary>The control, and it is what stops the gate above from being a blanket refusal: a TWO-block drop never reaches <c>fallDistance &gt; 2.5</c>, the cube is there when the body arrives, and the plan is exactly right.</summary>
    [Fact]
    public void BootedPlayer_StillDropsTwoBlocksOntoPowderSnow()
    {
        PathResult result = PlanLedge(2, Booted(LeatherBoots));

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.Contains(result.Path, node => node.X is > SnowStartX and <= 10 && node.Y == BodyY);
    }

    /// <summary>The capability itself, read straight off a capture. Feet slot only: the same leather boots in the hand or on the head do nothing, because Only the feet slot is considered.</summary>
    [Theory]
    [InlineData(8, true)]
    [InlineData(5, false)]
    [InlineData(7, false)]
    [InlineData(36, false)]
    [InlineData(45, false)]
    public void PowderSnowWalkableReadsTheFeetSlotAlone(int menuSlot, bool expected)
    {
        var capabilities = new PathfinderCapabilities
        {
            EffectsKnown = false,
            InventoryKnown = true,
            VitalsKnown = false,
            Items = [new CapabilityItem(LeatherBoots, Count: 1, menuSlot, EnchantmentReadout.None)],
        };

        Assert.Equal(expected, capabilities.PowderSnowWalkable);
        Assert.Equal(
            expected,
            capabilities.Equipment(EquipmentSlot.Feet)?.ItemId == LeatherBoots);
    }

    /// <summary>An empty capture claims nothing, which is the conservative answer.</summary>
    [Fact]
    public void NoCaptureIsNotWalkable() => Assert.False(PathfinderCapabilities.None.PowderSnowWalkable);

    private static CalculationContext ContextOverSnow(PathfinderCapabilities capabilities)
        => FixtureContext.Around(
            PowderSnowCorridor(), 5, FloorY, 3, options: null, margin: 16, capabilities: capabilities);
}
