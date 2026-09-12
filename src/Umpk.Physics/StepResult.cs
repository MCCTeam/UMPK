using Umpk.Game.Entities;

namespace Umpk.Physics;

/// <summary>Notable transitions produced by one <see cref="PlayerPhysics.Step"/>, surfaced so the host can raise events (landing, bounce, glide start/stop, pose change) without diffing states itself.</summary>
public readonly record struct StepEvents
{
    /// <summary>The player landed on the ground this tick (was airborne, now on ground).</summary>
    public bool Landed { get; init; }

    /// <summary>The fall distance at the moment of landing (0 when not landing).</summary>
    public double LandingFallDistance { get; init; }

    /// <summary>The player bounced off a slime block this tick.</summary>
    public bool Bounced { get; init; }

    /// <summary>Elytra gliding started this tick.</summary>
    public bool StartedGliding { get; init; }

    /// <summary>Elytra gliding stopped this tick.</summary>
    public bool StoppedGliding { get; init; }

    /// <summary>The pose changed this tick.</summary>
    public bool PoseChanged { get; init; }

    /// <summary>The previous pose when <see cref="PoseChanged"/> is set.</summary>
    public EntityPose PreviousPose { get; init; }

    /// <summary>An event set with no transitions.</summary>
    public static StepEvents None => default;
}

/// <summary>The result of one physics tick: the new <see cref="State"/> and the <see cref="Events"/> raised.</summary>
public readonly record struct StepResult(PhysicsState State, StepEvents Events);
