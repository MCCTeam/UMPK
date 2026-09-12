using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>The crouch factor that scales raw movement input is a value, not a fixed <c>0.3f</c>, because swift sneak replaces it rather than adding a second multiplier.</summary>
/// <remarks>
/// <para>In 1.19-1.20.6, input handling scales both movement impulses by <c>clamp(0.3F + 0.15F * swiftSneakLevel, 0.0F, 1.0F)</c>.</para>
/// <para>From 1.21 the same number is the <c>SNEAKING_SPEED</c> attribute, narrowed to float, with <c>SNEAKING_SPEED</c> ranging from <c>0.3</c> to <c>1.0</c> and swift sneak contributing <c>perLevel(0.15F)</c> as <c>ADD_VALUE</c>. Identical arithmetic on both eras: <c>clamp(0.3 + 0.15*L, 0, 1)</c>, so L=1 gives 0.45, L=2 0.60, L=3 0.75.</para>
/// <para>The <c>(float)</c> narrowing is not cosmetic: resolved in double the 1.21 attribute gives 0.7500000178813935 at L=3, not 0.75, because the modifier amount is a <c>float</c> widened to <c>double</c>. That is why <see cref="PhysicsConditions.SneakingSpeedFactor"/> is a <c>float</c> and why the holder narrows at the read.</para>
/// </remarks>
public sealed class SneakingSpeedFactorTests
{
    /// <summary>Steady-state per-tick Z displacement of a sneak-walk at the default 0.3 factor on friction 0.6. This is the number the hardcoded literal produced and it must not move.</summary>
    private const double SteadySneakWalkAtPointThree = 0.064757725773957553;

    private static FixtureWorld StoneRunway() => new FixtureWorld().Floor(-5, 5, -5, 400, 63, BlockKind.Stone);

    private static PhysicsConditions WithFactor(float factor) =>
        PhysicsConditions.Default with { SneakingSpeedFactor = factor };

    /// <summary>The per-tick Z displacement once the friction recurrence has converged.</summary>
    private static double SteadyForwardSpeed(PhysicsConditions conditions, MovementInput input, int ticks = 60)
    {
        var engine = new PlayerPhysics(StoneRunway(), PhysicsProfile.Modern);
        engine.SetConditions(conditions);
        engine.Reset(new Vec3d(0.5, 64, 0.5), 0f, 0f);
        engine.Step(MovementInput.None);
        engine.Step(MovementInput.None);

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

    /// <summary>THE BYTE-IDENTITY PIN. The default factor is 0.3, and a sneak-walk at it lands on exactly the displacement the hardcoded <c>0.3f</c> produced, to twelve decimals. Every existing physics expectation is therefore unmoved for a session with no swift sneak equipped.</summary>
    [Fact]
    public void NoSwiftSneak_SneakWalk_IsByteIdenticalToTheOldLiteral()
    {
        Assert.Equal(0.3f, PhysicsConditions.Default.SneakingSpeedFactor);
        Assert.Equal(
            SteadySneakWalkAtPointThree,
            SteadyForwardSpeed(PhysicsConditions.Default, new MovementInput { Forward = true, Sneak = true }),
            12);
    }

    /// <summary>The engine scales by the PUSHED factor, not by 0.3. The displacement is linear in the factor because the crouch scaling happens on the raw impulse, before friction, so each level's speed is the 0.3 speed times <c>factor / 0.3</c> - and every one of these is far outside anything a rounding argument covers.</summary>
    [Theory]
    [InlineData(0.45f, 0.097136575532158709)]  // swift sneak I
    [InlineData(0.60f, 0.12951545154791511)]   // swift sneak II
    [InlineData(0.75f, 0.16189430130611626)]   // swift sneak III
    [InlineData(1.0f, 0.21585906840815561)]    // the attribute's own maximum
    public void SneakInput_UsesConditionsFactor_NotHardcodedPointThree(float factor, double expected)
    {
        double actual = SteadyForwardSpeed(
            WithFactor(factor), new MovementInput { Forward = true, Sneak = true });

        Assert.Equal(expected, actual, 12);
        Assert.NotEqual(SteadySneakWalkAtPointThree, actual, 6);
    }

    /// <summary>The control: an ordinary walk is untouched by the factor, to twelve decimals, so the field cannot leak out of the crouch branch. A regression that applied it unconditionally would show up here before it showed up anywhere else.</summary>
    [Theory]
    [InlineData(0.3f)]
    [InlineData(0.75f)]
    public void WalkSpeed_IsUnaffectedByTheSneakingSpeedFactor(float factor)
        => Assert.Equal(
            SteadyForwardSpeed(PhysicsConditions.Default, new MovementInput { Forward = true }),
            SteadyForwardSpeed(WithFactor(factor), new MovementInput { Forward = true }),
            12);

    /// <summary>Swift sneak III makes a sneak-walk measurably faster over a real run of ticks, by the ratio <c>0.75 / 0.3 = 2.5</c>. Mirrors <c>WaterMovementEfficiencyPushTests.DepthStrider3Boots_MakesWaterTravelFaster_ThanNoGear</c>.</summary>
    [Fact]
    public void SwiftSneak3_MakesASneakWalkMeasurablyFaster()
    {
        double bare = SteadyForwardSpeed(
            PhysicsConditions.Default, new MovementInput { Forward = true, Sneak = true });
        double geared = SteadyForwardSpeed(
            WithFactor(0.75f), new MovementInput { Forward = true, Sneak = true });

        Assert.True(geared > bare, $"geared {geared} must exceed bare {bare}");
        Assert.Equal(2.5, geared / bare, 6);
    }

    /// <summary>A sneak-walk at the maximum factor is still SLOWER than a plain walk, which is the sanity bound vanilla's <c>[0,1]</c> attribute range guarantees. Swift sneak closes the gap; it never opens one.</summary>
    [Fact]
    public void EvenAtTheMaximumFactor_SneakingIsNotFasterThanWalking()
    {
        double walk = SteadyForwardSpeed(PhysicsConditions.Default, new MovementInput { Forward = true });
        double sneakAtMax = SteadyForwardSpeed(
            WithFactor(1.0f), new MovementInput { Forward = true, Sneak = true });

        Assert.True(sneakAtMax <= walk, $"sneak at factor 1.0 ({sneakAtMax}) must not exceed walk ({walk})");
    }
}
