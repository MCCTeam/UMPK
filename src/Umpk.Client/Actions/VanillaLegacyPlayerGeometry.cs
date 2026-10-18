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

    /// <summary>Applies wake when the original bed is gone. Intact-bed exit selection belongs to the caller's world operation.</summary>
    public void WakeAfterBedRemoval()
    {
        Resize(.6F, 1.8F);
        Sleeping = false;
    }

    /// <summary>Applies the death callback's size and explicit position operations. A subsequent size phase still evaluates the flags and sleeping state.</summary>
    public void HandleDeath()
    {
        Resize(.2F, .2F);
        SetPosition(Position);
    }

    public void ApplySizePhase(Func<Aabb, bool> noCollision)
    {
        if (!_profile.HasSizePhase)
            return;
        ArgumentNullException.ThrowIfNull(noCollision);
        (float width, float height) = (SharedFlags & 128) != 0 ? (.6F, .6F)
            : Sleeping ? (.2F, .2F)
            : _profile.SwimmingAndSpin && ((SharedFlags & 16) != 0 || SpinAttack) ? (.6F, .6F)
            : (.6F, (SharedFlags & 2) != 0 ? 1.65F : 1.8F);
        if (Width == width && Height == height)
            return;
        var candidate = new Aabb(Bounds.MinX, Bounds.MinY, Bounds.MinZ, Bounds.MinX + width, Bounds.MinY + height, Bounds.MinZ + width);
        if (noCollision(candidate))
            Resize(width, height);
    }
}

internal readonly record struct LegacyPlayerGeometryProfile(bool HasSizePhase, bool RecenterOnShrink, bool VectorSleepOffsets, bool SwimmingAndSpin)
{
    // Original callbacks across 27 releases and 19,968 native size phases establish each boundary independently of packet IDs.
    public static LegacyPlayerGeometryProfile ForProtocol(int protocol) => protocol switch
    {
        47 => new(false, false, false, false),
        107 or 108 or 109 or 110 or 210 => new(true, false, false, false),
        315 or 316 or 335 or 338 or 340 => new(true, true, true, false),
        393 or 401 or 404 => new(true, true, true, true),
        _ => throw new ArgumentOutOfRangeException(nameof(protocol), "The legacy player lifecycle profile has not been measured for this protocol."),
    };
}
