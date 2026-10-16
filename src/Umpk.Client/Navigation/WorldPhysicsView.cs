using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Physics;

namespace Umpk.Client.Navigation;

/// <summary>Adapts the tracked <see cref="Umpk.Game.World.World"/> to <see cref="IPhysicsWorldView"/> for the local-player physics engine. Collision shapes come from an <see cref="IBlockShapeSource"/>: the version's generated shape tables by default, or whatever the host injected. Entity colliders are not collected by default; the client can extend this later from the entity tracker.</summary>
internal sealed class WorldPhysicsView(Umpk.Game.World.World world, IBlockShapeSource shapes, WorldBorderContainmentEra borderEra) : IPhysicsWorldView
{
    private readonly Umpk.Game.World.World _world = world;
    private readonly IBlockShapeSource _shapes = shapes;
    private readonly WorldBorderContainmentEra _borderEra = borderEra;

    public BlockState GetBlock(BlockPos pos) => _world.GetBlock(pos);

    public ReadOnlySpan<Aabb> GetCollisionShapes(BlockState state) => _shapes.GetCollisionShapes(state);

    public bool IsChunkLoaded(BlockPos pos) => _world.GetColumn(pos) is not null;

    public bool CollidesWithBorder(in Aabb box, Vec3d sourcePosition) =>
        _world.Border.CollidesWith(box, sourcePosition, _borderEra);

    public void CollectEntityColliders(in Aabb region, ICollection<Aabb> into)
    {
        // Entity-vs-entity collision is optional and off by default.
    }
}
