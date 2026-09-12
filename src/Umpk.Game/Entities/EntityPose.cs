namespace Umpk.Game.Entities;

/// <summary>The animation pose an entity is in. On the wire a pose arrives as a metadata VarInt whose value is this enum's numeric member; the semantic key <see cref="EntityMetadataKeys.Pose"/> resolves the raw value to this type. The set grows over versions, so unknown numeric values map to <see cref="Unknown"/> rather than throwing.</summary>
public enum EntityPose
{
    /// <summary>An unrecognized pose value (forward-compatibility fallback).</summary>
    Unknown = -1,

    /// <summary>Standing.</summary>
    Standing = 0,

    /// <summary>In the fall-flying (elytra) pose.</summary>
    FallFlying = 1,

    /// <summary>Sleeping.</summary>
    Sleeping = 2,

    /// <summary>Swimming.</summary>
    Swimming = 3,

    /// <summary>Riptide spin attack.</summary>
    SpinAttack = 4,

    /// <summary>Crouching (sneaking).</summary>
    Crouching = 5,

    /// <summary>Long-jumping (goat, 1.17+).</summary>
    LongJumping = 6,

    /// <summary>Dying.</summary>
    Dying = 7,

    /// <summary>Croaking (frog, 1.19+).</summary>
    Croaking = 8,

    /// <summary>Using tongue (frog, 1.19+).</summary>
    UsingTongue = 9,

    /// <summary>Sitting.</summary>
    Sitting = 10,

    /// <summary>Roaring (warden, 1.19+).</summary>
    Roaring = 11,

    /// <summary>Sniffing (warden/sniffer, 1.19+).</summary>
    Sniffing = 12,

    /// <summary>Emerging (warden, 1.19+).</summary>
    Emerging = 13,

    /// <summary>Digging (warden, 1.19+).</summary>
    Digging = 14,

    /// <summary>Sliding (camel/breeze, 1.20+).</summary>
    Sliding = 15,

    /// <summary>Shooting (breeze, 1.21+).</summary>
    Shooting = 16,

    /// <summary>Inhaling (breeze, 1.21+).</summary>
    Inhaling = 17,
}
