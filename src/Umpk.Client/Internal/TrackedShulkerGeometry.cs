using Umpk.Client.Actions;
using Umpk.Game.Entities;
using Umpk.Geometry;

namespace Umpk.Client.Internal;

/// <summary>Connects synchronized shulker fields and eligible session ticks to the native lid geometry.</summary>
internal sealed class TrackedShulkerGeometry
{
    private static readonly Identifier ShulkerType = Identifier.Minecraft("shulker");
    private static readonly Identifier ScaleAttribute = Identifier.Minecraft("scale");
    private const int AttachFaceIndex = 16;
    private const int PeekIndex = 17;
    private const int PoseIndex = 6;
    private readonly VanillaShulkerGeometry _geometry;

    private TrackedShulkerGeometry(Entity entity, int protocol)
    {
        _geometry = new VanillaShulkerGeometry(protocol, entity.Position, Scale(entity), Direction.Down);
        ObserveMetadata(entity, AttachFaceIndex);
        ObserveMetadata(entity, PeekIndex);
        entity.Position = _geometry.Position;
    }

    public static bool TryCreate(Entity entity, int protocol, out TrackedShulkerGeometry? geometry)
    {
        if (entity.Type.Id == ShulkerType && protocol is >= 770 and <= 777)
        {
            geometry = new TrackedShulkerGeometry(entity, protocol);
            return true;
        }
        geometry = null;
        return false;
    }

    public bool TryGet(Entity entity, out Aabb bounds)
    {
        bounds = _geometry.Bounds;
        return (!entity.Metadata.TryGet(AttachFaceIndex, out MetadataValue face) || face.Kind == MetadataValueKind.Direction)
            && (!entity.Metadata.TryGet(PeekIndex, out MetadataValue peek) || peek.Kind == MetadataValueKind.Byte);
    }

    public void ObserveMetadata(Entity entity, int index)
    {
        if (index == AttachFaceIndex)
        {
            if (!entity.Metadata.TryGet(index, out MetadataValue value))
                _geometry.SetAttachFace(Direction.Down);
            else if (value.Kind == MetadataValueKind.Direction)
                _geometry.SetAttachFace(value.AsDirection());
        }
        else if (index == PeekIndex)
        {
            if (!entity.Metadata.TryGet(index, out MetadataValue value))
                _geometry.ObservePeek(0);
            else if (value.Kind == MetadataValueKind.Byte)
                _geometry.ObservePeek(value.AsByte());
        }
        else if (index == PoseIndex)
            ObservePosition(entity);
    }

    public void ObservePosition(Entity entity) => SetPosition(entity, entity.Vehicle is not null);

    private void SetPosition(Entity entity, bool passenger)
    {
        sbyte previous = _geometry.RawPeek;
        _geometry.SetPosition(entity.Position, passenger);
        entity.Position = _geometry.Position;
        if (_geometry.RawPeek != previous)
            entity.Metadata.Set(PeekIndex, MetadataValue.Byte(_geometry.RawPeek));
    }

    public void ObserveAttributes(Entity entity)
    {
        float scale = Scale(entity);
        if (scale == _geometry.Scale)
            return;
        _geometry.SetScale(scale);
        ObservePosition(entity);
    }

    public void ObserveMount(Entity entity)
    {
        _geometry.SetAttachFace(Direction.Down);
        entity.Metadata.Set(AttachFaceIndex, MetadataValue.Direction(Direction.Down));
        if (entity.Pose != EntityPose.Standing)
        {
            entity.Pose = EntityPose.Standing;
            entity.Metadata.Set(PoseIndex, MetadataValue.Pose(EntityPose.Standing));
            // Entity.startRiding changes pose after leaving the old vehicle and before attaching to the new one.
            SetPosition(entity, passenger: false);
        }
    }

    public void Tick(Entity entity)
    {
        sbyte previous = _geometry.RawPeek;
        _geometry.TickPeek(entity.Vehicle is not null);
        entity.Position = _geometry.Position;
        if (_geometry.RawPeek != previous)
            entity.Metadata.Set(PeekIndex, MetadataValue.Byte(_geometry.RawPeek));
    }

    public bool TryOpeningPush(Entity entity, out Aabb region, out Vec3d movement)
    {
        region = default;
        movement = default;
        return TryGet(entity, out _) && _geometry.TryOpeningPush(out region, out movement);
    }

    private static float Scale(Entity entity) => Math.Min((float)entity.Attributes.GetValueOrDefault(ScaleAttribute, 1.0), 3.0F);
}
