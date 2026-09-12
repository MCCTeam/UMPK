using Umpk.Geometry;

namespace Umpk.Physics;

/// <summary>Pure forward simulation over the physics core. Because <see cref="PhysicsState"/> is a value snapshot and the engine steps deterministically, cloning is a struct copy. A fresh <see cref="PlayerPhysics"/> is created per call so the simulation never disturbs a live engine.</summary>
public static class PhysicsSimulator
{
    /// <summary>Runs <paramref name="inputs"/> from <paramref name="start"/> and returns the final state.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> or <paramref name="profile"/> is null.</exception>
    public static PhysicsState Run(
        in PhysicsState start,
        in PhysicsConditions conditions,
        IPhysicsWorldView world,
        PhysicsProfile profile,
        ReadOnlySpan<MovementInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(profile);

        var engine = new PlayerPhysics(world, profile);
        engine.SetConditions(conditions);
        engine.LoadState(start);

        PhysicsState state = start;
        for (int i = 0; i < inputs.Length; i++)
            state = engine.Step(inputs[i]).State;

        return state;
    }

    /// <summary>Steps <paramref name="heldInput"/> until the player is on the ground or the tick budget runs out, returning where and whether it landed.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> or <paramref name="profile"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxTicks"/> is negative.</exception>
    public static LandingPrediction PredictLanding(
        in PhysicsState start,
        in PhysicsConditions conditions,
        IPhysicsWorldView world,
        PhysicsProfile profile,
        MovementInput heldInput,
        int maxTicks)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentOutOfRangeException.ThrowIfNegative(maxTicks);

        var engine = new PlayerPhysics(world, profile);
        engine.SetConditions(conditions);
        engine.LoadState(start);

        PhysicsState state = start;
        for (int tick = 1; tick <= maxTicks; tick++)
        {
            state = engine.Step(heldInput).State;
            if (state.OnGround)
                return new LandingPrediction
                {
                    Landed = true,
                    TicksSimulated = tick,
                    LandingPosition = state.Position,
                    FinalState = state,
                };

        }

        return new LandingPrediction
        {
            Landed = state.OnGround,
            TicksSimulated = maxTicks,
            LandingPosition = state.Position,
            FinalState = state,
        };
    }
}
