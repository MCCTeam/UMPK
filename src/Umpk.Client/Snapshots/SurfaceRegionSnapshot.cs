using Umpk.Game.Blocks;
using Umpk.Game.World;
using Umpk.Geometry;

namespace Umpk.Client.Snapshots;

/// <summary>One column's surface read: whether its chunk is loaded, whether a non-air block was found scanning down from the top (or from a caller-supplied ceiling), the Y it was found at, and its state. <see cref="Loaded"/> and <see cref="Found"/> are deliberately separate flags: an unloaded column (nothing known) and a loaded column that is void all the way to the floor (something known: there is nothing there) are different facts, and a minimap draws them differently, nothing versus void.</summary>
public sealed record SurfaceColumn(bool Loaded, bool Found, int SurfaceY, BlockState State)
{
    /// <summary>The result for a column whose chunk is not loaded: no data, not even "void".</summary>
    public static readonly SurfaceColumn Unloaded = new(false, false, 0, default);
}

/// <summary>A top-down sample of a rectangular XZ region: the minimap feed. Row-major over Z then X, so <c>Columns[row * Width + column]</c> is the cell at world <c>(OriginX + column, OriginZ + row)</c>.</summary>
public sealed record SurfaceRegionSnapshot(int OriginX, int OriginZ, int Width, int Length, IReadOnlyList<SurfaceColumn> Columns)
{
    /// <summary>The sampled column at the given grid offset. Bounds-checked against <see cref="Width"/>/<see cref="Length"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="column"/> or <paramref name="row"/> is outside the grid.</exception>
    public SurfaceColumn At(int column, int row)
    {
        if (column < 0 || column >= Width)
            throw new ArgumentOutOfRangeException(nameof(column));

        if (row < 0 || row >= Length)
            throw new ArgumentOutOfRangeException(nameof(row));

        return Columns[(row * Width) + column];
    }

    /// <summary>Samples a <paramref name="width"/> x <paramref name="length"/> region of <paramref name="world"/> starting at world <c>(originX, originZ)</c>. Each column scans down for the first non-air block, starting from the world's own top or from <paramref name="ceilingY"/> when given and lower (cave mode): the scan never looks above the ceiling, so it cannot report a surface that sits above it. A pure static: no session, no world thread affinity.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="length"/> is not positive.</exception>
    public static SurfaceRegionSnapshot Sample(World world, int originX, int originZ, int width, int length, int? ceilingY = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length));

        var columns = new SurfaceColumn[width * length];
        for (int row = 0; row < length; row++)
        {
            int wz = originZ + row;
            for (int col = 0; col < width; col++)
            {
                int wx = originX + col;
                columns[(row * width) + col] = SampleColumn(world, wx, wz, ceilingY);
            }
        }

        return new SurfaceRegionSnapshot(originX, originZ, width, length, columns);
    }

    private static SurfaceColumn SampleColumn(World world, int wx, int wz, int? ceilingY)
    {
        ChunkColumn? column = world.GetColumn(new BlockPos(wx, 0, wz));
        if (column is null)
            return SurfaceColumn.Unloaded;

        int top = column.MaxY - 1;
        if (ceilingY is { } ceiling && ceiling < top)
            top = ceiling;

        for (int y = top; y >= column.MinY; y--)
        {
            var state = new BlockState(world.BlockData, column.GetBlockStateId(wx, y, wz));
            if (state.IsAir)
                continue;

            return new SurfaceColumn(true, true, y, state);
        }

        // Loaded, but nothing but air down to the floor: a void column.
        return new SurfaceColumn(true, false, column.MinY, default);
    }
}
