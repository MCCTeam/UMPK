using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>PhysicsSimulator determinism, purity, and PredictLanding.</summary>
public sealed class SimulatorTests
{
    private static PhysicsState StandingOn(FixtureWorld world, Vec3d pos) => new()
    {
        Position = pos,
        Velocity = Vec3d.Zero,
        Yaw = 0f,
        Pitch = 0f,
        OnGround = true,
    };

    [Fact]
    public void Run_MatchesStepByStepEngine()
    {
        var world = new FixtureWorld().Floor(-5, 5, -5, 200, 63, BlockKind.Stone);
        var start = StandingOn(world, new Vec3d(0.5, 64, 0.5));
        var conditions = PhysicsConditions.Default;

        var inputs = new MovementInput[30];
        for (int i = 0; i < inputs.Length; i++)
            inputs[i] = new MovementInput { Forward = true, Sprint = true };

        // Reference: a live engine loaded from the same start state stepping the same inputs.
        var engine = new PlayerPhysics(world, PhysicsProfile.Modern);
        engine.SetConditions(conditions);
        engine.LoadState(start);
        PhysicsState reference = default;
        foreach (MovementInput input in inputs)
            reference = engine.Step(input).State;

        PhysicsState simulated = PhysicsSimulator.Run(start, conditions, world, PhysicsProfile.Modern, inputs);

        Assert.Equal(reference.Position.X, simulated.Position.X, 10);
        Assert.Equal(reference.Position.Y, simulated.Position.Y, 10);
        Assert.Equal(reference.Position.Z, simulated.Position.Z, 10);
        Assert.Equal(reference.Velocity, simulated.Velocity);
    }

    [Fact]
    public void Run_IsDeterministic()
    {
        var world = new FixtureWorld().Floor(-5, 5, -5, 200, 63, BlockKind.Stone);
        var start = StandingOn(world, new Vec3d(0.5, 64, 0.5));
        var inputs = new MovementInput[50];
        Array.Fill(inputs, new MovementInput { Forward = true, Jump = true, Sprint = true });

        PhysicsState a = PhysicsSimulator.Run(start, PhysicsConditions.Default, world, PhysicsProfile.Modern, inputs);
        PhysicsState b = PhysicsSimulator.Run(start, PhysicsConditions.Default, world, PhysicsProfile.Modern, inputs);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Run_DoesNotMutateStartState()
    {
        var world = new FixtureWorld().Floor(-5, 5, -5, 200, 63, BlockKind.Stone);
        var start = StandingOn(world, new Vec3d(0.5, 64, 0.5));
        var inputs = new MovementInput[10];
        Array.Fill(inputs, new MovementInput { Forward = true });

        PhysicsSimulator.Run(start, PhysicsConditions.Default, world, PhysicsProfile.Modern, inputs);
        // start is a readonly struct; verify the value passed in is untouched.
        Assert.Equal(new Vec3d(0.5, 64, 0.5), start.Position);
    }

    [Fact]
    public void PredictLanding_FindsGroundAfterFall()
    {
        var world = new FixtureWorld().Floor(-5, 5, -5, 5, 63, BlockKind.Stone);
        var start = new PhysicsState
        {
            Position = new Vec3d(0.5, 80, 0.5),
            Velocity = Vec3d.Zero,
            OnGround = false,
        };

        LandingPrediction p = PhysicsSimulator.PredictLanding(start, PhysicsConditions.Default, world, PhysicsProfile.Modern, MovementInput.None, 200);
        Assert.True(p.Landed);
        Assert.True(p.TicksSimulated < 200);
        Assert.Equal(64.0, p.LandingPosition.Y, 3);
    }

    [Fact]
    public void PredictLanding_ReturnsUnlandedWhenNoGround()
    {
        var world = new FixtureWorld(); // no floor
        var start = new PhysicsState { Position = new Vec3d(0.5, 80, 0.5), OnGround = false };
        LandingPrediction p = PhysicsSimulator.PredictLanding(start, PhysicsConditions.Default, world, PhysicsProfile.Modern, MovementInput.None, 20);
        Assert.False(p.Landed);
        Assert.Equal(20, p.TicksSimulated);
    }
}
