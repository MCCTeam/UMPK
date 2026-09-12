using Umpk.Game.Entities;
using Umpk.Geometry;

namespace Umpk.Physics;

/// <summary>The full physics snapshot. A value type so cloning for forward simulation is a struct copy. It is both the engine's exposed state (<see cref="PlayerPhysics.State"/>) and the unit <see cref="PhysicsSimulator"/> forwards.</summary>
public readonly record struct PhysicsState
{
    /// <summary>Feet position (block coordinates).</summary>
    public Vec3d Position { get; init; }

    /// <summary>Velocity (delta movement per tick).</summary>
    public Vec3d Velocity { get; init; }

    /// <summary>Facing yaw in degrees.</summary>
    public float Yaw { get; init; }

    /// <summary>Facing pitch in degrees.</summary>
    public float Pitch { get; init; }

    /// <summary>Whether the player is on the ground.</summary>
    public bool OnGround { get; init; }

    /// <summary>Whether the last move hit a horizontal wall.</summary>
    public bool HorizontalCollision { get; init; }

    /// <summary>Whether the last move hit a vertical surface.</summary>
    public bool VerticalCollision { get; init; }

    /// <summary>Accumulated fall distance (reset on ground/climb).</summary>
    public double FallDistance { get; init; }

    /// <summary>The current pose (standing/crouching/swimming/fall-flying).</summary>
    public EntityPose Pose { get; init; }

    /// <summary>The bounding box for the current position and pose.</summary>
    public Aabb BoundingBox { get; init; }

    /// <summary>Whether the feet or head are in water (or a bubble column).</summary>
    public bool InWater { get; init; }

    /// <summary>Whether the head is underwater.</summary>
    public bool IsUnderWater { get; init; }

    /// <summary>Whether the feet or head are in lava.</summary>
    public bool InLava { get; init; }

    /// <summary>Whether the player is against a climbable block (ladder/vine/scaffolding).</summary>
    public bool OnClimbable { get; init; }

    /// <summary>Whether the player is in the swimming state (sprinting underwater, not flying).</summary>
    public bool IsSwimming { get; init; }

    /// <summary>Whether the tick that produced this state was sprinting, i.e. whether its <see cref="MovementInput.Sprint"/> was held.</summary>
    /// <remarks>This is the engine's per-tick input bit, not the game's latched shared entity flag, which can survive a tick in which no key is pressed. UMPK's engine has no such latch because sprint reaches it only as input. A host that announces sprint to the server reads this and edge-triggers on it, which reproduces the game's edge-triggered sprint announcement for every input source that drives the engine.</remarks>
    public bool IsSprinting { get; init; }

    /// <summary>Whether the player is gliding with an elytra.</summary>
    public bool IsGliding { get; init; }

    /// <summary>The eye position for this state (feet + pose eye height).</summary>
    public Vec3d EyePosition => new(Position.X, Position.Y + EyeHeight, Position.Z);

    /// <summary>The eye height for the current pose.</summary>
    public double EyeHeight => Pose switch
    {
        EntityPose.Crouching => PhysicsConstants.PlayerCrouchEyeHeight,
        EntityPose.Swimming or EntityPose.FallFlying or EntityPose.SpinAttack => PhysicsConstants.PlayerSwimEyeHeight,
        _ => PhysicsConstants.PlayerStandingEyeHeight,
    };
}
