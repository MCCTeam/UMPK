using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>
/// Era coverage for <see cref="WaterTravelEra"/>: the two constants in <c>PlayerPhysics.TravelInWater</c> that changed at 1.13 (protocol 340 to 393).
///
/// <para>On the legacy side, the horizontal water slow-down is a flat <c>0.8F</c> with no sprinting arm, and the vertical tail is a flat <c>motionY -= 0.02</c>. On the modern side (393+), horizontal slowdown is 0.9 while sprinting and the ordinary water slowdown otherwise, followed by the fluid-falling adjustment.</para>
///
/// <para>The legacy terminal sink rate is <c>vy = 0.8 * vy - 0.02</c> -> <c>-0.1</c> per tick, while the modern one is <c>vy = 0.8 * vy - gravity/16</c> -> <c>-0.025</c> per tick. Running the modern model on the legacy band sinks the client roughly 4x too slowly, and makes it not sink at all while sprinting.</para>
/// </summary>
public sealed class WaterTravelProfileTests
{
    // Deep still water, well clear of the fixture's edges, so every tick takes the water-travel path with no collision and no fluid-current push.
    private static PlayerPhysics NewWaterEngine(int protocol)
    {
        var world = new FixtureWorld().Fill(-3, 40, -3, 3, 90, 3, BlockKind.Water);
        var engine = new PlayerPhysics(world, PhysicsProfile.ForProtocol(protocol));
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(0.5, 70, 0.5), 0f, 0f);
        engine.SetRotation(0f, 0f);
        return engine;
    }

    private static double[] SinkVelocities(int protocol, bool sprint, int ticks)
    {
        PlayerPhysics engine = NewWaterEngine(protocol);
        var input = new MovementInput { Sprint = sprint };
        var result = new double[ticks];
        for (int i = 0; i < ticks; i++)
        {
            engine.Step(input);
            result[i] = engine.State.Velocity.Y;
        }

        return result;
    }

    // (a) LEGACY: the flat -0.02 sink, stated as vanilla's own two statements rather than by calling
    //     anything in the engine. `this.w *= 0.8F; this.w -= 0.02;`
    [Theory]
    [InlineData(47)]    // 1.8 - 1.8.9
    [InlineData(340)]   // 1.12.2, the last legacy protocol
    public void LegacyWaterTravel_SinksAtTheFlatVanillaRate(int protocol)
    {
        const int Ticks = 40;
        double[] actual = SinkVelocities(protocol, sprint: false, Ticks);

        // Vanilla's legacy water tail, transcribed: the Y damping is the float literal 0.8F and the sink is the double literal 0.02. Deliberately NOT PhysicsConstants.* on the damping, so a constant edited in the engine cannot silently move this expectation with it.
        double expected = 0.0;
        for (int i = 0; i < Ticks; i++)
        {
            expected = (expected * (double)0.8f) - 0.02;
            Assert.Equal(expected, actual[i]);
        }

        // The steady state of vy = 0.8*vy - 0.02 is -0.1: four times the modern -0.025. After 40 ticks the residual is 0.1 * 0.8^40 ~= 1.3e-5, hence 4 decimal places rather than exact.
        Assert.Equal(-0.1, actual[^1], 4);
    }

    // (b) LEGACY: no sprint distinction at all. Vanilla 1.8.4/1.9/1.12.1 never test isSprinting() in
    //     the water branch, so sprinting must not perturb a single tick of the trajectory.
    [Theory]
    [InlineData(47)]
    [InlineData(340)]
    public void LegacyWaterTravel_SprintMakesNoDifference(int protocol)
    {
        double[] walking = SinkVelocities(protocol, sprint: false, 40);
        double[] sprinting = SinkVelocities(protocol, sprint: true, 40);

        Assert.Equal(walking, sprinting);
    }

    // (c) MODERN: the sprint distinction is real from 1.13 on - getFluidFallingAdjustedMovement
    //     returns the movement untouched while sprinting, so a sprinting player does not sink.
    [Theory]
    [InlineData(393)]   // 1.13, the first sprint-aware protocol
    [InlineData(754)]   // 1.16.4 / 1.16.5
    public void ModernWaterTravel_KeepsTheSprintDistinction(int protocol)
    {
        double[] walking = SinkVelocities(protocol, sprint: false, 40);
        double[] sprinting = SinkVelocities(protocol, sprint: true, 40);

        Assert.NotEqual(walking, sprinting);
        Assert.All(sprinting, vy => Assert.Equal(0.0, vy));
        Assert.Equal(-0.025, walking[^1], 4);
    }

    // (d) MODERN ZERO-DIFF PIN. These literal outputs pin the modern water-travel band bit for bit while
    //     legacy behavior varies independently.
    public static TheoryData<int, bool, double[], double[]> ModernBaseline() => new()
    {
        // protocol, sprinting, velocity Y at ticks {1,2,3,10,40}, position Y at the same ticks
        {
            393, false,
            [-0.005, -0.009000000059604645, -0.012200000154972078, -0.02231564637011615, -0.024996678417947976],
            [70, 69.995, 69.9859999999404, 69.8615782236, 69.1249833399347]
        },
        {
            393, true,
            [0, 0, 0, 0, 0],
            [70, 70, 70, 70, 70]
        },
        {
            754, false,
            [-0.005, -0.009000000059604645, -0.012200000154972078, -0.02231564637011615, -0.024996678417947976],
            [70, 69.995, 69.9859999999404, 69.8615782236, 69.1249833399347]
        },
        {
            754, true,
            [0, 0, 0, 0, 0],
            [70, 70, 70, 70, 70]
        },
    };

    [Theory]
    [MemberData(nameof(ModernBaseline))]
    public void ModernWaterTravel_IsByteIdenticalToThePreGateEngine(
        int protocol, bool sprint, double[] expectedVy, double[] expectedY)
    {
        int[] sampledTicks = [1, 2, 3, 10, 40];
        PlayerPhysics engine = NewWaterEngine(protocol);
        var input = new MovementInput { Sprint = sprint };

        for (int tick = 1, sample = 0; tick <= 40; tick++)
        {
            engine.Step(input);
            if (tick != sampledTicks[sample])
                continue;

            Assert.Equal(expectedVy[sample], engine.State.Velocity.Y);
            Assert.Equal(expectedY[sample], engine.State.Position.Y);
            sample++;
        }
    }

    // (e) The profile breakpoint itself, so the boundary is pinned independently of the dataset (the
    //     dataset side is pinned by Umpk.Client.Tests.PhysicsProfileConformanceTests).
    [Theory]
    [InlineData(47, WaterTravelEra.Legacy)]
    [InlineData(107, WaterTravelEra.Legacy)]
    [InlineData(338, WaterTravelEra.Legacy)]
    [InlineData(340, WaterTravelEra.Legacy)]
    [InlineData(393, WaterTravelEra.SprintAware)]
    [InlineData(498, WaterTravelEra.SprintAware)]
    [InlineData(770, WaterTravelEra.SprintAware)]
    [InlineData(776, WaterTravelEra.SprintAware)]
    public void ForProtocol_PinsTheWaterTravelBreakpoint(int protocol, WaterTravelEra expected)
    {
        Assert.Equal(expected, PhysicsProfile.ForProtocol(protocol).WaterTravel);
    }
}
