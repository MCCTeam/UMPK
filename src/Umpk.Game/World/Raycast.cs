using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;

namespace Umpk.Game.World;

/// <summary>DDA voxel raycasting over the world. It honors block shapes when an <see cref="IBlockShapeSource"/> is supplied. Without a shape source, non-air blocks are treated as full cubes. Entity raycasts use an AABB slab test against candidate boxes ordered by distance.</summary>
public static class Raycast
{
    private const double Epsilon = 1.0E-7;

    /// <summary>Casts a ray from <paramref name="start"/> to <paramref name="end"/> through the world, stopping at the first block that the ray enters. When <paramref name="shapes"/> is null a non-air block is a full cube; otherwise the ray is clipped against the block's outline AABBs. Fluids are treated as non-solid unless <paramref name="includeFluids"/> is set.</summary>
    public static BlockHitResult CastBlock(World world, Vec3d start, Vec3d end, bool includeFluids = false, IBlockShapeSource? shapes = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (start.Equals(end))
            return BlockHitResult.Miss;

        // Bias the endpoints inward by a tiny epsilon so a ray on a block boundary resolves deterministically.
        double sx = Lerp(-Epsilon, start.X, end.X);
        double sy = Lerp(-Epsilon, start.Y, end.Y);
        double sz = Lerp(-Epsilon, start.Z, end.Z);
        double ex = Lerp(-Epsilon, end.X, start.X);
        double ey = Lerp(-Epsilon, end.Y, start.Y);
        double ez = Lerp(-Epsilon, end.Z, start.Z);

        int cx = FloorInt(sx), cy = FloorInt(sy), cz = FloorInt(sz);

        if (TryHit(world, new BlockPos(cx, cy, cz), start, end, includeFluids, shapes, out BlockHitResult first))
            return first;

        double dx = ex - sx, dy = ey - sy, dz = ez - sz;
        int stepX = Math.Sign(dx), stepY = Math.Sign(dy), stepZ = Math.Sign(dz);
        double invX = stepX == 0 ? double.MaxValue : stepX / dx;
        double invY = stepY == 0 ? double.MaxValue : stepY / dy;
        double invZ = stepZ == 0 ? double.MaxValue : stepZ / dz;
        double tX = invX * (stepX > 0 ? 1.0 - Frac(sx) : Frac(sx));
        double tY = invY * (stepY > 0 ? 1.0 - Frac(sy) : Frac(sy));
        double tZ = invZ * (stepZ > 0 ? 1.0 - Frac(sz) : Frac(sz));

        while (tX <= 1.0 || tY <= 1.0 || tZ <= 1.0)
        {
            if (tX < tY)
                if (tX < tZ)
                {
                    cx += stepX;
                    tX += invX;
                }
                else
                {
                    cz += stepZ;
                    tZ += invZ;
                }

            else if (tY < tZ)
            {
                cy += stepY;
                tY += invY;
            }
            else
            {
                cz += stepZ;
                tZ += invZ;
            }

            if (TryHit(world, new BlockPos(cx, cy, cz), start, end, includeFluids, shapes, out BlockHitResult hit))
                return hit;

        }

        return BlockHitResult.Miss;
    }

    /// <summary>Casts a ray against a set of candidate AABBs (entity bounding boxes), returning the closest one the ray enters within the segment. Entities are the entity module's model, so this takes their boxes as spans of <see cref="Aabb"/>.</summary>
    public static EntityHitResult CastEntities(Vec3d start, Vec3d end, ReadOnlySpan<Aabb> candidates)
    {
        Vec3d dir = end.Subtract(start);
        double bestT = double.PositiveInfinity;
        int bestIndex = -1;

        for (int i = 0; i < candidates.Length; i++)
            if (TryClip(candidates[i], start, dir, out double t) && t < bestT)
            {
                bestT = t;
                bestIndex = i;
            }

        if (bestIndex < 0)
            return EntityHitResult.Miss;

        Vec3d point = start.Add(dir.Scale(bestT));
        double distance = point.Subtract(start).Length();
        return EntityHitResult.FromHit(bestIndex, point, distance);
    }

    private static bool TryHit(World world, BlockPos pos, Vec3d rayStart, Vec3d rayEnd, bool includeFluids, IBlockShapeSource? shapes, out BlockHitResult result)
    {
        result = BlockHitResult.Miss;
        BlockState state = world.GetBlock(pos);
        if (state.IsAir)
            return false;

        if (!includeFluids && state.IsFluid)
            return false;

        Vec3d dir = rayEnd.Subtract(rayStart);

        if (shapes is null)
        {
            // Full-cube semantics: clip against the unit box at this position.
            Aabb cube = Aabb.BlockAt(pos.X, pos.Y, pos.Z);
            if (ClipWithFace(cube, rayStart, dir, out Vec3d point, out Direction face, out double t))
            {
                result = BlockHitResult.FromHit(pos, face, point, point.Subtract(rayStart).Length());
                return true;
            }

            // The DDA entered this voxel, so a full cube is always hit; guard anyway.
            return false;
        }

        ReadOnlySpan<Aabb> outline = shapes.GetOutlineShapes(state);
        if (outline.IsEmpty)
            return false;

        double bestT = double.PositiveInfinity;
        Vec3d bestPoint = Vec3d.Zero;
        Direction bestFace = Direction.Up;
        bool any = false;
        foreach (Aabb box in outline)
        {
            Aabb worldBox = box.Move(pos.X, pos.Y, pos.Z);
            if (ClipWithFace(worldBox, rayStart, dir, out Vec3d point, out Direction face, out double t) && t < bestT)
            {
                bestT = t;
                bestPoint = point;
                bestFace = face;
                any = true;
            }
        }

        if (!any)
            return false;

        result = BlockHitResult.FromHit(pos, bestFace, bestPoint, bestPoint.Subtract(rayStart).Length());
        return true;
    }

    // Slab clip returning the entry parameter t in [0,1] along dir. Origin-inside boxes clip at t=0.
    private static bool TryClip(Aabb box, Vec3d origin, Vec3d dir, out double t)
    {
        double tMin = 0.0;
        double tMax = 1.0;

        if (!ClipAxis(origin.X, dir.X, box.MinX, box.MaxX, ref tMin, ref tMax) ||
            !ClipAxis(origin.Y, dir.Y, box.MinY, box.MaxY, ref tMin, ref tMax) ||
            !ClipAxis(origin.Z, dir.Z, box.MinZ, box.MaxZ, ref tMin, ref tMax))
        {
            t = 0.0;
            return false;
        }

        t = tMin;
        return true;
    }

    private static bool ClipWithFace(Aabb box, Vec3d origin, Vec3d dir, out Vec3d point, out Direction face, out double t)
    {
        point = Vec3d.Zero;
        face = Direction.Up;
        double tMin = 0.0;
        double tMax = 1.0;
        int axisHit = -1;
        int signHit = 0;

        if (!ClipAxisFace(origin.X, dir.X, box.MinX, box.MaxX, 0, ref tMin, ref tMax, ref axisHit, ref signHit) ||
            !ClipAxisFace(origin.Y, dir.Y, box.MinY, box.MaxY, 1, ref tMin, ref tMax, ref axisHit, ref signHit) ||
            !ClipAxisFace(origin.Z, dir.Z, box.MinZ, box.MaxZ, 2, ref tMin, ref tMax, ref axisHit, ref signHit))
        {
            t = 0.0;
            return false;
        }

        t = tMin;
        point = origin.Add(dir.Scale(tMin));
        // signHit is the sign of the entered face's outward normal on the hit axis.
        face = axisHit switch
        {
            0 => signHit < 0 ? Direction.West : Direction.East,
            1 => signHit < 0 ? Direction.Down : Direction.Up,
            _ => signHit < 0 ? Direction.North : Direction.South,
        };
        return true;
    }

    private static bool ClipAxis(double origin, double dir, double min, double max, ref double tMin, ref double tMax)
    {
        if (Math.Abs(dir) < 1.0E-12)
            return origin >= min && origin <= max;

        double t1 = (min - origin) / dir;
        double t2 = (max - origin) / dir;
        if (t1 > t2)
            (t1, t2) = (t2, t1);

        if (t1 > tMin)
            tMin = t1;

        if (t2 < tMax)
            tMax = t2;

        return tMin <= tMax;
    }

    private static bool ClipAxisFace(double origin, double dir, double min, double max, int axis, ref double tMin, ref double tMax, ref int axisHit, ref int signHit)
    {
        if (Math.Abs(dir) < 1.0E-12)
            return origin >= min && origin <= max;

        double t1 = (min - origin) / dir;
        double t2 = (max - origin) / dir;
        if (t1 > t2)
            (t1, t2) = (t2, t1);

        if (t1 > tMin)
        {
            tMin = t1;
            axisHit = axis;
            // You enter through the face whose outward normal opposes the ray direction on this axis.
            signHit = dir > 0 ? -1 : 1;
        }

        if (t2 < tMax)
            tMax = t2;

        return tMin <= tMax;
    }

    private static double Lerp(double delta, double start, double end) => start + delta * (end - start);

    private static int FloorInt(double v) => (int)Math.Floor(v);

    private static double Frac(double v)
    {
        long floor = (long)v;
        if (v < floor)
            floor -= 1;

        return v - floor;
    }
}
