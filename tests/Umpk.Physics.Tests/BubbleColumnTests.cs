using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>Tests upward bubble-column behavior. The magma arm is deliberately absent and its omission is asserted explicitly.</summary>
/// <remarks>
/// <para>The column reads the cell above and applies the above-cell or inside-cell impulse:</para>
/// <code>
/// above,  drag=false: vy = min( 1.8, vy + 0.1 ) inside, drag=false: vy = min( 0.7, vy + 0.06), then resetFallDistance() above,  drag=true : vy = max(-0.9, vy - 0.03)   NOT MODELLED inside, drag=true : vy = max(-0.3, vy - 0.03)   NOT MODELLED
/// </code>
/// <para>This behavior is unchanged across the supported eras, so it has no era axis.</para>
/// <para><b>Why an elevator is fast.</b> The impulse is applied once per CELL the body's box overlaps, because contact effects run for every overlapped cell, and a 1.8-tall body overlaps two or three column cells. The engine reproduces that because the scan is the same scan.</para>
/// </remarks>
public sealed class BubbleColumnTests
{
    /// <summary>A 1x1 shaft bored through a stone mass, with a 15-cell column and open air above it.</summary>
    private static FixtureWorld Shaft(BlockKind column)
    {
        var world = new FixtureWorld();
        world.Fill(712, 99, 712, 716, 117, 716, BlockKind.Stone);
        world.Fill(714, 100, 714, 714, 114, 714, column);
        world.Fill(714, 115, 714, 714, 116, 714, BlockKind.Air);
        return world;
    }

    /// <summary>Ticks to rise from the column's base to fourteen blocks up, or -1 if it never does.</summary>
    private static int TicksToRise14(BlockKind column, MovementInput input)
    {
        var engine = new PlayerPhysics(Shaft(column), PhysicsProfile.ForProtocol(774));
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(714.5, 100.0, 714.5), 0f, 0f);

        for (int tick = 0; tick < 900; tick++)
        {
            engine.Step(input);
            if (engine.State.Position.Y >= 114.0)
                return tick + 1;

        }

        return -1;
    }

    private static readonly MovementInput Riding = MovementInput.None;

    private static readonly MovementInput Swimming =
        new() { Forward = true, Sprint = true, Jump = true };

    /// <summary>The measurement <see cref="Umpk.Pathfinding.Core.ActionCosts.BubbleColumnUpOneBlock"/> is taken from, restated as an assertion so the constant cannot drift away from the engine.</summary>
    /// <remarks>
    /// <code>
    /// column, no input at all         : 25 ticks / 14 blocks = 1.78571 t/blk column, Forward+Sprint+Jump     : 32 ticks / 14 blocks = 2.28571 t/blk  &lt;- the executor's input plain water, Forward+Sprint+Jump: 96 ticks / 14 blocks = 6.85714 t/blk
    /// </code>
    /// Pressing up is SLOWER than riding, because the column's impulse is a per-tick <c>min(0.7, vy + 0.06)</c> clamp and a body already at the clamp gains nothing from a swim stroke that also changes its drag. The planner charges the executor's 32, not the rider's 25.
    /// </remarks>
    [Fact]
    public void AnUpwardColumn_LiftsAtTheMeasuredRate()
    {
        Assert.Equal(25, TicksToRise14(BlockKind.BubbleColumnUp, Riding));
        Assert.Equal(32, TicksToRise14(BlockKind.BubbleColumnUp, Swimming));
        Assert.Equal(96, TicksToRise14(BlockKind.Water, Swimming));
    }

    /// <summary>The scope line, in numbers. A DOWNWARD column is water and nothing else: a body in one rises at exactly plain water's rate under the same input, and sinks no faster than plain water under no input at all. The downward impulse remains outside this model.</summary>
    [Fact]
    public void ADownwardColumn_IsPlainWaterWithNoDowndraft()
    {
        Assert.Equal(
            TicksToRise14(BlockKind.Water, Swimming),
            TicksToRise14(BlockKind.BubbleColumnDown, Swimming));

        var engine = new PlayerPhysics(Shaft(BlockKind.BubbleColumnDown), PhysicsProfile.ForProtocol(774));
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(714.5, 110.0, 714.5), 0f, 0f);

        var water = new PlayerPhysics(Shaft(BlockKind.Water), PhysicsProfile.ForProtocol(774));
        water.SetConditions(PhysicsConditions.Default);
        water.Reset(new Vec3d(714.5, 110.0, 714.5), 0f, 0f);

        for (int tick = 0; tick < 100; tick++)
        {
            engine.Step(MovementInput.None);
            water.Step(MovementInput.None);
        }

        Assert.Equal(water.State.Position.Y, engine.State.Position.Y, 9);
    }

    /// <summary>The inside arm zeroes the fall distance, so a body that drops into an upward column arrives having taken no fall. The MOUTH arm does not, which is why the witness is a body that reaches the column's interior.</summary>
    [Fact]
    public void AnUpwardColumn_ZeroesTheFallDistance()
    {
        FixtureWorld world = Shaft(BlockKind.BubbleColumnUp);
        world.Fill(714, 117, 714, 714, 140, 714, BlockKind.Air);

        var engine = new PlayerPhysics(world, PhysicsProfile.ForProtocol(774));
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(714.5, 140.0, 714.5), 0f, 0f);

        double peak = 0.0;
        for (int tick = 0; tick < 400; tick++)
        {
            engine.Step(MovementInput.None);
            peak = Math.Max(peak, engine.State.FallDistance);
            if (engine.State.Position.Y < 113.0)
                break;

        }

        Assert.True(peak > 10.0, $"the body never built a fall distance worth zeroing (peak {peak:F3})");
        Assert.Equal(0.0, engine.State.FallDistance, 6);
    }
}
