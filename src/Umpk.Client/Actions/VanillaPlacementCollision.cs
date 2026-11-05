using Umpk.Geometry;

namespace Umpk.Client.Actions;

/// <summary>Vanilla placement obstruction against an entity box, using the selected block collision boxes and native voxel grid.</summary>
internal static class VanillaPlacementCollision
{
    private const double Epsilon = 1.0E-7;

    public static bool Intersects(BlockPos block, Aabb entity, BlockInteractionProfile profile)
        => Intersects(Aabb.BlockAt(block.X, block.Y, block.Z), entity, profile);

    public static bool Intersects(BlockPos block, ReadOnlySpan<Aabb> shapes, Aabb entity, BlockInteractionProfile profile)
    {
        foreach (Aabb shape in shapes)
            if (Intersects(new Aabb(block.X + shape.MinX, block.Y + shape.MinY, block.Z + shape.MinZ, block.X + shape.MaxX, block.Y + shape.MaxY, block.Z + shape.MaxZ), entity, profile))
                return true;
        return false;
    }

    private static bool Intersects(Aabb cube, Aabb entity, BlockInteractionProfile profile)
    {
        if (!profile.VoxelPlacementObstruction)
            return cube.Intersects(entity);
        if (profile.DiscardThinObstructionShapes && (entity.XSize < Epsilon || entity.YSize < Epsilon || entity.ZSize < Epsilon))
            return false;

        // Shapes.create quantizes only when all three axes fit its 0..1 dyadic grid. Otherwise it keeps every original double coordinate.
        int xBits = FindBits(entity.MinX, entity.MaxX, profile.DiscardThinObstructionShapes);
        int yBits = FindBits(entity.MinY, entity.MaxY, profile.DiscardThinObstructionShapes);
        int zBits = FindBits(entity.MinZ, entity.MaxZ, profile.DiscardThinObstructionShapes);
        if (xBits >= 0 && yBits >= 0 && zBits >= 0)
        {
            entity = new Aabb(Snap(entity.MinX, xBits), Snap(entity.MinY, yBits), Snap(entity.MinZ, zBits), Snap(entity.MaxX, xBits), Snap(entity.MaxY, yBits), Snap(entity.MaxZ, zBits));
            if (entity.XSize <= 0 || entity.YSize <= 0 || entity.ZSize <= 0)
                return false;
        }

        return Overlaps(cube.MinX, cube.MaxX, entity.MinX, entity.MaxX)
            && Overlaps(cube.MinY, cube.MaxY, entity.MinY, entity.MaxY)
            && Overlaps(cube.MinZ, cube.MaxZ, entity.MinZ, entity.MaxZ);
    }

    private static int FindBits(double min, double max, bool modern)
    {
        if (min < -Epsilon || max > 1.0000001)
            return -1;
        for (int bits = 0; bits <= 3; bits++)
        {
            int parts = 1 << bits;
            double low = min * parts;
            double high = max * parts;
            // Before 1.17 the grid test uses floor and an unscaled tolerance. The modern test uses nearest-integer rounding and scales the tolerance by the grid resolution.
            double lowReference = Math.Floor(low + (modern ? 0.5 : 0));
            double highReference = Math.Floor(high + (modern ? 0.5 : 0));
            double tolerance = modern ? Epsilon * parts : Epsilon;
            if (Math.Abs(low - lowReference) < tolerance && Math.Abs(high - highReference) < tolerance)
                return bits;
        }
        return -1;
    }

    private static double Snap(double value, int bits)
    {
        int parts = 1 << bits;
        return Math.Floor(value * parts + 0.5) / parts;
    }

    private static bool Overlaps(double firstMin, double firstMax, double secondMin, double secondMax)
    {
        if (firstMax < secondMin - Epsilon || secondMax < firstMin - Epsilon)
            return false;

        // Reduce IndirectMerger(AND) to two filled intervals. A distinct next plane closes the previous interval; coalesced planes replace its indices. Preserve the native comparison order at large coordinates.
        int first = 0, second = 0, count = 0, previousFirst = -1, previousSecond = -1;
        double last = double.NaN;
        while (first < 2 || second < 2)
        {
            bool firstDone = first >= 2;
            bool secondDone = second >= 2;
            double firstCoordinate = first == 0 ? firstMin : firstMax;
            double secondCoordinate = second == 0 ? secondMin : secondMax;
            bool takeFirst = !firstDone && (secondDone || firstCoordinate < secondCoordinate + Epsilon);
            if (takeFirst)
            {
                first++;
                if (second == 0 || secondDone)
                    continue;
            }
            else
            {
                second++;
                if (first == 0 || firstDone)
                    continue;
            }

            double coordinate = takeFirst ? firstCoordinate : secondCoordinate;
            if (!(last >= coordinate - Epsilon))
            {
                if (count != 0 && previousFirst == 0 && previousSecond == 0)
                    return true;
                count++;
                last = coordinate;
            }
            previousFirst = first - 1;
            previousSecond = second - 1;
        }
        return false;
    }
}
