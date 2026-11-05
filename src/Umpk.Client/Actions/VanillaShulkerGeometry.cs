using Umpk.Client.Internal;
using Umpk.Geometry;

namespace Umpk.Client.Actions;

/// <summary>Caller-owned shulker geometry phases. The session owner supplies metadata, scale and eligible peek ticks.</summary>
internal sealed class VanillaShulkerGeometry
{
    private readonly ShulkerGeometryProfile _profile;
    private bool _hasTicked;

    public VanillaShulkerGeometry(int protocol, Vec3d position, float scale, Direction attachFace)
    {
        _profile = ShulkerGeometryProfile.ForProtocol(protocol);
        Scale = scale;
        AttachFace = attachFace;
        SetPosition(position);
    }

    public Vec3d Position { get; private set; }
    public Aabb Bounds { get; private set; }
    public Direction AttachFace { get; private set; }
    public float Scale { get; private set; }
    public sbyte RawPeek { get; private set; }
    public float Peek { get; private set; }
    public float PreviousPeek { get; private set; }
    public float PhysicalPeek => .5F - VanillaTrigonometry.SinCos((.5F + Peek) * MathF.PI, _profile.WideTrigIndex).Sin * .5F;
    public float Width => Scale;
    public float Height => _profile.OpeningFloorDimensions && AttachFace == Direction.Down && Peek > 0
        ? (1F + Peek) * Scale
        : Scale;

    public void ObservePeek(sbyte value) => RawPeek = value;

    public void SetScale(float value)
    {
        Scale = value;
        RefreshBounds();
    }

    public void SetAttachFace(Direction value)
    {
        AttachFace = value;
        RefreshBounds();
    }

    public void SetPosition(Vec3d value, bool passenger = false)
    {
        var next = passenger ? value : new Vec3d(Math.Floor(value.X) + .5, Math.Floor(value.Y + .5), Math.Floor(value.Z) + .5);
        Vec3d previous = Position;
        Position = next;
        RefreshBounds();
        if (_hasTicked && (Math.Floor(next.X) != Math.Floor(previous.X) || Math.Floor(next.Y) != Math.Floor(previous.Y) || Math.Floor(next.Z) != Math.Floor(previous.Z)))
            RawPeek = 0;
    }

    public bool TickPeek(bool passenger = false)
    {
        _hasTicked = true;
        PreviousPeek = Peek;
        float target = RawPeek * .01F;
        if (Peek == target)
            return false;
        Peek = Peek > target
            ? Clamp(Peek - .05F, target, 1F)
            : Clamp(Peek + .05F, 0F, target);
        SetPosition(Position, passenger);
        return true;
    }

    public bool TryOpeningPush(out Aabb region, out Vec3d movement)
    {
        float previous = .5F - VanillaTrigonometry.SinCos((.5F + PreviousPeek) * MathF.PI, _profile.WideTrigIndex).Sin * .5F;
        float current = PhysicalPeek;
        float distance = (current - previous) * Scale;
        region = default;
        movement = default;
        if (distance <= 0F || !float.IsFinite(distance))
            return false;
        Direction outward = AttachFace.Opposite();
        var box = new Aabb(-Scale * .5, 0, -Scale * .5, Scale * .5, Scale, Scale * .5)
            .ExpandTowards(outward.StepX() * (double)Math.Max(previous, current) * Scale,
                outward.StepY() * (double)Math.Max(previous, current) * Scale, outward.StepZ() * (double)Math.Max(previous, current) * Scale);
        double x = -outward.StepX() * (1D + Math.Min(previous, current)) * Scale;
        double y = -outward.StepY() * (1D + Math.Min(previous, current)) * Scale;
        double z = -outward.StepZ() * (1D + Math.Min(previous, current)) * Scale;
        // AABB.contract changes the named endpoint before moving the relative box into world space.
        region = new Aabb(box.MinX - Math.Min(x, 0), box.MinY - Math.Min(y, 0), box.MinZ - Math.Min(z, 0),
            box.MaxX - Math.Max(x, 0), box.MaxY - Math.Max(y, 0), box.MaxZ - Math.Max(z, 0)).Move(Position);
        movement = new Vec3d(distance * outward.StepX(), distance * outward.StepY(), distance * outward.StepZ());
        return true;
    }

    private void RefreshBounds()
    {
        Direction outward = AttachFace.Opposite();
        double peek = PhysicalPeek;
        // 1.21.5-client-decompiled net/minecraft/world/entity/monster/Shulker.java expands relative bounds before adding the world position. Reversing that order changes large-coordinate rounding.
        Bounds = new Aabb(-Scale * .5, 0, -Scale * .5, Scale * .5, Scale, Scale * .5)
            .ExpandTowards(outward.StepX() * peek * Scale, outward.StepY() * peek * Scale, outward.StepZ() * peek * Scale)
            .Move(Position);
    }

    private static float Clamp(float value, float minimum, float maximum) => value < minimum ? minimum : Math.Min(value, maximum);
}

internal readonly record struct ShulkerGeometryProfile(bool WideTrigIndex, bool OpeningFloorDimensions)
{
    // 1.21.11-client-decompiled net/minecraft/util/Mth.java widens sine indexing. 26.2-client-decompiled net/minecraft/world/entity/monster/Shulker.java adds the floor-attachment dimension term, separate from the physical lid distance.
    public static ShulkerGeometryProfile ForProtocol(int protocol) => protocol is >= 770 and <= 777
        ? new(protocol >= 774, protocol >= 776)
        : throw new ArgumentOutOfRangeException(nameof(protocol), "The native shulker phase profile has not been established for this protocol.");
}
