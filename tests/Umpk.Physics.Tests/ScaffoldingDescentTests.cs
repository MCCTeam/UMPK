using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>The climb-clamp half of shift-descending a scaffold. <see cref="ScaffoldingCollisionTests"/> pins the shape half; this file pins the velocity half, and the two together are what makes a body actually sink.</summary>
/// <remarks>
/// <para>Climbable movement clamps downward velocity to -0.15. Sneaking normally raises that to zero, except while the feet cell is scaffolding.</para>
/// <para>The rule is unchanged across every era with scaffolding. Suppressing ladder descent is the sneak key, and the relevant block state is the feet cell already read for <c>_onClimbable</c>.</para>
/// <para>Without this rule, a sneaking body inside a scaffolding column already sees <c>an empty collision shape</c> - the plates really do vanish, then the clamp zeroes downward velocity and leaves the body hanging in mid-air.</para>
/// </remarks>
public sealed class ScaffoldingDescentTests
{
    /// <summary>Facing +X (east): vanilla yaw 0 looks +Z, and -90 rotates the look vector to +X.</summary>
    private const float YawEast = -90f;

    /// <summary>The pad the column stands on: its top face, and therefore the descent's floor.</summary>
    private const double PadTop = 100.0;

    /// <summary>The column's top face. <c>SHAPE_STABLE</c> tops out at 1.0, so this is an integer plane.</summary>
    private const double ColumnTop = 105.0;

    /// <summary>A five-cell scaffolding column at <c>(650, 100..104, 74)</c> standing on a stone pad whose top face is y=100.</summary>
    private static FixtureWorld ScaffoldWorld()
    {
        var world = new FixtureWorld().Floor(646, 653, 70, 77, 99, BlockKind.Stone);
        for (int y = 100; y <= 104; y++)
            world.Set(650, y, 74, BlockKind.Scaffolding);

        return world;
    }

    /// <summary>The control's world: the identical column built out of ladder instead.</summary>
    private static FixtureWorld LadderWorld()
    {
        var world = new FixtureWorld().Floor(646, 653, 70, 77, 99, BlockKind.Stone);
        for (int y = 100; y <= 104; y++)
            world.Set(650, y, 74, BlockKind.Ladder);

        return world;
    }

    private static PlayerPhysics NewEngine(FixtureWorld world, Vec3d at, PhysicsConditions? conditions = null)
    {
        var engine = new PlayerPhysics(world, PhysicsProfile.Modern);
        engine.SetConditions(conditions ?? PhysicsConditions.Default);
        engine.Reset(at, YawEast, 0f);
        return engine;
    }

    /// <summary>From the top face, holding sneak must let the body reach the pad.</summary>
    [Fact]
    public void SneakingFromTheTopFace_ReachesTheColumnsFoot()
    {
        var engine = NewEngine(ScaffoldWorld(), new Vec3d(650.5, ColumnTop, 74.5));
        var sneak = new MovementInput { Sneak = true };

        int landed = -1;
        for (int tick = 0; tick < 200 && landed < 0; tick++)
        {
            engine.Step(sneak);
            if (engine.State.OnGround && engine.State.Position.Y <= PadTop + 1e-9)
                landed = tick;

        }

        Assert.True(
            landed >= 0,
            $"the body never reached the pad; y={engine.State.Position.Y:F6}, onGround={engine.State.OnGround}");
        Assert.Equal(PadTop, engine.State.Position.Y, 6);
    }

    /// <summary>The rate, stated as a number rather than as "it sinks". Vanilla's clamp is <c>max(delta.y, -0.15F)</c>, so once gravity has saturated it the body falls exactly <c>ClimbMaxSpeed</c> a tick and not a fraction more.</summary>
    [Fact]
    public void SneakingInsideTheColumn_SinksAtTheClimbClamp()
    {
        var engine = NewEngine(ScaffoldWorld(), new Vec3d(650.5, ColumnTop, 74.5));
        var sneak = new MovementInput { Sneak = true };

        // Two ticks to saturate the clamp, then the steady state.
        engine.Step(sneak);
        engine.Step(sneak);

        for (int tick = 0; tick < 20; tick++)
        {
            double before = engine.State.Position.Y;
            engine.Step(sneak);
            double step = before - engine.State.Position.Y;
            Assert.True(
                Math.Abs(step - PhysicsConstants.ClimbMaxSpeed) < 1e-9,
                $"tick {tick} sank {step:F6}, not {PhysicsConstants.ClimbMaxSpeed:F6} (y={engine.State.Position.Y:F6})");
        }
    }

    /// <summary>Confirms that the exemption is scaffolding-specific: holding sneak still pins a body on a ladder.</summary>
    [Fact]
    public void SneakingOnALadder_IsStillPinned()
    {
        var engine = NewEngine(LadderWorld(), new Vec3d(650.5, 103.0, 74.5));
        var sneak = new MovementInput { Sneak = true };

        for (int tick = 0; tick < 60; tick++)
            engine.Step(sneak);

        Assert.Equal(103.0, engine.State.Position.Y, 6);
    }

    /// <summary>The same ladder column with no input at all: the -0.15 clamp takes the body to the floor. This is the second half of the control - it proves the ladder row above is pinned BY the sneak and not by the fixture being inert.</summary>
    [Fact]
    public void ALadderWithNoInput_StillSlides()
    {
        var engine = NewEngine(LadderWorld(), new Vec3d(650.5, 103.0, 74.5));

        for (int tick = 0; tick < 60; tick++)
            engine.Step(default);

        Assert.Equal(PadTop, engine.State.Position.Y, 6);
    }

    /// <summary>Releasing sneak mid-descent must park the body on the next plate because the shape returns as as soon as the player stops descending.</summary>
    [Fact]
    public void ReleasingSneakMidDescent_ParksOnTheNextPlate()
    {
        var engine = NewEngine(ScaffoldWorld(), new Vec3d(650.5, ColumnTop, 74.5));
        var sneak = new MovementInput { Sneak = true };

        // Sink until the body is between two plates, well clear of both the top face and the pad.
        for (int tick = 0; tick < 200 && engine.State.Position.Y > 103.5; tick++)
            engine.Step(sneak);

        Assert.True(
            engine.State.Position.Y < ColumnTop && engine.State.Position.Y > PadTop,
            $"the setup did not get the body inside the column: y={engine.State.Position.Y:F6}");

        for (int tick = 0; tick < 60; tick++)
            engine.Step(default);

        Assert.Equal(103.0, engine.State.Position.Y, 6);
        Assert.True(engine.State.OnGround, "the body did not settle on the plate below it");
    }

    /// <summary>Swift sneak does not change the descent rate, and this pins it so a future attribute change cannot silently alter it.</summary>
    /// <remarks>The reason is narrower than "a scaffolding descent presses no directional input": once <c>ClimbTemplate</c> presses <c>Forward</c> and <c>Sneak</c> together to recentre, the factor does scale the HORIZONTAL recentre of a scaffolding descent, and swift sneak makes that faster. What it cannot touch is the vertical rate, because that is <c>max(delta.y, -0.15F)</c>, a velocity clamp, and <c>MapInput</c> scales only raw horizontal input.</remarks>
    [Theory]
    [InlineData(0.3f)]
    [InlineData(0.45f)]
    [InlineData(0.6f)]
    [InlineData(0.75f)]
    [InlineData(1.0f)]
    public void SwiftSneakDoesNotChangeTheDescentRate(float sneakingSpeedFactor)
    {
        PhysicsConditions conditions = PhysicsConditions.Default with { SneakingSpeedFactor = sneakingSpeedFactor };
        var engine = NewEngine(ScaffoldWorld(), new Vec3d(650.5, ColumnTop, 74.5), conditions);
        var sneak = new MovementInput { Sneak = true };

        int landed = -1;
        for (int tick = 0; tick < 200 && landed < 0; tick++)
        {
            engine.Step(sneak);
            if (engine.State.OnGround && engine.State.Position.Y <= PadTop + 1e-9)
                landed = tick;

        }

        Assert.Equal(34, landed);
    }
}
