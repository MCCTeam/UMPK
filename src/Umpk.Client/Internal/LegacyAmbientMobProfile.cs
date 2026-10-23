using Umpk.Client.Actions;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Geometry;

namespace Umpk.Client.Internal;

/// <summary>The source-bound 1.11 bat and squid slice. Adjacent releases retain their existing unknown geometry.</summary>
internal readonly record struct LegacyAmbientMobProfile(bool IsBat)
{
    public float Width => IsBat ? .5F : .8F;
    public float Height => IsBat ? .9F : .8F;
    public float DefaultHealth => IsBat ? 6F : 10F;
    public EntityGeometryDimensions Dimensions => new(Width, Height, true, true);

    public static Vec3d SpawnVelocity(int protocol, int typeId, short x, short y, short z, Vec3d fallback)
        => protocol == 315 && typeId is 65 or 94 ? new(x / 8000.0F, y / 8000.0F, z / 8000.0F) : fallback;

    public static bool TryGet(Entity entity, int protocol, out LegacyAmbientMobProfile profile)
    {
        profile = default;
        if (protocol != 315 || !entity.Type.Id.IsMinecraft || entity.Type.Id.Path is not ("bat" or "squid"))
            return false;
        profile = new(entity.Type.Id.Path == "bat");
        return true;
    }
}

/// <summary>A native cached box whose position setter is independent of the actor's later direct position assignments.</summary>
internal sealed class TrackedLegacyAmbientGeometry(Entity entity, LegacyAmbientMobProfile profile)
{
    private VanillaEntityBounds _bounds = VanillaEntityBounds.Create(entity.Position, profile.Width, profile.Height);
    public LegacyAmbientMobProfile Profile { get; } = profile;
    public bool Known { get; set; } = Finite(entity.Position);
    public Aabb Bounds => _bounds.Box;

    public void ObservePosition(Entity entity)
    {
        _bounds = _bounds.AtPosition(entity.Position);
        Known = Finite(entity.Position);
    }

    public void ObserveMovement(Entity entity, Aabb bounds)
    {
        _bounds = new(entity.Position, Profile.Width, Profile.Height, bounds);
    }

    private static bool Finite(Vec3d position)
        => double.IsFinite(position.X) && double.IsFinite(position.Y) && double.IsFinite(position.Z)
            && position.X is > int.MinValue and < int.MaxValue && position.Y is > int.MinValue and < int.MaxValue && position.Z is > int.MinValue and < int.MaxValue;
}
