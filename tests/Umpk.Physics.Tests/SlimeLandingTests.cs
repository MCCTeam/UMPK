using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>Observable results of a twelve-block drop onto slime, used by the executor's settle-and-sneak policy.</summary>
/// <remarks>
/// <para><c>slime bounce response</c> inverts a downward velocity with NO decay factor for a living entity: <c>vy = -vy * 1.0</c>, unless the player is sneaking, which for a player is the sneak key, in which case ordinary landing zeroes vertical velocity runs instead. Drag and the fall profile are what make the cycle decay, not the block.</para>
/// <para>Both landing branches deal zero damage: an ordinary landing uses a zero multiplier, while a suppressed bounce skips damage processing, so the sneak buys the bounce cancel for no health at all. That is worth stating because the opposite is easy to assume.</para>
/// </remarks>
public sealed class SlimeLandingTests
{
    /// <summary>N1's shape reduced to what decides it: a twelve-block drop onto a three-wide pad.</summary>
    private static FixtureWorld Pad(BlockKind pad)
    {
        var world = new FixtureWorld();
        world.Fill(6, 87, 1088, 17, 87, 1090, BlockKind.Stone);
        world.Fill(6, 87, 1088, 8, 87, 1090, pad);
        return world;
    }

    private readonly record struct Landing(int RealBounces, int SettledAtTick, double EndY);

    /// <summary>Drops a body from y=100 and reports how many bounces LIFT it and when it is finally stable on the pad at y=88.</summary>
    /// <remarks>"Real" bounces are the inversions whose speed exceeds 0.08, because a body at rest on slime keeps inverting its own gravity tick forever at about 0.0398 and those are chatter rather than bounces: the position does not move. Settling is therefore judged on POSITION, which is the only thing an executor's completion gate can honestly read.</remarks>
    private static Landing Drop(BlockKind pad, bool sneak)
    {
        var engine = new PlayerPhysics(Pad(pad), PhysicsProfile.ForProtocol(774));
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(7.5, 100.0, 1089.5), 0f, 0f);
        MovementInput input = sneak ? new MovementInput { Sneak = true } : MovementInput.None;

        int real = 0;
        int settledAt = -1;
        for (int tick = 0; tick < 400; tick++)
        {
            StepResult r = engine.Step(input);
            if (r.Events.Bounced && Math.Abs(r.State.Velocity.Y) > 0.08)
                real++;

            if (Math.Abs(r.State.Position.Y - 88.0) > 0.02)
                settledAt = -1;

            else if (settledAt < 0 && r.State.OnGround)
                settledAt = tick;

        }

        return new Landing(real, settledAt, engine.State.Position.Y);
    }

    /// <summary>The bounce, and the sneak that cancels it. Ten lifting inversions and 152 ticks to settle without the sneak; zero and 18 with it, which is exactly what the identical drop onto hay costs.</summary>
    [Fact]
    public void ATwelveBlockDropOntoSlime_BouncesTenTimesUnlessTheBodySneaks()
    {
        Landing bouncing = Drop(BlockKind.SlimeBlock, sneak: false);
        Landing sneaked = Drop(BlockKind.SlimeBlock, sneak: true);
        Landing hay = Drop(BlockKind.HayBlock, sneak: false);

        Assert.Equal(10, bouncing.RealBounces);
        Assert.Equal(152, bouncing.SettledAtTick);

        Assert.Equal(0, sneaked.RealBounces);
        Assert.Equal(18, sneaked.SettledAtTick);

        Assert.Equal(0, hay.RealBounces);
        Assert.Equal(18, hay.SettledAtTick);

        // Whatever the route, the body ends on the pad rather than beside it.
        Assert.Equal(88.0, bouncing.EndY, 4);
        Assert.Equal(88.0, sneaked.EndY, 4);
        Assert.Equal(88.0, hay.EndY, 4);
    }

    /// <summary>The pre-flattening spelling is the same block. The engine's slime predicate matched <c>minecraft:slime_block</c> alone, so eleven protocols got no bounce and no step-on slowdown at all - while <c>MoveHelper</c>'s own slime predicate had carried both names since it was written, which is exactly the planner/engine disagreement this repository exists to prevent.</summary>
    [Fact]
    public void ThePreFlatteningSlimeName_BouncesToo()
    {
        Landing modern = Drop(BlockKind.SlimeBlock, sneak: false);
        Landing legacy = Drop(BlockKind.LegacySlimeBlock, sneak: false);

        Assert.Equal(modern.RealBounces, legacy.RealBounces);
        Assert.Equal(modern.SettledAtTick, legacy.SettledAtTick);
    }
}
