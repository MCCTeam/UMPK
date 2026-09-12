using Umpk.Geometry;

namespace Umpk.Physics;

/// <summary>The outcome of <see cref="PhysicsSimulator.PredictLanding"/>: whether the player came to rest on the ground within the tick budget, where, and the state at that moment.</summary>
public readonly record struct LandingPrediction
{
    /// <summary>Whether the player was on the ground when simulation stopped.</summary>
    public bool Landed { get; init; }

    /// <summary>The number of ticks simulated before stopping.</summary>
    public int TicksSimulated { get; init; }

    /// <summary>The final position.</summary>
    public Vec3d LandingPosition { get; init; }

    /// <summary>The full final state.</summary>
    public PhysicsState FinalState { get; init; }
}
