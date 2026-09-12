using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>Tests the stuck-speed multiplier applied by cobweb contact.</summary>
/// <remarks>
/// <para>Entering a cobweb arms a <c>(0.25, 0.05F, 0.25)</c> movement multiplier and clears fall distance. The next movement applies that multiplier and clears velocity. Those values are unchanged across supported eras, so there is no era axis.</para>
/// </remarks>
public sealed class CobwebStuckSpeedTests
{
    /// <summary>Facing +X (east): vanilla yaw 0 looks +Z, and -90 rotates the look vector to +X.</summary>
    private const float YawEast = -90f;

    private const int Settle = 60;
    private const int Measured = 400;

    private static PlayerPhysics NewEngine(FixtureWorld world)
    {
        var engine = new PlayerPhysics(world, PhysicsProfile.ForProtocol(774));
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(0.5, 64.0, 0.5), YawEast, 0f);
        return engine;
    }

    private static FixtureWorld Lane(bool webbed)
    {
        var world = new FixtureWorld().Floor(0, 400, -4, 4, 63, BlockKind.Stone);
        if (webbed)
            world.Fill(0, 64, -4, 400, 65, 4, BlockKind.Cobweb);

        return world;
    }

    /// <summary>Blocks a body covers per tick down a straight lane, after it has reached steady state.</summary>
    private static double BlocksPerTick(bool webbed, bool sprint)
    {
        PlayerPhysics engine = NewEngine(Lane(webbed));
        var input = new MovementInput { Forward = true, Sprint = sprint };
        for (int i = 0; i < Settle; i++)
            engine.Step(input);

        double x0 = engine.State.Position.X;
        for (int i = 0; i < Measured; i++)
            engine.Step(input);

        return (engine.State.Position.X - x0) / Measured;
    }

    /// <summary>The measurement <see cref="Umpk.Pathfinding.Core.ActionCosts.MeasuredWebCostMultiplier"/> is taken from, restated here as an assertion so the constant cannot drift away from the engine.</summary>
    /// <remarks>
    /// Measured over 400 ticks on protocol 774 after a 60-tick settle:
    /// <code>
    /// open lane, walk   : 0.21585907 blocks/tick ->  4.63265225 ticks a block open lane, sprint : 0.28061681 blocks/tick ->  3.56357843 ticks a block cobweb, walk      : 0.02450000 blocks/tick -> 40.81632513 ticks a block cobweb, sprint    : 0.03185000 blocks/tick -> 31.39717120 ticks a block
    /// </code>
    /// The ratios are 8.8105739179753861 (walk) and 8.8105739179754305 (sprint), which agree to twelve decimals, and that is structural rather than lucky: the multiplier zeroes the velocity every tick, so the body restarts from rest and its speed stops depending on what it would have reached unimpeded.
    /// </remarks>
    [Theory]
    [InlineData(false, 0.21585907, 0.02450000)]
    [InlineData(true, 0.28061681, 0.03185000)]
    public void ACobwebCostsTheSameMultipleOfWalkingAndOfSprinting(bool sprint, double open, double webbed)
    {
        double clear = BlocksPerTick(webbed: false, sprint);
        double stuck = BlocksPerTick(webbed: true, sprint);

        Assert.Equal(open, clear, 6);
        Assert.Equal(webbed, stuck, 6);
        Assert.Equal(8.8105739, clear / stuck, 6);
    }

    /// <summary>The other half of cobweb contact, and the reason a web is a safety net as well as a trap: it zeroes the fall distance every tick the body is inside one, so a body that drops into a web takes no fall damage from the drop that got it there.</summary>
    /// <remarks>The witness is the LANDING fall distance, not the resting one, which is always zero. Dropped 26 blocks onto a floor at y=63 through a three-cell web at 64-66, the body lands having accumulated <b>0</b>: it fell 23 blocks in free air and then crossed the web at 0.05 of the vertical movement a tick, with the distance zeroed on every one of those ticks. A continuous accumulation would reach 25.083335.</remarks>
    [Fact]
    public void ACobwebZeroesTheFallDistance()
    {
        var world = new FixtureWorld().Floor(-4, 4, -4, 4, 63, BlockKind.Stone);
        world.Fill(-1, 64, -1, 1, 66, 1, BlockKind.Cobweb);

        var engine = new PlayerPhysics(world, PhysicsProfile.ForProtocol(774));
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(0.5, 90.0, 0.5), YawEast, 0f);

        double peak = 0.0;
        for (int tick = 0; tick < 2000; tick++)
        {
            StepResult step = engine.Step(MovementInput.None);
            peak = Math.Max(peak, engine.State.FallDistance);
            if (step.Events.Landed)
            {
                Assert.True(peak > 10.0, $"the body never built a fall distance worth zeroing (peak {peak:F3})");
                Assert.Equal(0.0, step.Events.LandingFallDistance, 6);
                return;
            }
        }

        Assert.Fail("the body never landed");
    }

    /// <summary>The polarity control: with no web the same lane is the open-ground rate, so the assertion above is about the web and not about the harness.</summary>
    [Fact]
    public void WithoutAWeb_TheLaneIsOpenGround()
        => Assert.Equal(0.28061681, BlocksPerTick(webbed: false, sprint: true), 6);
}
