using Umpk.Client.Actions;
using Umpk.Game.Entities;
using Umpk.Geometry;

namespace Umpk.Client.Internal;

/// <summary>Owns the measured legacy falling-block constructor box and preserves translations separately from explicit position setters.</summary>
internal sealed class TrackedFallingBlockGeometry
{
    private const float Size = .98F;
    private Aabb _box;
    public bool Known { get; private set; } = true;
    public Vec3d ObservedPosition { get; private set; }

    public TrackedFallingBlockGeometry(Entity entity, int protocol = 107)
    {
        entity.Position = entity.Position.Add(0, LegacyFallingBlockProfile.SpawnYOffset(protocol), 0);
        ObservePosition(entity);
    }

    public bool TryGet(out Aabb box)
    {
        box = _box;
        return Known;
    }

    public void ObservePosition(Entity entity)
    {
        _box = VanillaEntityBounds.Create(entity.Position, Size, Size).Box;
        ObservedPosition = entity.Position;
        Known &= double.IsFinite(entity.Position.X) && double.IsFinite(entity.Position.Y) && double.IsFinite(entity.Position.Z);
    }

    public void Move(Entity entity, Vec3d displacement)
    {
        _box = _box.Move(displacement);
        entity.Position = _box.GetBottomCenter();
        ObservedPosition = entity.Position;
    }

    public void Invalidate() => Known = false;
}
