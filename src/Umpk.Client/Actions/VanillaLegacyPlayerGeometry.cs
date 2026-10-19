using Umpk.Client.Internal;
using Umpk.Geometry;

namespace Umpk.Client.Actions;

/// <summary>Caller-owned legacy player size and explicit lifecycle callbacks. The session owner supplies eligible phases and block clearance.</summary>
internal sealed class VanillaLegacyPlayerGeometry
{
    private readonly LegacyPlayerGeometryProfile _profile;
    private VanillaEntityBounds _bounds;

    public VanillaLegacyPlayerGeometry(int protocol, Vec3d position)
    {
        _profile = LegacyPlayerGeometryProfile.ForProtocol(protocol);
        _bounds = VanillaEntityBounds.Create(position, .6F, 1.8F);
    }

    public Vec3d Position => _bounds.Position;
    public Aabb Bounds => _bounds.Box;
    public float Width => _bounds.Width;
    public float Height => _bounds.Height;
    public bool Sleeping { get; private set; }
    public byte SharedFlags { get; private set; }
    public bool SpinAttack { get; private set; }

    public void SetPosition(Vec3d value) => _bounds = _bounds.AtPosition(value);
    public void ObserveFlags(byte value, bool spinAttack = false)
    {
        SharedFlags = value;
        SpinAttack = spinAttack;
    }

    public void Resize(float width, float height)
    {
        if (Width == width && Height == height)
            return;
        _bounds = _profile.RecenterOnShrink
            ? _bounds.Refresh(width, height, recenter: false)
            : new VanillaEntityBounds(Position, width, height, new Aabb(Bounds.MinX, Bounds.MinY, Bounds.MinZ, Bounds.MinX + width, Bounds.MinY + height, Bounds.MinZ + width));
    }

    /// <summary>Applies the original sleep callback when the bed cell is available and its horizontal facing is known.</summary>
    public void BeginSleep(BlockPos bed, Direction facing)
    {
        if (facing is < Direction.North or > Direction.East)
            throw new ArgumentOutOfRangeException(nameof(facing), "A sleeping player requires a horizontal bed facing.");
        Resize(.2F, .2F);
        int stepX = facing.StepX(), stepZ = facing.StepZ();
        float offsetX = _profile.VectorSleepOffsets ? .5F + stepX * .4F : stepX < 0 ? .1F : stepX > 0 ? .9F : .5F;
        float offsetZ = _profile.VectorSleepOffsets ? .5F + stepZ * .4F : stepZ < 0 ? .1F : stepZ > 0 ? .9F : .5F;
        // Native sleep adds the integer cell and offset as floats before widening to the entity's double position.
        SetPosition(new Vec3d(bed.X + offsetX, bed.Y + .6875F, bed.Z + offsetZ));
        Sleeping = true;
    }

    /// <summary>Applies the centered path when the original sleep callback's world-validity guard skips its bed read.</summary>
    public void BeginSleepWithoutBedRead(BlockPos bed)
    {
        Resize(.2F, .2F);
        SetPosition(new Vec3d(bed.X + .5F, bed.Y + .6875F, bed.Z + .5F));
        Sleeping = true;
    }

    /// <summary>Applies wake when the original bed is gone. Intact-bed exit selection belongs to the caller's world operation.</summary>
    public void WakeAfterBedRemoval()
    {
        Resize(.6F, 1.8F);
        Sleeping = false;
    }

    /// <summary>Applies wake after the caller updates the occupied bed state. The predicate supplies the native support and material rules; null means the original scan used its above-bed fallback.</summary>
    public BlockPos? WakeFromIntactBed(BlockPos bed, Direction facing, Func<BlockPos, bool> canStand)
    {
        ArgumentNullException.ThrowIfNull(canStand);
        TryWakeFromIntactBed(bed, facing, KnownCell, out BlockPos? exit);
        return exit;

        bool KnownCell(BlockPos cell, out bool result)
        {
            result = canStand(cell);
            return true;
        }
    }

    /// <summary>Returns false at the first unknown candidate. A true result with no exit means the original above-bed fallback is proven.</summary>
    public bool TryWakeFromIntactBed(BlockPos bed, Direction facing, LegacyBedExitPredicate canStand, out BlockPos? exit)
    {
        ArgumentNullException.ThrowIfNull(canStand);
        if (facing is < Direction.North or > Direction.East)
            throw new ArgumentOutOfRangeException(nameof(facing), "A waking player requires a horizontal bed facing.");
        Resize(.6F, 1.8F);
        if (!TryFindBedExit(bed, facing, canStand, out exit))
            return false;
        BlockPos destination = exit ?? new BlockPos(bed.X, bed.Y + 1, bed.Z);
        SetPosition(new Vec3d(destination.X + .5F, destination.Y + .1F, destination.Z + .5F));
        Sleeping = false;
        return true;
    }

    private static bool TryFindBedExit(BlockPos bed, Direction facing, LegacyBedExitPredicate canStand, out BlockPos? exit)
    {
        exit = null;
        for (int area = 0; area <= 1; area++)
        {
            int firstX = bed.X - facing.StepX() * area - 1;
            int firstZ = bed.Z - facing.StepZ() * area - 1;
            for (int x = firstX; x <= firstX + 2; x++)
                for (int z = firstZ; z <= firstZ + 2; z++)
                {
                    var candidate = new BlockPos(x, bed.Y, z);
                    if (!canStand(candidate, out bool supported))
                        return false;
                    if (supported)
                    {
                        exit = candidate;
                        return true;
                    }
                }
        }
        return true;
    }

    /// <summary>Applies the death callback's size and explicit position operations. A subsequent size phase still evaluates the flags and sleeping state.</summary>
    public void HandleDeath()
    {
        Resize(.2F, .2F);
        SetPosition(Position);
    }

    public void ApplySizePhase(Func<Aabb, bool> noCollision)
        => TryApplySizePhase(noCollision, dimensionsKnown: true);

    /// <summary>When prior dimensions are uncertain, only a successful forced query proves the requested size. A rejected query retains uncertainty.</summary>
    public bool TryApplySizePhase(Func<Aabb, bool> noCollision, bool dimensionsKnown)
    {
        if (!_profile.HasSizePhase)
            return dimensionsKnown;
        ArgumentNullException.ThrowIfNull(noCollision);
        (float width, float height) = (SharedFlags & 128) != 0 ? (.6F, .6F)
            : Sleeping ? (.2F, .2F)
            : _profile.SwimmingAndSpin && ((SharedFlags & 16) != 0 || SpinAttack) ? (.6F, .6F)
            : (.6F, (SharedFlags & 2) != 0 ? 1.65F : 1.8F);
        if (dimensionsKnown && Width == width && Height == height)
            return true;
        var candidate = new Aabb(Bounds.MinX, Bounds.MinY, Bounds.MinZ, Bounds.MinX + width, Bounds.MinY + height, Bounds.MinZ + width);
        if (noCollision(candidate))
        {
            Resize(width, height);
            return true;
        }
        return dimensionsKnown;
    }
}

internal readonly record struct LegacyPlayerGeometryProfile(bool HasSizePhase, bool RecenterOnShrink, bool VectorSleepOffsets, bool SwimmingAndSpin, bool BoundedBedRead, bool DeathPiBeforeDivision, bool ClearsFireOnDeath)
{
    // Original EmptyChunk block-update listeners retain state only in the measured protocol-47 band; this is independent of the size-phase and bed-validity guards.
    public bool RetainsEmptyChunkWrites { get; init; }

    // Original 1.13.1/1.13.2 providers fill an existing real chunk or create one for either payload form; a full flag does not replace its entity lists.
    public bool RetainsColumnForChunkPayloads { get; init; }

    public static bool Supports(int protocol) => protocol is 47 or 107 or 108 or 109 or 110 or 210 or 315 or 316 or 335 or 338 or 340 or 393 or 401 or 404;

    // Original callbacks and 19,968 native size phases establish size/offset boundaries. The separate 27-release world/bed-availability campaign establishes the bed-read guard.
    public static LegacyPlayerGeometryProfile ForProtocol(int protocol) => protocol switch
    {
        47 => new(false, false, false, false, true, true, false) { RetainsEmptyChunkWrites = true },
        107 or 108 or 109 => new(true, false, false, false, true, false, false),
        110 or 210 => new(true, false, false, false, false, false, false),
        315 or 316 or 335 or 338 or 340 => new(true, true, true, false, false, false, true),
        393 => new(true, true, true, true, false, false, true),
        401 or 404 => new(true, true, true, true, false, false, true) { RetainsColumnForChunkPayloads = true },
        _ => throw new ArgumentOutOfRangeException(nameof(protocol), "The legacy player lifecycle profile has not been measured for this protocol."),
    };

    // Through 1.9.2, World.isBlockLoaded and direct block reads share the full coordinate-validity guard. From 1.9.3, the sleep availability check no longer has that guard.
    public bool SkipsBedRead(BlockPos bed) => BoundedBedRead && (bed.Y is < 0 or >= 256 || bed.X is < -30_000_000 or >= 30_000_000 || bed.Z is < -30_000_000 or >= 30_000_000);

    /// <summary>Computes the original player-death impulse from current rotation and the separately owned damage yaw.</summary>
    public Vec3d DeathVelocity(float yaw, float damageYaw)
    {
        float sum = damageYaw + yaw;
        float radians = DeathPiBeforeDivision ? sum * (float)Math.PI / 180F : sum * (float)(Math.PI / 180.0);
        (float cos, float sin) = VanillaTrigonometry.SinCos(radians, wideIndex: false);
        return new(-cos * .1F, .1F, -sin * .1F);
    }
}

internal delegate bool LegacyBedExitPredicate(BlockPos cell, out bool canStand);
