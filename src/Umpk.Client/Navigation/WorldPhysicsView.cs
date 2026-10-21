using Umpk.Client.Internal;
using Umpk.Game.Blocks;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Physics;

namespace Umpk.Client.Navigation;

/// <summary>Adapts the tracked <see cref="Umpk.Game.World.World"/> to <see cref="IPhysicsWorldView"/> for the local-player physics engine. Collision shapes come from an <see cref="IBlockShapeSource"/>: the version's generated shape tables by default, or whatever the host injected. Entity colliders are not collected by default; the client can extend this later from the entity tracker.</summary>
internal sealed class WorldPhysicsView(Umpk.Game.World.World world, IBlockShapeSource shapes, WorldBorderContainmentEra borderEra, BlockCollisionQueryEra collisionEra = BlockCollisionQueryEra.BoundingBoxes, EntityCollisionContext? entityContext = null) : IPhysicsWorldView
{
    private readonly Umpk.Game.World.World _world = world;
    private readonly IBlockShapeSource _shapes = shapes;
    private readonly WorldBorderContainmentEra _borderEra = borderEra;

    public BlockCollisionQueryEra CollisionQueryEra { get; } = collisionEra;

    public BlockCollisionGrid? GetCollisionGrid(BlockState state) => (_shapes as IBlockCollisionGridSource)?.GetCollisionGrid(state);

    public BlockState GetBlock(BlockPos pos) => entityContext is { Protocol: 107 } context && !context.Membership.HasColumn(pos)
        ? new BlockState(_world.BlockData, 0) : _world.GetBlock(pos);

    public ReadOnlySpan<Aabb> GetCollisionShapes(BlockState state) => _shapes.GetCollisionShapes(state);

    public bool IsChunkLoaded(BlockPos pos) => entityContext is { } context ? context.Membership.HasColumn(pos) : _world.GetColumn(pos) is not null;

    public bool CollidesWithBorder(in Aabb box, Vec3d sourcePosition) =>
        _world.Border.CollidesWith(box, sourcePosition, _borderEra);

    public void CollectEntityColliders(in Aabb region, ICollection<Aabb> into)
    {
        if (!TryCollectEntityColliders(region, into))
            throw new InvalidOperationException("The source entity's hard collision context is unknown.");
    }

    /// <summary>Retains unowned hard boxes as unknown. A proven placement or picking box is not automatically a native movement collider.</summary>
    internal bool TryCollectEntityColliders(in Aabb region, ICollection<Aabb> into)
    {
        if (entityContext is not { } context)
            return true;
        if (context.Protocol != 107 || !LegacyFallingBlockProfile.Supports(context.Source, context.Protocol))
            return false;
        foreach (Entity peer in context.Entities.All)
        {
            if (ReferenceEquals(peer, context.Source) || context.Motion.IsTrackedPlayer(peer) || LegacyFallingBlockProfile.HasNoHardPeerBox(peer.Type.Id))
                continue;
            bool? member = context.Membership.IsWorldQueryMember(peer);
            if (member == false)
                continue;
            if (member is null)
                return false;
            if (SharedRoot(context.Source, peer))
                continue;
            // The 1.9 boat and Shulker peer boxes need their own owned moving geometry. Their missing bounds cannot prove that a distant-looking packet position is irrelevant.
            return false;
        }
        return true;
    }

    private static bool SharedRoot(Entity first, Entity second)
    {
        int leftDepth = 0;
        for (Entity? left = first; left is not null && leftDepth++ < 64; left = left.Vehicle)
        {
            int rightDepth = 0;
            for (Entity? right = second; right is not null && rightDepth++ < 64; right = right.Vehicle)
                if (ReferenceEquals(left, right))
                    return true;
        }
        return false;
    }
}

/// <summary>The source actor and tracked native membership needed for a movement collision query.</summary>
internal sealed record EntityCollisionContext(Entity Source, EntityStore Entities, EntityMembershipState Membership, EntityMotionState Motion, int Protocol);
