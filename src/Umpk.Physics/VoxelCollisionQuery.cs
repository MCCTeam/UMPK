using System.Buffers;
using Umpk.Game.Blocks;
using Umpk.Geometry;

namespace Umpk.Physics;

/// <summary>Intersects a translated block grid with a native AABB query shape. The block iterator wraps coordinate lists on translation, so two implicit cube-list merging is unreachable here.</summary>
/// <remarks>1.13.2-client-decompiled cep.java keeps planes exactly one epsilon apart; 1.14-client-decompiled csi.java changes that strict comparison. 1.14.4-client-decompiled net/minecraft/world/level/LevelReader.java supplies query-first merging; 1.16-client-decompiled net/minecraft/world/level/CollisionSpliterator.java adds the canonical full-block shortcut and block-first merging. 1.17-client-decompiled net/minecraft/world/phys/shapes/Shapes.java changes thin-query and grid detection. 26.3-client-decompiled net/minecraft/world/phys/shapes/IndirectMerger.java retains the previous plane's index mapping until the next distinct plane is found.</remarks>
internal static class VoxelCollisionQuery
{
    private const double Epsilon = PhysicsConstants.CollisionEpsilon;

    internal static bool Intersects(BlockCollisionGrid grid, BlockPos position, in Aabb query, BlockCollisionQueryEra era, Vec3d shapeOffset)
    {
        if (grid.OccupiedCells.IsEmpty)
            return false;
        if (era is BlockCollisionQueryEra.BlockFirst or BlockCollisionQueryEra.Modern && grid.CanonicalFullBlock)
            return Aabb.BlockAt(position.X, position.Y, position.Z).Intersects(query);
        bool modern = era == BlockCollisionQueryEra.Modern;
        if (modern && (query.XSize < Epsilon || query.YSize < Epsilon || query.ZSize < Epsilon))
            return false;

        Span<int> bits = stackalloc int[3];
        for (int axis = 0; axis < 3; axis++)
            bits[axis] = FindBits(query.Min(axis), query.Max(axis), modern);
        bool arrayShape = bits[0] < 0 || bits[1] < 0 || bits[2] < 0;
        Span<double> queryCoordinates = stackalloc double[27];
        Span<int> sizes = stackalloc int[3];
        Span<int> low = stackalloc int[3];
        Span<int> high = stackalloc int[3];
        for (int axis = 0; axis < 3; axis++)
        {
            Span<double> coordinates = queryCoordinates.Slice(axis * 9, 9);
            if (arrayShape)
            {
                coordinates[0] = query.Min(axis);
                coordinates[1] = query.Max(axis);
                sizes[axis] = 2;
                low[axis] = 0;
                high[axis] = 1;
            }
            else
            {
                int intervals = 1 << bits[axis];
                for (int index = 0; index <= intervals; index++)
                    coordinates[index] = (double)index / intervals;
                sizes[axis] = intervals + 1;
                low[axis] = (int)Math.Floor(query.Min(axis) * intervals + .5);
                high[axis] = (int)Math.Floor(query.Max(axis) * intervals + .5);
                if (low[axis] == high[axis])
                    return false;
            }
        }

        int nx = grid.XCoordinates.Length - 1, ny = grid.YCoordinates.Length - 1, nz = grid.ZCoordinates.Length - 1;
        int capacity = checked(nx + ny + nz);
        byte[]? rented = null;
        Span<byte> matches = capacity <= 256 ? stackalloc byte[capacity] : (rented = ArrayPool<byte>.Shared.Rent(capacity)).AsSpan(0, capacity);
        matches.Clear();
        try
        {
            bool queryFirst = era is BlockCollisionQueryEra.QueryFirst or BlockCollisionQueryEra.QueryFirstStrict;
            bool strict = era == BlockCollisionQueryEra.QueryFirstStrict;
            MarkAxis(grid.XCoordinates, shapeOffset.X, position.X, queryCoordinates[..sizes[0]], low[0], high[0], queryFirst, strict, matches[..nx]);
            MarkAxis(grid.YCoordinates, shapeOffset.Y, position.Y, queryCoordinates.Slice(9, sizes[1]), low[1], high[1], queryFirst, strict, matches.Slice(nx, ny));
            MarkAxis(grid.ZCoordinates, shapeOffset.Z, position.Z, queryCoordinates.Slice(18, sizes[2]), low[2], high[2], queryFirst, strict, matches.Slice(nx + ny, nz));
            foreach (int cell in grid.OccupiedCells)
            {
                int z = cell % nz, xy = cell / nz, y = xy % ny, x = xy / ny;
                if (matches[x] != 0 && matches[nx + y] != 0 && matches[nx + ny + z] != 0)
                    return true;
            }
            return false;
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static int FindBits(double min, double max, bool modern)
    {
        if (min < -Epsilon || max > 1.0000001)
            return -1;
        for (int bits = 0; bits <= 3; bits++)
        {
            int intervals = 1 << bits;
            double low = min * intervals, high = max * intervals;
            double tolerance = modern ? Epsilon * intervals : Epsilon;
            if (Math.Abs(low - Math.Floor(low + (modern ? .5 : 0))) < tolerance
                && Math.Abs(high - Math.Floor(high + (modern ? .5 : 0))) < tolerance)
                return bits;
        }
        return -1;
    }

    private static void MarkAxis(ReadOnlySpan<double> block, double shapeOffset, double offset, ReadOnlySpan<double> query, int low, int high, bool queryFirst, bool strict, Span<byte> matches)
    {
        bool identical = block.Length == query.Length;
        for (int index = 0; identical && index < block.Length; index++)
            identical = BitConverter.DoubleToInt64Bits(block[index] + shapeOffset + offset) == BitConverter.DoubleToInt64Bits(query[index]);
        if (identical)
        {
            for (int index = low; index < high; index++)
                matches[index] = 1;
            return;
        }

        // The indirect merger can overwrite the mapping of the previous plane. Publish that plane's interval only after a distinct next plane arrives, and discard the final plane.
        int b = 0, q = 0, pendingBlock = -1, pendingQuery = -1;
        double last = double.NaN;
        while (b < block.Length || q < query.Length)
        {
            bool blockDone = b >= block.Length, queryDone = q >= query.Length;
            bool chooseBlock = queryFirst
                ? blockDone ? false : queryDone || !(query[q] < block[b] + shapeOffset + offset + Epsilon)
                : !blockDone && (queryDone || block[b] + shapeOffset + offset < query[q] + Epsilon);
            if (chooseBlock)
            {
                b++;
                if (q == 0 || queryDone)
                    continue;
            }
            else
            {
                q++;
                if (b == 0 || blockDone)
                    continue;
            }
            double coordinate = chooseBlock ? block[b - 1] + shapeOffset + offset : query[q - 1];
            if (strict ? !(last > coordinate - Epsilon) : !(last >= coordinate - Epsilon))
            {
                if (pendingBlock >= 0 && pendingBlock < matches.Length && pendingQuery >= low && pendingQuery < high)
                    matches[pendingBlock] = 1;
                last = coordinate;
            }
            pendingBlock = b - 1;
            pendingQuery = q - 1;
        }
    }
}
