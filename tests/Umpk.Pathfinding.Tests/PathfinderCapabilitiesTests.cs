using Umpk.Game.Entities;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Pathfinding;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests;

/// <summary>The capability surface itself (A1) and its arrival on both contexts (A2). Pure construct-and-read coverage: nothing here builds a producer, because A1/A2 deliberately ship the shape with no producer behind it.</summary>
public sealed class PathfinderCapabilitiesTests
{
    private static readonly Identifier FireResistance = Identifier.Minecraft("fire_resistance");
    private static readonly Identifier WaterBreathing = Identifier.Minecraft("water_breathing");
    private static readonly Identifier Torch = Identifier.Minecraft("torch");
    private static readonly Identifier DiamondBoots = Identifier.Minecraft("diamond_boots");

    /// <summary>The whole point of <see cref="PathfinderCapabilities.None"/>: it is what a context gets when nobody captured anything, and it must read as UNKNOWN rather than as "the player has nothing". An empty list with <c>EffectsKnown = true</c> would be the dangerous polarity.</summary>
    [Fact]
    public void None_ReadsAsUnknownOnEveryArm()
    {
        PathfinderCapabilities none = PathfinderCapabilities.None;

        Assert.False(none.EffectsKnown);
        Assert.False(none.InventoryKnown);
        Assert.Empty(none.Effects);
        Assert.Empty(none.Items);
        Assert.False(none.TryGetEffect(FireResistance, out CapabilityEffect effect));
        Assert.Equal(default, effect);
        Assert.Equal(0, none.CountOf(Torch));
        Assert.Null(none.Equipment(EquipmentSlot.Feet));
    }

    [Fact]
    public void TryGetEffect_FindsAnEffectByIdentifierAndMissesTheRest()
    {
        var capabilities = new PathfinderCapabilities
        {
            EffectsKnown = true,
            InventoryKnown = false,
            VitalsKnown = false,
            Effects =
            [
                new CapabilityEffect(FireResistance, NetworkId: 12, Amplifier: 0, RemainingTicks: 160,
                    IsInfinite: false, DurationIsEstimated: true),
            ],
        };

        Assert.True(capabilities.TryGetEffect(FireResistance, out CapabilityEffect found));
        Assert.Equal(160, found.RemainingTicks);
        Assert.Equal(12, found.NetworkId);
        Assert.True(found.DurationIsEstimated);
        Assert.False(capabilities.TryGetEffect(WaterBreathing, out _));
    }

    /// <summary><c>CountOf</c> returning 0 must never be read as "the player has none": with the Inventory feature off it is the ONLY answer available, which is why the caller has to consult <see cref="PathfinderCapabilities.InventoryKnown"/> first.</summary>
    [Fact]
    public void CountOf_SumsAcrossStacks_AndReturnsZeroWhenInventoryIsUnknown()
    {
        var known = new PathfinderCapabilities
        {
            EffectsKnown = false,
            InventoryKnown = true,
            VitalsKnown = false,
            Items =
            [
                new CapabilityItem(Torch, Count: 12, MenuSlot: 36, EnchantmentReadout.None),
                new CapabilityItem(Torch, Count: 30, MenuSlot: 9, EnchantmentReadout.None),
            ],
        };

        Assert.Equal(42, known.CountOf(Torch));
        Assert.Equal(0, known.CountOf(DiamondBoots));

        var unknown = new PathfinderCapabilities { EffectsKnown = false, InventoryKnown = false, VitalsKnown = false };
        Assert.Equal(0, unknown.CountOf(Torch));
    }

    /// <summary>The menu indices are <c>PlayerInventorySlotMap</c>'s, not a second index space invented here: head 5, chest 6, legs 7, feet 8, hotbar 36-44, offhand 45.</summary>
    [Theory]
    [InlineData(EquipmentSlot.Head, 5)]
    [InlineData(EquipmentSlot.Chest, 6)]
    [InlineData(EquipmentSlot.Legs, 7)]
    [InlineData(EquipmentSlot.Feet, 8)]
    [InlineData(EquipmentSlot.OffHand, 45)]
    [InlineData(EquipmentSlot.MainHand, 36 + 4)]
    public void Equipment_ResolvesEachSlotToItsMenuIndex(EquipmentSlot slot, int menuSlot)
    {
        var capabilities = new PathfinderCapabilities
        {
            EffectsKnown = false,
            InventoryKnown = true,
            VitalsKnown = false,
            HeldSlot = 4,
            Items = [new CapabilityItem(DiamondBoots, Count: 1, menuSlot, EnchantmentReadout.None)],
        };

        CapabilityItem? item = capabilities.Equipment(slot);
        Assert.NotNull(item);
        Assert.Equal(menuSlot, item!.Value.MenuSlot);
    }

    /// <summary>Body and saddle are mount equipment with no slot in the player window at all (<c>PlayerInventorySlotMap</c>'s own remarks). Answering them with a guessed index would be exactly the mistake that file exists to prevent.</summary>
    [Theory]
    [InlineData(EquipmentSlot.Body)]
    [InlineData(EquipmentSlot.Saddle)]
    public void Equipment_RefusesSlotsWithNoPlayerWindowIndex(EquipmentSlot slot)
    {
        var capabilities = new PathfinderCapabilities
        {
            EffectsKnown = false,
            InventoryKnown = true,
            VitalsKnown = false,
            Items = [new CapabilityItem(DiamondBoots, Count: 1, MenuSlot: 8, EnchantmentReadout.None)],
        };

        Assert.Null(capabilities.Equipment(slot));
    }

    [Fact]
    public void Equipment_ReturnsNull_WhenInventoryIsUnknown()
    {
        var capabilities = new PathfinderCapabilities { EffectsKnown = false, InventoryKnown = false, VitalsKnown = false };
        Assert.Null(capabilities.Equipment(EquipmentSlot.Feet));
    }

    /// <summary>The default readout is the "no item / nothing captured" one, and its <c>CanEnumerate</c> is false so an empty <c>All</c> is never read as "this item has no enchantments".</summary>
    [Fact]
    public void EnchantmentReadout_None_CannotEnumerateAndFindsNoLevel()
    {
        EnchantmentReadout readout = EnchantmentReadout.None;

        Assert.False(readout.CanEnumerate);
        Assert.Empty(readout.All);
        Assert.False(readout.TryGetLevel(Identifier.Minecraft("depth_strider"), out int level));
        Assert.Equal(0, level);
    }

    [Fact]
    public void CalculationContext_DefaultsToNone_NotNull()
    {
        var world = new FixtureWorld();
        world.Floor(-4, 4, -4, 4, 64);
        PlanningWorldView view = world.Capture(new BlockPos(0, 65, 0), new BlockPos(2, 65, 0), margin: 4);

        var ctx = new CalculationContext(view, PathfinderOptions.Default);

        Assert.Same(PathfinderCapabilities.None, ctx.Capabilities);
    }

    [Fact]
    public void CalculationContext_CarriesTheCapabilitiesItWasGiven()
    {
        var world = new FixtureWorld();
        world.Floor(-4, 4, -4, 4, 64);
        PlanningWorldView view = world.Capture(new BlockPos(0, 65, 0), new BlockPos(2, 65, 0), margin: 4);
        var capabilities = new PathfinderCapabilities { EffectsKnown = true, InventoryKnown = true, VitalsKnown = false };

        var ctx = new CalculationContext(view, PathfinderOptions.Default, capabilities);

        Assert.Same(capabilities, ctx.Capabilities);
    }

    [Fact]
    public void PathExecutionContext_DefaultsToNone_NotNull()
    {
        var world = new FixtureWorld();
        world.Floor(-4, 4, -4, 4, 64);
        PlanningWorldView view = world.Capture(new BlockPos(0, 65, 0), new BlockPos(2, 65, 0), margin: 4);

        var ctx = new PathExecutionContext(view, PhysicsProfile.ForProtocol(774));

        Assert.Same(PathfinderCapabilities.None, ctx.Capabilities);
    }

    [Fact]
    public void PathExecutionContext_CarriesTheCapabilitiesItWasGiven()
    {
        var world = new FixtureWorld();
        world.Floor(-4, 4, -4, 4, 64);
        PlanningWorldView view = world.Capture(new BlockPos(0, 65, 0), new BlockPos(2, 65, 0), margin: 4);
        var capabilities = new PathfinderCapabilities { EffectsKnown = true, InventoryKnown = false, VitalsKnown = false };

        var ctx = new PathExecutionContext(
            view, PhysicsProfile.ForProtocol(774), PhysicsConditions.Default, allowSprint: true, capabilities);

        Assert.Same(capabilities, ctx.Capabilities);
    }

    /// <summary>A representative course with a step up, a step down, and a gap has a pinned node sequence, move sequence, and total cost. Supplying the capability context must not change any edge.</summary>
    [Fact]
    public void RepresentativePlan_IsUnchangedByTheCapabilitySurface()
    {
        var world = new FixtureWorld();
        world.Floor(-8, 20, -8, 8, 64);
        // A one-block step up at x=4, a two-block plateau, then back down at x=7.
        world.Fill(4, 65, -2, 6, 65, 2, FixtureWorld.Stone);
        var start = new BlockPos(0, 65, 0);
        var goalPos = new BlockPos(12, 65, 0);
        PlanningWorldView view = world.Capture(start, goalPos, margin: 8);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goalPos));

        Assert.Equal(PathStatus.Success, result.Status);
        Assert.Equal(
            "0,65,0 1,65,0 2,65,0 3,65,0 4,66,0 5,66,0 6,66,0 8,65,0 9,65,0 10,65,0 11,65,0 12,65,0",
            string.Join(' ', result.Path.Select(n => $"{n.X},{n.Y},{n.Z}")));
        Assert.Equal(
            "Traverse Traverse Traverse Ascend Traverse Traverse Descend Traverse Traverse Traverse Traverse",
            string.Join(' ', result.Moves));
        Assert.Equal(49.76550249465431, result.Cost, 9);
    }
}
