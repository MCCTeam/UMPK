namespace Umpk.Game.Entities;

/// <summary>The closed set of value shapes a tier-1 entity-metadata entry can hold. Each kind names a distinct wire shape, not a semantic field. The many vanilla "variant" and "state" metadata serializers (cat/cow/pig variants, sniffer/armadillo/copper-golem states, painting variants, and so on) are all VarInt-shaped and map onto <see cref="VarInt"/>; the semantic meaning is recovered through tier-2 keys, never through a new kind here. This keeps the union finite and version-stable while remaining lossless for storage.</summary>
public enum MetadataValueKind
{
    /// <summary>Signed 8-bit value (also the carrier for boolean and bit-flag fields).</summary>
    Byte,

    /// <summary>A VarInt-encoded 32-bit integer.</summary>
    VarInt,

    /// <summary>A VarLong-encoded 64-bit integer.</summary>
    VarLong,

    /// <summary>A 32-bit float.</summary>
    Float,

    /// <summary>A UTF-8 string.</summary>
    String,

    /// <summary>A required text component (chat).</summary>
    Component,

    /// <summary>An optional text component (present flag + component); absent stored as null component.</summary>
    OptionalComponent,

    /// <summary>An item slot (placeholder; see <see cref="IMetadataSlot"/>). Empty slot stored as null.</summary>
    Slot,

    /// <summary>A boolean.</summary>
    Boolean,

    /// <summary>Three floats (pitch/yaw/roll rotation).</summary>
    Rotations,

    /// <summary>A block position.</summary>
    Position,

    /// <summary>An optional block position; absent stored as no value.</summary>
    OptionalPosition,

    /// <summary>A facing direction (VarInt-encoded on the wire).</summary>
    Direction,

    /// <summary>An optional UUID; absent stored as no value.</summary>
    OptionalUuid,

    /// <summary>A block-state id (VarInt on the wire, 0 meaning absent for the optional variant).</summary>
    BlockState,

    /// <summary>An optional block-state id; absent stored as no value.</summary>
    OptionalBlockState,

    /// <summary>An opaque NBT payload.</summary>
    Nbt,

    /// <summary>A particle payload (placeholder; the particle codec is owned by the protocol library).</summary>
    Particle,

    /// <summary>Villager data: three VarInts (type, profession, level).</summary>
    VillagerData,

    /// <summary>An optional VarInt (absent stored as no value; on the wire 0 means absent).</summary>
    OptionalVarInt,

    /// <summary>An entity pose.</summary>
    Pose,

    /// <summary>A global position: a dimension identifier plus a block position.</summary>
    GlobalPosition,

    /// <summary>An optional global position; absent stored as no value.</summary>
    OptionalGlobalPosition,

    /// <summary>A quaternion (four floats), used by display entities (1.19.4+).</summary>
    Quaternion,

    /// <summary>Three floats (a display-entity transform vector).</summary>
    Vector3,

    /// <summary>A list of particle payloads used by area-effect clouds since 1.20.5. Each element is an opaque particle payload owned by the protocol library.</summary>
    Particles,

    /// <summary>A resolvable player profile in the 26.2 metadata form: an optional resolved-or-partial game profile plus a player-skin patch.</summary>
    ResolvableProfile,
}
