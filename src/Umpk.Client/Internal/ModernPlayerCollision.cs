using Umpk.Client.Navigation;
using Umpk.Game.Entities;
using Umpk.Game.Items;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Physics;

namespace Umpk.Client.Internal;

/// <summary>Queries the tracked world for an ordinary unmounted remote player's native interpolation handler.</summary>
internal sealed class ModernPlayerCollision
{
    private readonly Entity _source;
    private readonly PhysicsCollisionQuery _query;

    public ModernPlayerCollision(Entity source, EntityStore entities, EntityMembershipState membership, EntityMotionState motion, EntityGeometryState geometry, IEntityGeometrySource geometrySource, int protocol, World world, IBlockShapeSource shapes)
    {
        _source = source;
        _query = new PhysicsCollisionQuery(new WorldPhysicsView(world, shapes, WorldBorderState.ContainmentEraForProtocol(protocol), PhysicsProfile.BlockCollisionQueryForProtocol(protocol),
            new EntityCollisionContext(source, entities, membership, motion, protocol, geometry, geometrySource)));
    }

    public bool NoCollision(Aabb box)
    {
        if (!double.IsFinite(box.MinX) || !double.IsFinite(box.MaxX) || !double.IsFinite(box.MinY) || !double.IsFinite(box.MaxY) || !double.IsFinite(box.MinZ) || !double.IsFinite(box.MaxZ)
            || box.MinX < int.MinValue + 2.0 || box.MaxX > int.MaxValue - 2.0 || box.MinY < int.MinValue + 2.0 || box.MaxY > int.MaxValue - 2.0 || box.MinZ < int.MinValue + 2.0 || box.MaxZ > int.MaxValue - 2.0)
            throw new UnknownEntityCollisionContextException();
        bool descending = false;
        if (_source.Metadata.TryResolve(EntityMetadataKeys.SharedFlags, out MetadataValue flags))
        {
            if (flags.Kind != MetadataValueKind.Byte)
                throw new UnknownEntityCollisionContextException();
            descending = (flags.AsByte() & 2) != 0;
        }
        bool boots = false;
        if (_source.TryGetEquipment(EquipmentSlot.Feet, out IMetadataSlot? equipped) && equipped is not null)
        {
            if (equipped is not ItemStack stack)
                throw new UnknownEntityCollisionContextException();
            boots = !stack.IsEmpty && stack.Item.Id == Identifier.Minecraft("leather_boots");
        }
        // Original RemotePlayer interpolation does not integrate a physical fall. This bounded owner starts with native constructor fallDistance zero and invalidates unowned rider displacement before querying.
        return _query.NoCollision(_source.Position, box, descending, fallDistance: 0, powderSnowWalkable: boots);
    }
}
