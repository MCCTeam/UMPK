using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>
/// The sprint movement-speed modifier, which the engine applies itself from the tick's input.
///
/// <para>The game keeps it on the attribute: sprinting installs and removes a transient <c>ADD_MULTIPLIED_TOTAL</c> modifier of <c>0.3F</c> keyed <c>minecraft:sprinting</c> and the resolved attribute is read back. UMPK has no self-side attribute map to hold a transient modifier on, and only the engine knows the per-tick sprint input, so the resolved value is split: the host pushes everything except sprint as <see cref="PhysicsConditions.BaseMovementSpeedAttribute"/> and the engine multiplies by <see cref="PhysicsConstants.SprintSpeedModifier"/> when the input holds Sprint.</para>
///
/// <para>The resolved sprint factor keeps executor movement at 0.28062 blocks per tick rather than the 0.21586 walking rate. This matches <c>ActionCosts.SprintOneBlock</c> at 3.5638 ticks per block and avoids pricing sprint moves at a rate the executor cannot deliver.</para>
/// </summary>
public sealed class SprintSpeedTests
{
    /// <summary>The resolved speed for a sprinting player at the default 0.1 attribute: <c>(float)(0.1f * (1.0 + (double)0.3f))</c>. Spelled as the literal it lands on so a change in the fold order (float multiply instead of double, or 1.3f instead of 1 + 0.3f) is visible here rather than only in the tenth decimal of a displacement.</summary>
    private const float ResolvedSprintSpeedAtDefault = 0.13000001f;

    /// <summary>Steady-state per-tick displacement while sprinting on friction 0.6 at the default attribute, which is vanilla's 5.612 m/s. Derived analytically as <c>speed * 0.98 / (1 - 0.6 * 0.91)</c> from the resolved speed above; the engine has to land on it to twelve decimals.</summary>
    private const double SteadySprintDisplacement = 0.28061680662163102;

    /// <summary>The same for a walking player, which is vanilla's 4.317 m/s. This one must NOT move.</summary>
    private const double SteadyWalkDisplacement = 0.21585906840815539;

    private static PlayerPhysics Ground(
        FixtureWorld world, Vec3d start, PhysicsConditions conditions, PhysicsProfile? profile = null)
    {
        var engine = new PlayerPhysics(world, profile ?? PhysicsProfile.Modern);
        engine.SetConditions(conditions);
        engine.Reset(start, 0f, 0f);
        engine.Step(MovementInput.None);
        engine.Step(MovementInput.None);
        return engine;
    }

    /// <summary>The per-tick Z displacement once the friction recurrence has converged.</summary>
    private static double SteadyForwardSpeed(
        FixtureWorld world, PhysicsConditions conditions, MovementInput input,
        int ticks = 60, PhysicsProfile? profile = null)
    {
        PlayerPhysics engine = Ground(world, new Vec3d(0.5, 64, 0.5), conditions, profile);
        double lastZ = engine.State.Position.Z;
        double speed = 0;
        for (int i = 0; i < ticks; i++)
        {
            engine.Step(input);
            speed = engine.State.Position.Z - lastZ;
            lastZ = engine.State.Position.Z;
        }

        return speed;
    }

    private static FixtureWorld StoneRunway() => new FixtureWorld().Floor(-5, 5, -5, 400, 63, BlockKind.Stone);

    // Ground speed

    /// <summary>Holding Sprint reaches vanilla's sprint speed, 0.28062 blocks a tick (5.612 m/s), which is what <c>ActionCosts.SprintOneBlock</c> already charges.</summary>
    [Fact]
    public void SprintInput_ReachesVanillaSprintSpeed()
    {
        double sprint = SteadyForwardSpeed(
            StoneRunway(), PhysicsConditions.Default, new MovementInput { Forward = true, Sprint = true });

        Assert.Equal(SteadySprintDisplacement, sprint, 12);
    }

    /// <summary>The control: walking is untouched, to the same twelve decimals. A modifier that leaked into the no-sprint path would show up here before it showed up anywhere else.</summary>
    [Fact]
    public void WalkSpeed_IsUnchangedBySprintWork()
    {
        double walk = SteadyForwardSpeed(
            StoneRunway(), PhysicsConditions.Default, new MovementInput { Forward = true });

        Assert.Equal(SteadyWalkDisplacement, walk, 12);
    }

    /// <summary>The modifier multiplies the total, it does not add to it. <c>ADD_MULTIPLIED_TOTAL</c> at 0.3 on an attribute of 0.12 resolves to 0.156, not to 0.15; the two are 0.013 blocks a tick apart, which is far outside anything a rounding argument covers.</summary>
    [Fact]
    public void SprintModifier_IsMultipliedIntoTheTotal_NotAdded()
    {
        FixtureWorld world = StoneRunway();
        double sprintAtTwelve = SteadyForwardSpeed(
            world,
            PhysicsConditions.Default with { BaseMovementSpeedAttribute = 0.12f },
            new MovementInput { Forward = true, Sprint = true });

        // 0.12 * 1.3 resolved the way vanilla resolves it: (float)(0.12f * (1.0 + (double)0.3f)).
        double walkAtMultiplied = SteadyForwardSpeed(
            world,
            PhysicsConditions.Default with { BaseMovementSpeedAttribute = 0.15600000321865082f },
            new MovementInput { Forward = true });

        // What ADD_VALUE at 0.03 would have produced instead.
        double walkAtAdded = SteadyForwardSpeed(
            world,
            PhysicsConditions.Default with { BaseMovementSpeedAttribute = 0.15f },
            new MovementInput { Forward = true });

        Assert.Equal(walkAtMultiplied, sprintAtTwelve, 12);
        Assert.NotEqual(walkAtAdded, sprintAtTwelve, 6);
    }

    /// <summary>Both friction-influenced speed branches carry the modifier. UMPK's ground branch only scales by <c>0.21600002 / friction^3</c> when friction is strictly above 0.6, so stone (0.6) takes the unscaled return and ice (0.98) takes the scaled one. A modifier applied inside the scaled arm alone would leave stone, which is every ordinary block, running at walking speed.</summary>
    [Theory]
    [InlineData(BlockKind.Stone)]
    [InlineData(BlockKind.Ice)]
    public void SprintModifier_IsCarriedByBothFrictionArms(BlockKind floor)
    {
        // Ice retains 0.8918 of its speed a tick against stone's 0.546, so it needs several times as many ticks to converge to twelve decimals.
        const int Ticks = 400;
        double sprint = SteadyForwardSpeed(
            new FixtureWorld().Floor(-5, 5, -5, 400, 63, floor),
            PhysicsConditions.Default,
            new MovementInput { Forward = true, Sprint = true },
            Ticks);

        double walkAtResolved = SteadyForwardSpeed(
            new FixtureWorld().Floor(-5, 5, -5, 400, 63, floor),
            PhysicsConditions.Default with { BaseMovementSpeedAttribute = ResolvedSprintSpeedAtDefault },
            new MovementInput { Forward = true },
            Ticks);

        Assert.Equal(walkAtResolved, sprint, 12);
    }

    /// <summary>The modifier is identical on every era. Vanilla declares the same modifier, with the same amount and the same operation from 1.8.9 onward. The identifiers change, but each is a 0.3 total multiplier. The expectation is one literal for all eras, not a per-era table.</summary>
    [Theory]
    [InlineData(47)]    // 1.8
    [InlineData(110)]   // 1.9.4
    [InlineData(340)]   // 1.12.2
    [InlineData(393)]   // 1.13, the swimming update
    [InlineData(404)]   // 1.13.2
    [InlineData(477)]   // 1.14, the crawl pose
    [InlineData(578)]   // 1.15.2
    [InlineData(754)]   // 1.16.5
    [InlineData(770)]   // 1.21.5
    [InlineData(776)]   // 26.2
    public void SprintSpeed_IsIdenticalOnEveryWireLayout(int protocol)
    {
        double sprint = SteadyForwardSpeed(
            StoneRunway(),
            PhysicsConditions.Default,
            new MovementInput { Forward = true, Sprint = true },
            profile: PhysicsProfile.ForProtocol(protocol));

        Assert.Equal(SteadySprintDisplacement, sprint, 12);
    }

    // The branches that must NOT see it

    /// <summary>Airborne acceleration is a flat 0.025999999 for a sprinter and has never read the movement-speed attribute, so changing the attribute must change nothing off the ground. This is the guard against applying the modifier one branch too high in <c>GetFrictionInfluencedSpeed</c>, where it would double-count against the sprint air speed that was already correct.</summary>
    [Fact]
    public void AirSpeed_DoesNotReadTheAttribute_SprintingOrNot()
    {
        double Drift(float attribute, bool sprint)
        {
            var engine = new PlayerPhysics(new FixtureWorld(), PhysicsProfile.Modern);
            engine.SetConditions(PhysicsConditions.Default with { BaseMovementSpeedAttribute = attribute });
            engine.Reset(new Vec3d(0.5, 200, 0.5), 0f, 0f);
            double startZ = engine.State.Position.Z;
            for (int i = 0; i < 20; i++)
                engine.Step(new MovementInput { Forward = true, Sprint = sprint });

            return engine.State.Position.Z - startZ;
        }

        Assert.Equal(Drift(0.1f, sprint: true), Drift(0.5f, sprint: true), 15);
        Assert.Equal(Drift(0.1f, sprint: false), Drift(0.5f, sprint: false), 15);

        // And the sprint air speed itself is still a real difference, so the equalities above are not passing because everything went to zero.
        Assert.True(Drift(0.1f, sprint: true) > Drift(0.1f, sprint: false));
    }

    /// <summary>Creative flight doubles its movement factor when sprinting and likewise never reads the movement-speed attribute.</summary>
    [Fact]
    public void CreativeFlySpeed_DoesNotReadTheAttribute_SprintingOrNot()
    {
        double Drift(float attribute, bool sprint)
        {
            var engine = new PlayerPhysics(new FixtureWorld(), PhysicsProfile.Modern);
            engine.SetConditions(PhysicsConditions.Default with
            {
                BaseMovementSpeedAttribute = attribute,
                CreativeFlying = true,
                MayFly = true,
            });
            engine.Reset(new Vec3d(0.5, 200, 0.5), 0f, 0f);
            double startZ = engine.State.Position.Z;
            for (int i = 0; i < 20; i++)
                engine.Step(new MovementInput { Forward = true, Sprint = sprint });

            return engine.State.Position.Z - startZ;
        }

        Assert.Equal(Drift(0.1f, sprint: true), Drift(0.5f, sprint: true), 15);
        Assert.Equal(Drift(0.1f, sprint: false), Drift(0.5f, sprint: false), 15);
        Assert.True(Drift(0.1f, sprint: true) > Drift(0.1f, sprint: false));
    }

    // Water.

    /// <summary>The water-movement-efficiency blend reads the sprint-resolved speed rather than the base attribute. Reading the base value leaves a depth-strider sprint swim 30% slow.</summary>
    /// <remarks>
    /// <para>The measurement is ONE tick out of an identical settled state, not a steady-state run, because in water a sprinting player and a walking player do not stay comparable for longer than that. Vanilla's Fluid movement skips its downward correction entirely while sprinting when sprinting, so a sprinter in shallow water stops being pressed into the floor and becomes airborne: 1 tick in 200 against a walker's 200 in 200 at an unchanged Y of 64. The game then halves the efficiency for an airborne entity, so a multi-tick comparison silently ends up comparing a full blend against a half blend and can be made to pass or fail for a reason that has nothing to do with the speed term.</para>
    /// <para>A single tick has none of that: grounded state is whatever the shared settle left, the slow-down term is applied to the velocity only AFTER the move (so it cannot touch this tick's displacement), and the fluid-falling correction is a Y-only edit. The one thing that can move Z is the blended speed. The fixture is a sheet of water one block deep over stone so the feet are in water while the head is not, keeping <c>IsUnderWater</c> false and the swim pose and swim steer out of it.</para>
    /// </remarks>
    [Fact]
    public void WaterWalkerBlend_UsesTheSprintResolvedSpeed()
    {
        double OneTickZ(float attribute, bool sprint)
        {
            var world = new FixtureWorld()
                .Floor(-5, 5, -5, 200, 63, BlockKind.Stone)
                .Floor(-5, 5, -5, 200, 64, BlockKind.Water);
            var engine = new PlayerPhysics(world, PhysicsProfile.Modern);
            engine.SetConditions(PhysicsConditions.Default with
            {
                BaseMovementSpeedAttribute = attribute,
                WaterMovementEfficiency = 1.0f,
            });
            engine.Reset(new Vec3d(0.5, 64, 0.5), 0f, 0f);

            // Settle with no input, which is identical on both sides: the attribute cannot matter while MoveRelative is scaling a zero input, and Sprint is a per-tick input bit, not a latch.
            engine.Step(MovementInput.None);
            engine.Step(MovementInput.None);
            Assert.True(engine.State.OnGround, "the settle left the player off the floor; the blend would be halved");
            Assert.False(engine.State.IsUnderWater, "the head went under; the swim pose would differ between the runs");

            double before = engine.State.Position.Z;
            engine.Step(new MovementInput { Forward = true, Sprint = sprint });
            return engine.State.Position.Z - before;
        }

        // Sprinting at the default 0.1 must blend in 0.13000001, which is what a walker carrying an already-resolved 0.13000001 blends in.
        Assert.Equal(OneTickZ(ResolvedSprintSpeedAtDefault, sprint: false), OneTickZ(0.1f, sprint: true), 15);

        // Two-sided: it must not still be blending in the bare 0.1.
        Assert.NotEqual(OneTickZ(0.1f, sprint: false), OneTickZ(0.1f, sprint: true), 6);
    }
}
