using Umpk.Client.Navigation;
using Umpk.Game.Blocks;
using Umpk.Game.World;
using Umpk.Geometry;

namespace Umpk.Client.Internal;

/// <summary>The measured legacy falling-block movement query. Unclassified shapes, callbacks and border transitions keep the result unknown.</summary>
internal sealed class LegacyFallingBlockCollision(World world, WorldPhysicsView view, int protocol = 107)
{
    private readonly List<Aabb> _colliders = [];

    public BlockState ReadBlock(BlockPos position)
        => LegacyFallingBlockProfile.UsesVoxelCollisionQuery(protocol) && (!view.IsChunkLoaded(position) || world.GetColumn(position) is null) ? default : view.GetBlock(position);

    public bool TryCollide(in Aabb box, Vec3d requested, out Vec3d displacement)
    {
        displacement = default;
        bool voxel = LegacyFallingBlockProfile.UsesVoxelCollisionQuery(protocol);
        if (voxel && (requested.X != 0 || requested.Z != 0 || !double.IsFinite(requested.Y)
            || !double.IsFinite(world.Border.MinX) || !double.IsFinite(world.Border.MaxX) || !double.IsFinite(world.Border.MinZ) || !double.IsFinite(world.Border.MaxZ)))
            return false;
        Aabb region = box.ExpandTowards(requested);
        if (!Finite(region) || region.XSize > 16 || region.YSize > 16 || region.ZSize > 16
            || region.MinY < int.MinValue + 2.0 || region.MaxY > int.MaxValue - 2.0
            || region.MinX <= world.Border.MinX + 2 || region.MaxX >= world.Border.MaxX - 2
            || region.MinZ <= world.Border.MinZ + 2 || region.MaxZ >= world.Border.MaxZ - 2)
            return false;
        _colliders.Clear();
        bool classic = LegacyFallingBlockProfile.UsesClassicCollisionQuery(protocol);
        Aabb query = voxel ? region.Inflate(1.0E-7) : region;
        int minX = (int)Math.Floor(query.MinX) - (classic ? 0 : 1), maxX = classic ? (int)Math.Floor(query.MaxX + 1) : (int)Math.Ceiling(query.MaxX) + 1;
        int minY = (int)Math.Floor(query.MinY) - 1, maxY = classic ? (int)Math.Floor(query.MaxY + 1) : (int)Math.Ceiling(query.MaxY) + 1;
        int minZ = (int)Math.Floor(query.MinZ) - (classic ? 0 : 1), maxZ = classic ? (int)Math.Floor(query.MaxZ + 1) : (int)Math.Ceiling(query.MaxZ) + 1;
        for (int x = minX; x < maxX; x++)
            for (int z = minZ; z < maxZ; z++)
            {
                int sides = classic ? 0 : (x == minX || x == maxX - 1 ? 1 : 0) + (z == minZ || z == maxZ - 1 ? 1 : 0);
                if (sides == 2)
                    continue;
                var columnCell = new BlockPos(x, 64, z);
                if (!view.IsChunkLoaded(columnCell))
                {
                    if (voxel)
                        return false;
                    continue;
                }
                if (voxel && world.GetColumn(columnCell) is null)
                    return false;
                for (int y = minY; y < maxY; y++)
                {
                    if (sides != 0 && (y == maxY - 1 || !voxel && y == minY))
                        continue;
                    var cell = new BlockPos(x, y, z);
                    BlockState block = view.GetBlock(cell);
                    if (!block.IsValid)
                        return false;
                    if (block.IsAir)
                        continue;
                    if (LegacyFallingBlockProfile.HasBaseCallbacksAndEmptyCollision(block.Block.NetworkId, protocol))
                    {
                        if (!view.GetCollisionShapes(block).IsEmpty)
                            return false;
                        continue;
                    }
                    if (!LegacyFallingBlockProfile.HasBaseCollisionAndCallbacks(block.Block.NetworkId, protocol))
                        return false;
                    ReadOnlySpan<Aabb> shapes = view.GetCollisionShapes(block);
                    if (shapes.Length != 1 || shapes[0] != Aabb.BlockAt(0, 0, 0))
                        return false;
                    Aabb collider = shapes[0].Move(x, y, z);
                    if (voxel || collider.Intersects(region))
                        _colliders.Add(collider);
                }
            }
        if (!view.TryCollectEntityColliders(region, _colliders))
            return false;
        if (voxel)
        {
            displacement = new Vec3d(0, ClipVoxelVertical(box, requested.Y), 0);
            return true;
        }
        Aabb current = box;
        double yMove = Clip(1, current, requested.Y);
        current = current.Move(0, yMove, 0);
        double xMove = Clip(0, current, requested.X);
        current = current.Move(xMove, 0, 0);
        double zMove = Clip(2, current, requested.Z);
        displacement = new Vec3d(xMove, yMove, zMove);
        return true;
    }

    // Original 1.13 cei.collide applies epsilon to voxel planes and transverse intervals. Only source-proven unit stone cubes participate in this vertical slice.
    private double ClipVoxelVertical(in Aabb moving, double distance)
    {
        const double epsilon = 1.0E-7;
        if (_colliders.Count == 0)
            return distance;
        if (Math.Abs(distance) < epsilon)
            return 0;
        foreach (Aabb obstacle in _colliders)
        {
            if (moving.MaxX - epsilon <= obstacle.MinX || moving.MinX + epsilon >= obstacle.MaxX
                || moving.MaxZ - epsilon <= obstacle.MinZ || moving.MinZ + epsilon >= obstacle.MaxZ)
                continue;
            if (distance > 0 && obstacle.MinY - moving.MaxY >= -epsilon)
                distance = Math.Min(distance, obstacle.MinY - moving.MaxY);
            else if (distance < 0 && obstacle.MaxY - moving.MinY <= epsilon)
                distance = Math.Max(distance, obstacle.MaxY - moving.MinY);
        }
        return distance;
    }

    private double Clip(int axis, in Aabb moving, double distance)
    {
        foreach (Aabb obstacle in _colliders)
        {
            int first = (axis + 1) % 3, second = (axis + 2) % 3;
            if (Maximum(moving, first) <= Minimum(obstacle, first) || Minimum(moving, first) >= Maximum(obstacle, first)
                || Maximum(moving, second) <= Minimum(obstacle, second) || Minimum(moving, second) >= Maximum(obstacle, second))
                continue;
            if (distance > 0 && Maximum(moving, axis) <= Minimum(obstacle, axis))
                distance = Math.Min(distance, Minimum(obstacle, axis) - Maximum(moving, axis));
            else if (distance < 0 && Minimum(moving, axis) >= Maximum(obstacle, axis))
                distance = Math.Max(distance, Maximum(obstacle, axis) - Minimum(moving, axis));
        }
        return distance;
    }

    private static double Minimum(in Aabb box, int axis) => axis == 0 ? box.MinX : axis == 1 ? box.MinY : box.MinZ;
    private static double Maximum(in Aabb box, int axis) => axis == 0 ? box.MaxX : axis == 1 ? box.MaxY : box.MaxZ;
    private static bool Finite(in Aabb box) => double.IsFinite(box.MinX) && double.IsFinite(box.MaxX) && double.IsFinite(box.MinY) && double.IsFinite(box.MaxY) && double.IsFinite(box.MinZ) && double.IsFinite(box.MaxZ);
}
