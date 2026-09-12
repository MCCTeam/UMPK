using System.Globalization;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Game.Entities;

/// <summary>A single tier-1 entity-metadata value: a closed union over the wire shapes listed in <see cref="MetadataValueKind"/>. Construction is through the named factory methods (no ambiguous overloads, per RS0026/RS0027); reading is through the kind-checked accessors.</summary>
/// <remarks>Storage packs every fixed-size scalar into one 64-bit field plus two extra floats for the multi-float shapes, and keeps reference payloads (string, component, slot, nbt, particle, villager data, global position) in a single object slot. Absence for the optional kinds is a distinct state (<see cref="HasValue"/> is false) rather than a sentinel numeric.</remarks>
public readonly struct MetadataValue : IEquatable<MetadataValue>
{
    private readonly long _scalar;
    private readonly float _f1;
    private readonly float _f2;
    private readonly float _f3;
    private readonly object? _ref;
    private readonly bool _hasValue;

    private MetadataValue(MetadataValueKind kind, long scalar, float f1, float f2, float f3, object? reference, bool hasValue)
    {
        Kind = kind;
        _scalar = scalar;
        _f1 = f1;
        _f2 = f2;
        _f3 = f3;
        _ref = reference;
        _hasValue = hasValue;
    }

    /// <summary>The wire shape of this value.</summary>
    public MetadataValueKind Kind { get; }

    /// <summary>For the optional kinds (<see cref="MetadataValueKind.OptionalComponent"/>, <see cref="MetadataValueKind.OptionalPosition"/>, <see cref="MetadataValueKind.OptionalUuid"/>, <see cref="MetadataValueKind.OptionalBlockState"/>, <see cref="MetadataValueKind.OptionalVarInt"/>, <see cref="MetadataValueKind.OptionalGlobalPosition"/>, and <see cref="MetadataValueKind.Slot"/>), whether a value is present. Always true for the non-optional kinds.</summary>
    public bool HasValue => _hasValue;

    /// <summary>A signed-byte value (also used for boolean-as-byte and bitflag fields).</summary>
    public static MetadataValue Byte(sbyte value) => new(MetadataValueKind.Byte, value, 0, 0, 0, null, true);

    /// <summary>A VarInt integer value.</summary>
    public static MetadataValue VarInt(int value) => new(MetadataValueKind.VarInt, value, 0, 0, 0, null, true);

    /// <summary>A VarLong integer value.</summary>
    public static MetadataValue VarLong(long value) => new(MetadataValueKind.VarLong, value, 0, 0, 0, null, true);

    /// <summary>A float value.</summary>
    public static MetadataValue Float(float value) => new(MetadataValueKind.Float, 0, value, 0, 0, null, true);

    /// <summary>A boolean value.</summary>
    public static MetadataValue Boolean(bool value) => new(MetadataValueKind.Boolean, value ? 1 : 0, 0, 0, 0, null, true);

    /// <summary>A facing-direction value.</summary>
    public static MetadataValue Direction(Direction value) => new(MetadataValueKind.Direction, (long)value, 0, 0, 0, null, true);

    /// <summary>A block-state id value.</summary>
    public static MetadataValue BlockState(int stateId) => new(MetadataValueKind.BlockState, stateId, 0, 0, 0, null, true);

    /// <summary>A required block position.</summary>
    public static MetadataValue Position(BlockPos value) => new(MetadataValueKind.Position, 0, 0, 0, 0, value, true);

    /// <summary>A required text component.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static MetadataValue Component(Component value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(MetadataValueKind.Component, 0, 0, 0, 0, value, true);
    }

    /// <summary>A required UTF-8 string.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static MetadataValue String(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(MetadataValueKind.String, 0, 0, 0, 0, value, true);
    }

    /// <summary>An opaque NBT payload.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static MetadataValue Nbt(NbtTag value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(MetadataValueKind.Nbt, 0, 0, 0, 0, value, true);
    }

    /// <summary>A particle placeholder (opaque object owned by the protocol library).</summary>
    /// <exception cref="ArgumentNullException"><paramref name="particle"/> is null.</exception>
    public static MetadataValue Particle(object particle)
    {
        ArgumentNullException.ThrowIfNull(particle);
        return new(MetadataValueKind.Particle, 0, 0, 0, 0, particle, true);
    }

    /// <summary>A list of particle placeholders (opaque objects owned by the protocol library).</summary>
    /// <exception cref="ArgumentNullException"><paramref name="particles"/> is null.</exception>
    public static MetadataValue Particles(IReadOnlyList<object> particles)
    {
        ArgumentNullException.ThrowIfNull(particles);
        return new(MetadataValueKind.Particles, 0, 0, 0, 0, particles, true);
    }

    /// <summary>A resolvable player profile.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is null.</exception>
    public static MetadataValue ResolvableProfile(ResolvableProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new(MetadataValueKind.ResolvableProfile, 0, 0, 0, 0, profile, true);
    }

    /// <summary>Three-float rotations.</summary>
    public static MetadataValue Rotations(Rotations value) => new(MetadataValueKind.Rotations, 0, value.X, value.Y, value.Z, null, true);

    /// <summary>A display-entity three-float vector.</summary>
    public static MetadataValue Vector3(Vec3d value) => new(MetadataValueKind.Vector3, 0, (float)value.X, (float)value.Y, (float)value.Z, null, true);

    /// <summary>A quaternion.</summary>
    public static MetadataValue Quaternion(Quaternion value) => new(MetadataValueKind.Quaternion, 0, 0, 0, 0, value, true);

    /// <summary>Villager data (type/profession/level).</summary>
    public static MetadataValue VillagerData(VillagerData value) => new(MetadataValueKind.VillagerData, 0, 0, 0, 0, value, true);

    /// <summary>A required global position.</summary>
    public static MetadataValue GlobalPosition(GlobalPosition value) => new(MetadataValueKind.GlobalPosition, 0, 0, 0, 0, value, true);

    /// <summary>An entity pose.</summary>
    public static MetadataValue Pose(EntityPose value) => new(MetadataValueKind.Pose, (long)value, 0, 0, 0, null, true);

    /// <summary>An item slot; pass null for the empty slot.</summary>
    public static MetadataValue Slot(IMetadataSlot? slot) => new(MetadataValueKind.Slot, 0, 0, 0, 0, slot, true);

    /// <summary>An optional component; pass null for absent.</summary>
    public static MetadataValue OptionalComponent(Component? value) =>
        new(MetadataValueKind.OptionalComponent, 0, 0, 0, 0, value, value is not null);

    /// <summary>An optional block position; pass null for absent.</summary>
    public static MetadataValue OptionalPosition(BlockPos? value) =>
        new(MetadataValueKind.OptionalPosition, 0, 0, 0, 0, value is { } p ? p : null, value.HasValue);

    /// <summary>An optional UUID; pass null for absent.</summary>
    public static MetadataValue OptionalUuid(Guid? value) =>
        new(MetadataValueKind.OptionalUuid, 0, 0, 0, 0, value is { } g ? g : null, value.HasValue);

    /// <summary>An optional block-state id; pass null for absent.</summary>
    public static MetadataValue OptionalBlockState(int? stateId) =>
        new(MetadataValueKind.OptionalBlockState, stateId ?? 0, 0, 0, 0, null, stateId.HasValue);

    /// <summary>An optional VarInt; pass null for absent.</summary>
    public static MetadataValue OptionalVarInt(int? value) =>
        new(MetadataValueKind.OptionalVarInt, value ?? 0, 0, 0, 0, null, value.HasValue);

    /// <summary>An optional global position; pass null for absent.</summary>
    public static MetadataValue OptionalGlobalPosition(GlobalPosition? value) =>
        new(MetadataValueKind.OptionalGlobalPosition, 0, 0, 0, 0, value is { } gp ? gp : null, value.HasValue);

    /// <summary>Reads a <see cref="MetadataValueKind.Byte"/> value.</summary>
    public sbyte AsByte() => (sbyte)Require(MetadataValueKind.Byte)._scalar;

    /// <summary>Reads a <see cref="MetadataValueKind.VarInt"/> value.</summary>
    public int AsVarInt() => (int)Require(MetadataValueKind.VarInt)._scalar;

    /// <summary>Reads a <see cref="MetadataValueKind.VarLong"/> value.</summary>
    public long AsVarLong() => Require(MetadataValueKind.VarLong)._scalar;

    /// <summary>Reads a <see cref="MetadataValueKind.Float"/> value.</summary>
    public float AsFloat() => Require(MetadataValueKind.Float)._f1;

    /// <summary>Reads a <see cref="MetadataValueKind.Boolean"/> value.</summary>
    public bool AsBoolean() => Require(MetadataValueKind.Boolean)._scalar != 0;

    /// <summary>Reads a <see cref="MetadataValueKind.Direction"/> value.</summary>
    public Direction AsDirection() => (Direction)Require(MetadataValueKind.Direction)._scalar;

    /// <summary>Reads a <see cref="MetadataValueKind.BlockState"/> value.</summary>
    public int AsBlockState() => (int)Require(MetadataValueKind.BlockState)._scalar;

    /// <summary>Reads a <see cref="MetadataValueKind.Position"/> value.</summary>
    public BlockPos AsPosition() => (BlockPos)Require(MetadataValueKind.Position)._ref!;

    /// <summary>Reads a <see cref="MetadataValueKind.Component"/> value.</summary>
    public Component AsComponent() => (Component)Require(MetadataValueKind.Component)._ref!;

    /// <summary>Reads a <see cref="MetadataValueKind.String"/> value.</summary>
    public string AsString() => (string)Require(MetadataValueKind.String)._ref!;

    /// <summary>Reads a <see cref="MetadataValueKind.Nbt"/> value.</summary>
    public NbtTag AsNbt() => (NbtTag)Require(MetadataValueKind.Nbt)._ref!;

    /// <summary>Reads a <see cref="MetadataValueKind.Particle"/> placeholder.</summary>
    public object AsParticle() => Require(MetadataValueKind.Particle)._ref!;

    /// <summary>Reads a <see cref="MetadataValueKind.Particles"/> list of placeholders.</summary>
    public IReadOnlyList<object> AsParticles() => (IReadOnlyList<object>)Require(MetadataValueKind.Particles)._ref!;

    /// <summary>Reads a <see cref="MetadataValueKind.ResolvableProfile"/> value.</summary>
    public ResolvableProfile AsResolvableProfile() => (ResolvableProfile)Require(MetadataValueKind.ResolvableProfile)._ref!;

    /// <summary>Reads a <see cref="MetadataValueKind.Rotations"/> value.</summary>
    public Rotations AsRotations() { var v = Require(MetadataValueKind.Rotations); return new Rotations(v._f1, v._f2, v._f3); }

    /// <summary>Reads a <see cref="MetadataValueKind.Vector3"/> value.</summary>
    public Vec3d AsVector3() { var v = Require(MetadataValueKind.Vector3); return new Vec3d(v._f1, v._f2, v._f3); }

    /// <summary>Reads a <see cref="MetadataValueKind.Quaternion"/> value.</summary>
    public Quaternion AsQuaternion() => (Quaternion)Require(MetadataValueKind.Quaternion)._ref!;

    /// <summary>Reads a <see cref="MetadataValueKind.VillagerData"/> value.</summary>
    public VillagerData AsVillagerData() => (VillagerData)Require(MetadataValueKind.VillagerData)._ref!;

    /// <summary>Reads a <see cref="MetadataValueKind.GlobalPosition"/> value.</summary>
    public GlobalPosition AsGlobalPosition() => (GlobalPosition)Require(MetadataValueKind.GlobalPosition)._ref!;

    /// <summary>Reads a <see cref="MetadataValueKind.Pose"/> value.</summary>
    public EntityPose AsPose() => (EntityPose)Require(MetadataValueKind.Pose)._scalar;

    /// <summary>Reads a <see cref="MetadataValueKind.Slot"/> placeholder; null for the empty slot.</summary>
    public IMetadataSlot? AsSlot() => (IMetadataSlot?)Require(MetadataValueKind.Slot)._ref;

    /// <summary>Reads an <see cref="MetadataValueKind.OptionalComponent"/>; null when absent.</summary>
    public Component? AsOptionalComponent() => (Component?)Require(MetadataValueKind.OptionalComponent)._ref;

    /// <summary>Reads an <see cref="MetadataValueKind.OptionalPosition"/>; null when absent.</summary>
    public BlockPos? AsOptionalPosition() { var v = Require(MetadataValueKind.OptionalPosition); return v._hasValue ? (BlockPos)v._ref! : null; }

    /// <summary>Reads an <see cref="MetadataValueKind.OptionalUuid"/>; null when absent.</summary>
    public Guid? AsOptionalUuid() { var v = Require(MetadataValueKind.OptionalUuid); return v._hasValue ? (Guid)v._ref! : null; }

    /// <summary>Reads an <see cref="MetadataValueKind.OptionalBlockState"/>; null when absent.</summary>
    public int? AsOptionalBlockState() { var v = Require(MetadataValueKind.OptionalBlockState); return v._hasValue ? (int)v._scalar : null; }

    /// <summary>Reads an <see cref="MetadataValueKind.OptionalVarInt"/>; null when absent.</summary>
    public int? AsOptionalVarInt() { var v = Require(MetadataValueKind.OptionalVarInt); return v._hasValue ? (int)v._scalar : null; }

    /// <summary>Reads an <see cref="MetadataValueKind.OptionalGlobalPosition"/>; null when absent.</summary>
    public GlobalPosition? AsOptionalGlobalPosition() { var v = Require(MetadataValueKind.OptionalGlobalPosition); return v._hasValue ? (GlobalPosition)v._ref! : null; }

    private MetadataValue Require(MetadataValueKind kind)
    {
        if (Kind != kind)
            throw new InvalidOperationException(
                string.Format(CultureInfo.InvariantCulture, "Metadata value is of kind {0}, not {1}.", Kind, kind));

        return this;
    }

    /// <inheritdoc/>
    public bool Equals(MetadataValue other) =>
        Kind == other.Kind
        && _hasValue == other._hasValue
        && _scalar == other._scalar
        && _f1.Equals(other._f1) && _f2.Equals(other._f2) && _f3.Equals(other._f3)
        && Equals(_ref, other._ref);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is MetadataValue other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Kind, _hasValue, _scalar, _f1, _f2, _ref);

    /// <inheritdoc/>
    public override string ToString() => _hasValue
        ? string.Format(CultureInfo.InvariantCulture, "{0}", Kind)
        : string.Format(CultureInfo.InvariantCulture, "{0}(absent)", Kind);

    /// <summary>Value equality.</summary>
    public static bool operator ==(MetadataValue left, MetadataValue right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(MetadataValue left, MetadataValue right) => !left.Equals(right);
}
