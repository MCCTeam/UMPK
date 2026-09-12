using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Xunit;

namespace Umpk.Pathfinding.Tests.Moves;

public sealed class PressurePlateActivationTests
{
    private const int FloorY = 99;

    private const int BodyY = 100;

    private const int CourseMargin = 24;

    private const int LaneZ = 1;

    private static readonly BlockPos LaneStart = new(0, BodyY, LaneZ);

    private static readonly BlockPos LaneGoal = new(7, BodyY, LaneZ);

    private static readonly BlockPos Door = new(4, BodyY, LaneZ);

    private static FixtureWorld PlateLane(int plateState, int plateX = 3, int length = 7)
    {
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, length, FloorY, 2, FixtureWorld.Stone);
        world.Fill(0, BodyY, 0, length, BodyY + 1, 0, FixtureWorld.Stone);
        world.Fill(0, BodyY, 2, length, BodyY + 1, 2, FixtureWorld.Stone);
        world.Set(4, BodyY, LaneZ, FixtureWorld.IronDoorClosed);
        world.Set(4, BodyY + 1, LaneZ, FixtureWorld.IronDoorClosed);
        if (plateState != FixtureWorld.Air)
            world.Set(plateX, BodyY, LaneZ, plateState);

        return world;
    }

    private static CalculationContext Context(FixtureWorld world)
        => new(world.Capture(LaneStart, LaneGoal, CourseMargin), PathfinderOptions.Default);

    /// <summary>The whole feature in one assertion: a wooden plate in the approach cell resolves, and it resolves with the plate's OWN cell as the stand cell. That last part is what makes the narrow design work without any causal planning - the cell the plan has to put the body in to open the door is the cell the plan was already going to route it through.</summary>
    [Fact]
    public void TryResolve_WithAWoodenPlateInTheApproachCell_ResolvesThePlateItself()
    {
        CalculationContext ctx = Context(PlateLane(FixtureWorld.OakPressurePlate));

        Assert.True(DoorActivation.TryResolve(ctx, Door, out DoorActivation activation));
        Assert.Equal(ActivatorKind.PressurePlate, activation.Kind);
        Assert.Equal(new BlockPos(3, BodyY, LaneZ), activation.Activator);
        Assert.Equal(new BlockPos(3, BodyY, LaneZ), activation.StandCell);
        Assert.Equal(DoorActivation.PlatePressedTicks, activation.WindowTicks);
    }

    [Theory]
    [InlineData(FixtureWorld.OakPressurePlate, 20)]
    [InlineData(FixtureWorld.StonePressurePlate, 20)]
    [InlineData(FixtureWorld.PolishedBlackstonePressurePlate, 20)]
    public void TryResolve_EveryPlainFamily_ResolvesOnTheSameTwentyTickPress(int plateState, int expectedWindow)
    {
        CalculationContext ctx = Context(PlateLane(plateState));

        Assert.True(DoorActivation.TryResolve(ctx, Door, out DoorActivation activation));
        Assert.Equal(ActivatorKind.PressurePlate, activation.Kind);
        Assert.Equal(expectedWindow, activation.WindowTicks);
    }

    [Theory]
    [InlineData(FixtureWorld.LightWeightedPressurePlate)]
    [InlineData(FixtureWorld.HeavyWeightedPressurePlate)]
    public void TryResolve_AWeightedPlate_IsRefusedBecauseTenTicksCannotHoldASeventeenTickCrossing(int plateState)
    {
        CalculationContext ctx = Context(PlateLane(plateState));

        Assert.False(DoorActivation.TryResolve(ctx, Door, out _));
        Assert.False(DoorActivation.FitsWindow(
            blocks: 2, DoorActivation.WeightedPlatePressedTicks, lid: false, chargeLatency: false));
        Assert.True(DoorActivation.FitsWindow(
            blocks: 2, DoorActivation.PlatePressedTicks, lid: false, chargeLatency: false));
    }

    [Fact]
    public void TryResolve_WithAPlateOneCellTooFarBack_FindsNothing()
    {
        CalculationContext ctx = Context(PlateLane(FixtureWorld.OakPressurePlate, plateX: 2));

        Assert.False(DoorActivation.TryResolve(ctx, Door, out _));
    }

    /// <summary>A plate wired to the door through dust is refused for the same reason a button wired that way is (course row L4). The relay is walkable - dust has the empty collision shape - so the LANE is fine and only the signal is unmodelled; the honest answer is a refusal rather than a bot that stands in front of a door it has no reason to think will move.</summary>
    [Fact]
    public void TryResolve_WithAPlateWiredThroughDust_StillFindsNothing()
    {
        FixtureWorld world = PlateLane(FixtureWorld.OakPressurePlate, plateX: 1);
        world.Set(2, BodyY, LaneZ, FixtureWorld.RedstoneWire);
        world.Set(3, BodyY, LaneZ, FixtureWorld.RedstoneWire);

        Assert.False(DoorActivation.TryResolve(Context(world), Door, out _));
    }

    /// <summary>A plate whose <c>powered</c> cannot be read is not an activator. This is the pre-flattening band (protocols 47-340), where every property answers false, and it is the case a name-only rule gets wrong: the block is still called <c>oak_pressure_plate</c> and only the property read fails. Same policy the door side already states - where the era cannot say, refuse rather than guess.</summary>
    [Fact]
    public void TryResolve_WithAPlateWhosePropertyCannotBeRead_FindsNothing()
    {
        CalculationContext ctx = Context(PlateLane(FixtureWorld.UnreadablePressurePlate));

        Assert.False(DoorActivation.TryResolve(ctx, Door, out _));
    }

    [Fact]
    public void TryResolve_ForAnIronTrapdoor_RefusesAPlateButStillAcceptsAButton()
    {
        var lid = new BlockPos(4, BodyY, LaneZ);
        var world = new FixtureWorld();
        world.Fill(0, FloorY, 0, 7, FloorY, 2, FixtureWorld.Stone);
        world.Fill(0, BodyY, 0, 7, BodyY + 1, 0, FixtureWorld.Stone);
        world.Fill(0, BodyY, 2, 7, BodyY + 1, 2, FixtureWorld.Stone);
        world.Set(lid.X, lid.Y, lid.Z, FixtureWorld.IronTrapdoorClosedTop);
        world.Set(3, BodyY, LaneZ, FixtureWorld.OakPressurePlate);

        Assert.False(DoorActivation.TryResolve(Context(world), lid, out _));

        world.Set(3, BodyY, LaneZ, FixtureWorld.OakButtonWallSouth);

        Assert.True(DoorActivation.TryResolve(Context(world), lid, out DoorActivation viaButton));
        Assert.Equal(ActivatorKind.Button, viaButton.Kind);
    }

    /// <summary>A lever beside the same door beats the plate. A latch imposes no window at all, where a plate imposes one the crossing has to fit, so the preference is the same reasoning the button-versus- lever ordering already carries.</summary>
    [Fact]
    public void TryResolve_WithBothALeverAndAPlate_PrefersTheLever()
    {
        FixtureWorld world = PlateLane(FixtureWorld.OakPressurePlate);
        world.Set(3, BodyY + 1, LaneZ, FixtureWorld.LeverWallSouth);

        Assert.True(DoorActivation.TryResolve(Context(world), Door, out DoorActivation activation));
        Assert.Equal(ActivatorKind.Lever, activation.Kind);
    }

    [Fact]
    public void TryResolve_WithBothAButtonAndAPlate_PrefersTheButton()
    {
        FixtureWorld world = PlateLane(FixtureWorld.OakPressurePlate);
        world.Set(3, BodyY + 1, LaneZ, FixtureWorld.OakButtonWallSouth);

        Assert.True(DoorActivation.TryResolve(Context(world), Door, out DoorActivation activation));
        Assert.Equal(ActivatorKind.Button, activation.Kind);
    }

    /// <summary>The observe latency is NOT inside a plate's window, and this is the one line of arithmetic that separates a plate from a button. A button's window clock starts at the press and the round trip that confirms the door moved is spent standing at the press cell, inside the same budget. A plate's is spent standing ON THE PLATE, which is what holds the door open, so charging it would be charging the body for time during which the door cannot close. With the latency charged, a two-block crossing is 27 ticks and no plate in the game holds that long.</summary>
    [Fact]
    public void FitsWindow_ChargingTheLatency_IsWhatWouldRefuseEveryPlateInTheGame()
    {
        Assert.False(DoorActivation.FitsWindow(
            blocks: 2, DoorActivation.PlatePressedTicks, lid: false, chargeLatency: true));
        Assert.True(DoorActivation.FitsWindow(
            blocks: 2, DoorActivation.PlatePressedTicks, lid: false, chargeLatency: false));
    }

    [Fact]
    public void FitsWindow_TheExistingOverloads_StillChargeTheLatency()
    {
        Assert.False(DoorActivation.FitsWindow(blocks: 2, DoorActivation.StoneButtonWindowTicks));
        Assert.False(DoorActivation.FitsWindow(blocks: 2, DoorActivation.StoneButtonWindowTicks, lid: false));
        Assert.True(DoorActivation.FitsWindow(blocks: 2, DoorActivation.WoodenButtonWindowTicks));
    }
}
