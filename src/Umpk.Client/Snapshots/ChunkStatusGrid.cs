using Umpk.Game.World;
using Umpk.Geometry;

namespace Umpk.Client.Snapshots;

/// <summary>A per-chunk loaded-state grid centered on a chunk (typically the player's). Row-major: same layout convention as <see cref="SurfaceRegionSnapshot"/>, <c>Loaded[row * Columns + column]</c>.</summary>
public sealed record ChunkStatusGrid(int CenterChunkX, int CenterChunkZ, int Columns, int Rows, IReadOnlyList<bool> Loaded)
{
    /// <summary>The number of loaded columns in the grid.</summary>
    public int LoadedCount
    {
        get
        {
            int count = 0;
            foreach (bool loaded in Loaded)
                if (loaded)
                    count++;

            return count;
        }
    }

    /// <summary>The chunk position at the given grid cell. Consumers re-derive <c>center - half + index</c> by hand today, and the two halves come from DIFFERENT dimensions (<paramref name="column"/> against X, <paramref name="row"/> against Z), which is easy to transpose; this is the one place that formula is written.</summary>
    public ChunkPos ChunkAt(int row, int column)
    {
        int halfX = (Columns - 1) / 2;
        int halfZ = (Rows - 1) / 2;
        return new ChunkPos(CenterChunkX - halfX + column, CenterChunkZ - halfZ + row);
    }

    /// <summary>Whether the chunk at the given grid cell is loaded.</summary>
    public bool IsLoadedAt(int row, int column) => Loaded[(row * Columns) + column];

    /// <summary>Builds a loaded-state grid of <paramref name="columns"/> x <paramref name="rows"/> chunks centered on <paramref name="center"/>. Odd sizes centre exactly: the middle cell IS <paramref name="center"/>. A pure static: no session, no world thread affinity.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="columns"/> or <paramref name="rows"/> is not positive.</exception>
    public static ChunkStatusGrid Around(World world, ChunkPos center, int columns, int rows)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (columns <= 0)
            throw new ArgumentOutOfRangeException(nameof(columns));

        if (rows <= 0)
            throw new ArgumentOutOfRangeException(nameof(rows));

        int halfX = (columns - 1) / 2;
        int halfZ = (rows - 1) / 2;

        var loaded = new bool[columns * rows];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
            {
                var pos = new ChunkPos(center.X - halfX + c, center.Z - halfZ + r);
                loaded[(r * columns) + c] = world.GetColumn(pos) is not null;
            }

        return new ChunkStatusGrid(center.X, center.Z, columns, rows, loaded);
    }
}
