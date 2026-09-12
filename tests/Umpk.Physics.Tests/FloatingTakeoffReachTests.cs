using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>How high a body AFLOAT can get itself, which is the measurement the planner's floating-takeoff refusal stands on.</summary>
/// <remarks>
/// <para><see cref="ShallowFluidTests"/> pins the impulse: a held jump uses uses the 0.42 ground jump only when the sensed fluid height is no deeper than <c>0.4</c>, and uses the 0.04 fluid jump otherwise, with no delay. This file pins the reach that impulse actually buys, which is what a move's feasibility depends on.</para>
/// <para><b>The result, and it is the same number at every depth:</b> a body holding Jump in water climbs to a steady rate and then stops the moment its box leaves the fluid, so it peaks a little under a quarter of a block above the WATERLINE and no further - 0.232 to 0.279 over one, two and three layers. A grounded jump's apex is 1.2522 (<c>Umpk.Pathfinding.Tests.Moves.HangingTakeoffTests</c> quotes the same figure), so the whole jump family's calibration is off by a factor of five at a floating takeoff. The only thing that gets a swimmer meaningfully higher is the ledge hop, which needs a bank to press into, reaches 0.9203 over the waterline, and is what <c>MoveSwimExit</c> prices - see <see cref="FluidLedgeHopTests"/>.</para>
/// </remarks>
public sealed class FloatingTakeoffReachTests
{
    private const int FloorY = 64;

    /// <summary>Settles the body on the floor of a pool, holds Jump, and returns the highest Y it reaches. Jump alone, with no forward input: the ledge hop requires pressing into a bank and is a DIFFERENT move (it is what <c>MoveSwimExit</c> prices and <see cref="FluidLedgeHopTests"/> measures), so Forward here would measure the climb-out instead of the bob.</summary>
    private static double PeakHeldJumpY(BlockKind fluid, int layers, int ticks)
    {
        var world = new FixtureWorld().Floor(-6, 6, -6, 6, FloorY - 1, BlockKind.Stone);
        world.Fill(-6, FloorY, -6, 6, FloorY + layers - 1, 6, fluid);
        var engine = new PlayerPhysics(world, PhysicsProfile.Modern);
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(0.5, FloorY, 0.5), 0f, 0f);
        for (int i = 0; i < 10; i++)
            engine.Step(MovementInput.None);

        double peak = engine.State.Position.Y;
        for (int i = 0; i < ticks; i++)
        {
            engine.Step(new MovementInput { Jump = true });
            peak = Math.Max(peak, engine.State.Position.Y);
        }

        return peak;
    }

    /// <summary>The reach over the waterline, at three depths and for both a source column and the FALLING state a real waterfall is made of. Forty ticks is four times the 10-tick jump delay a ground jump is paced by and roughly four times the 8-11 ticks a one-block ascend takes.</summary>
    /// <remarks>The waterline is <c>FloorY + layers - 1 + ownHeight</c>: the top cell's own surface, which for a source and for the falling state alike is <c>8/9</c> at amount 8. The two fluids give bit-identical peaks, which is the point of carrying both rows: the FALLING flag changes the current, not the buoyancy.</remarks>
    [Theory]
    [InlineData(BlockKind.Water, 1, 65.1206)]
    [InlineData(BlockKind.Water, 2, 66.1288)]
    [InlineData(BlockKind.Water, 3, 67.1678)]
    [InlineData(BlockKind.FallingWater, 1, 65.1206)]
    [InlineData(BlockKind.FallingWater, 2, 66.1288)]
    [InlineData(BlockKind.FallingWater, 3, 67.1678)]
    public void ABobbingBodyNeverGetsAQuarterOfABlockOverTheWaterline(BlockKind fluid, int layers, double expectedPeak)
    {
        double waterline = FloorY + layers - 1 + (8.0 / 9.0);
        double peak = PeakHeldJumpY(fluid, layers, ticks: 40);

        Assert.Equal(expectedPeak, peak, 4);

        double overTheWaterline = peak - waterline;
        Assert.InRange(overTheWaterline, 0.2, 0.28);

        // And nowhere near what the jump family is calibrated on.
        Assert.True(
            overTheWaterline < 1.2522 / 4.0,
            $"{fluid} x{layers}: reached {overTheWaterline:F4} over the waterline");
    }

    /// <summary>The control: under the 0.4 threshold the SAME held Jump is a full ground jump and clears the block outright. This is the boundary the planner's refusal is drawn at, and it is a real boundary rather than a chosen one - <c>WaterLevel5</c> is amount 3 (height 1/3) and <c>WaterLevel4</c> is amount 4 (height 4/9), the first amount over 0.4.</summary>
    [Theory]
    [InlineData(BlockKind.WaterLevel7, true, "amount 1, height 1/9")]
    [InlineData(BlockKind.FlowingWater, true, "amount 2, height 2/9")]
    [InlineData(BlockKind.WaterLevel5, true, "amount 3, height 1/3 - the last one under the line")]
    [InlineData(BlockKind.WaterLevel4, false, "amount 4, height 4/9 - the first one over it")]
    [InlineData(BlockKind.WaterLevel1, false, "amount 7, height 7/9")]
    [InlineData(BlockKind.Water, false, "a source, height 8/9")]
    public void TheThresholdIsWhereTheGroundJumpStops(BlockKind fluid, bool groundJump, string why)
    {
        Assert.False(string.IsNullOrEmpty(why));
        double peak = PeakHeldJumpY(fluid, layers: 1, ticks: 40);

        // A ground jump's apex is 1.2522 over the floor; through one tick of water damping it still clears the block by a wide margin. A bob peaks 0.12 over it at the very most.
        if (groundJump)
            Assert.True(peak > FloorY + 1.2, $"{fluid}: peaked at {peak:F4}, not a ground jump");

        else
            Assert.True(peak < FloorY + 1.2, $"{fluid}: peaked at {peak:F4}, that is a ground jump");

    }
}
