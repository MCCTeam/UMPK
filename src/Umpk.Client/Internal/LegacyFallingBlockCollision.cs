using Umpk.Client.Navigation;
using Umpk.Game.Blocks;
using Umpk.Game.World;
using Umpk.Geometry;

namespace Umpk.Client.Internal;

/// <summary>The measured 1.9 falling-block movement query. Unclassified shapes, callbacks and border transitions keep the result unknown.</summary>
internal sealed class LegacyFallingBlockCollision(World world, WorldPhysicsView view)
{
    private readonly List<Aabb> _colliders = [];

    public BlockState ReadBlock(BlockPos position) => view.GetBlock(position);

    public bool TryCollide(in Aabb box, Vec3d requested, out Vec3d displacement)
    {
        displacement = default;
        Aabb region = box.ExpandTowards(requested);
        if (!Finite(region) || region.XSize > 16 || region.YSize > 16 || region.ZSize > 16
            || region.MinY < int.MinValue + 2.0 || region.MaxY > int.MaxValue - 2.0
            || region.MinX <= world.Border.MinX + 2 || region.MaxX >= world.Border.MaxX - 2
            || region.MinZ <= world.Border.MinZ + 2 || region.MaxZ >= world.Border.MaxZ - 2)
            return false;
        _colliders.Clear();
        int minX = (int)Math.Floor(region.MinX) - 1, maxX = (int)Math.Ceiling(region.MaxX) + 1;
        int minY = (int)Math.Floor(region.MinY) - 1, maxY = (int)Math.Ceiling(region.MaxY) + 1;
        int minZ = (int)Math.Floor(region.MinZ) - 1, maxZ = (int)Math.Ceiling(region.MaxZ) + 1;
        for (int x = minX; x < maxX; x++)
            for (int z = minZ; z < maxZ; z++)
            {
                int sides = (x == minX || x == maxX - 1 ? 1 : 0) + (z == minZ || z == maxZ - 1 ? 1 : 0);
                if (sides == 2 || !view.IsChunkLoaded(new BlockPos(x, 64, z)))
                    continue;
                for (int y = minY; y < maxY; y++)
                {
                    if (sides != 0 && (y == minY || y == maxY - 1))
                        continue;
                    var cell = new BlockPos(x, y, z);
                    BlockState block = view.GetBlock(cell);
                    if (!block.IsValid)
                        return false;
                    if (block.IsAir)
                        continue;
                    if (!LegacyFallingBlockProfile.HasBaseCollisionAndCallbacks(block.Block.NetworkId))
                        return false;
                    ReadOnlySpan<Aabb> shapes = view.GetCollisionShapes(block);
                    if (shapes.Length != 1 || shapes[0] != Aabb.BlockAt(0, 0, 0))
                        return false;
                    Aabb collider = shapes[0].Move(x, y, z);
                    if (collider.Intersects(region))
                        _colliders.Add(collider);
                }
            }
        if (!view.TryCollectEntityColliders(region, _colliders))
            return false;
        Aabb current = box;
        double yMove = Clip(1, current, requested.Y);
        current = current.Move(0, yMove, 0);
        double xMove = Clip(0, current, requested.X);
        current = current.Move(xMove, 0, 0);
        double zMove = Clip(2, current, requested.Z);
        displacement = new Vec3d(xMove, yMove, zMove);
        return true;
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
