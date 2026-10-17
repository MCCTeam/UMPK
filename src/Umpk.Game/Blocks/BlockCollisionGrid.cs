using Umpk.Geometry;

namespace Umpk.Game.Blocks;

/// <summary>An immutable block-local voxel grid, including inactive and coincident coordinate planes. Occupied cells use <c>(x * ySize + y) * zSize + z</c> order.</summary>
/// <remarks>The canonical full-block flag records the native shape's identity. Equal bounding boxes do not establish that identity. Evaluate any measured position offset with <see cref="OffsetAt"/> and apply it before the world position; never normalize a shifted grid by subtracting a floating-point offset.</remarks>
public sealed class BlockCollisionGrid
{
    private readonly double[] _x, _y, _z;
    private readonly int[] _cells;
    private readonly Aabb[] _boxes;

    /// <summary>Copies exact native coordinates, sorted unique occupied cells, and the shape's bounding-box decomposition.</summary>
    public BlockCollisionGrid(ReadOnlySpan<double> xCoordinates, ReadOnlySpan<double> yCoordinates, ReadOnlySpan<double> zCoordinates, ReadOnlySpan<int> occupiedCells, ReadOnlySpan<Aabb> boxes, bool canonicalFullBlock = false)
        : this(xCoordinates, yCoordinates, zCoordinates, occupiedCells, boxes, canonicalFullBlock, 0)
    {
    }

    private BlockCollisionGrid(ReadOnlySpan<double> xCoordinates, ReadOnlySpan<double> yCoordinates, ReadOnlySpan<double> zCoordinates, ReadOnlySpan<int> occupiedCells, ReadOnlySpan<Aabb> boxes, bool canonicalFullBlock, double maxHorizontalOffset)
    {
        if (!double.IsFinite(maxHorizontalOffset) || maxHorizontalOffset < 0 || (canonicalFullBlock && maxHorizontalOffset != 0))
            throw new ArgumentOutOfRangeException(nameof(maxHorizontalOffset));
        ValidateCoordinates(xCoordinates, nameof(xCoordinates));
        ValidateCoordinates(yCoordinates, nameof(yCoordinates));
        ValidateCoordinates(zCoordinates, nameof(zCoordinates));
        long capacity = checked((long)(xCoordinates.Length - 1) * (yCoordinates.Length - 1) * (zCoordinates.Length - 1));
        if (occupiedCells.IsEmpty != boxes.IsEmpty)
            throw new ArgumentException("Occupied cells and collision boxes must agree on whether the shape is empty.", nameof(boxes));
        int previous = -1;
        foreach (int cell in occupiedCells)
        {
            if (cell <= previous || cell >= capacity)
                throw new ArgumentException("Occupied cells must be sorted, unique indices within the coordinate grid.", nameof(occupiedCells));
            previous = cell;
        }
        foreach (Aabb box in boxes)
        {
            if (!double.IsFinite(box.MinX) || !double.IsFinite(box.MinY) || !double.IsFinite(box.MinZ)
                || !double.IsFinite(box.MaxX) || !double.IsFinite(box.MaxY) || !double.IsFinite(box.MaxZ))
                throw new ArgumentException("Collision boxes must have finite coordinates.", nameof(boxes));
        }
        if (canonicalFullBlock && (!IsUnitAxis(xCoordinates) || !IsUnitAxis(yCoordinates) || !IsUnitAxis(zCoordinates)
            || occupiedCells.Length != 1 || occupiedCells[0] != 0 || boxes.Length != 1 || boxes[0] != Aabb.BlockAt(0, 0, 0)))
            throw new ArgumentException("A canonical full block must have the occupied unit grid and unit bounding box.", nameof(canonicalFullBlock));
        _x = xCoordinates.ToArray();
        _y = yCoordinates.ToArray();
        _z = zCoordinates.ToArray();
        _cells = occupiedCells.ToArray();
        _boxes = boxes.ToArray();
        CanonicalFullBlock = canonicalFullBlock;
        MaxHorizontalOffset = maxHorizontalOffset;
    }

    /// <summary>The exact X coordinate planes, including inactive planes.</summary>
    public ReadOnlySpan<double> XCoordinates => _x;

    /// <summary>The exact Y coordinate planes, including inactive planes.</summary>
    public ReadOnlySpan<double> YCoordinates => _y;

    /// <summary>The exact Z coordinate planes, including inactive planes.</summary>
    public ReadOnlySpan<double> ZCoordinates => _z;

    /// <summary>The sorted flattened indices of occupied voxels. A zero-width voxel can be occupied.</summary>
    public ReadOnlySpan<int> OccupiedCells => _cells;

    /// <summary>The native bounding-box decomposition, in the same local coordinates.</summary>
    public ReadOnlySpan<Aabb> Boxes => _boxes;

    /// <summary>Whether the original native shape was the canonical full-block singleton.</summary>
    public bool CanonicalFullBlock { get; }

    /// <summary>The measured horizontal offset limit, or zero for a grid with no position offset.</summary>
    public double MaxHorizontalOffset { get; }

    /// <summary>Copies original coordinate delegates and a positive native horizontal offset limit. A translated shape does not have canonical full-block identity.</summary>
    public static BlockCollisionGrid CreateWithHorizontalOffset(ReadOnlySpan<double> xCoordinates, ReadOnlySpan<double> yCoordinates, ReadOnlySpan<double> zCoordinates, ReadOnlySpan<int> occupiedCells, ReadOnlySpan<Aabb> boxes, double maxHorizontalOffset)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHorizontalOffset);
        return new BlockCollisionGrid(xCoordinates, yCoordinates, zCoordinates, occupiedCells, boxes, false, maxHorizontalOffset);
    }

    /// <summary>Evaluates the native horizontal position offset. The seed ignores Y and retains the game's integer wrapping and float division.</summary>
    /// <remarks>1.14.4-client-decompiled net/minecraft/world/level/block/Block.java and 26.3-client-decompiled net/minecraft/world/level/block/state/BlockBehaviour.java define the position hash and quarter-block offset range.</remarks>
    public Vec3d OffsetAt(BlockPos position)
    {
        if (MaxHorizontalOffset == 0)
            return Vec3d.Zero;
        long seed = unchecked((long)(position.X * 3129871) ^ (long)position.Z * 116129781L);
        seed = unchecked(seed * seed * 42317861L + seed * 11L) >> 16;
        double x = Math.Clamp(((float)(seed & 15L) / 15.0F - .5) * .5, -MaxHorizontalOffset, MaxHorizontalOffset);
        double z = Math.Clamp(((float)((seed >> 8) & 15L) / 15.0F - .5) * .5, -MaxHorizontalOffset, MaxHorizontalOffset);
        return new Vec3d(x, 0, z);
    }

    private static bool IsUnitAxis(ReadOnlySpan<double> coordinates) => coordinates.Length == 2 && coordinates[0] == 0 && coordinates[1] == 1;

    private static void ValidateCoordinates(ReadOnlySpan<double> coordinates, string name)
    {
        if (coordinates.IsEmpty)
            throw new ArgumentException("A voxel axis must contain at least one coordinate.", name);
        for (int index = 0; index < coordinates.Length; index++)
            if (!double.IsFinite(coordinates[index]) || (index > 0 && coordinates[index] < coordinates[index - 1]))
                throw new ArgumentException("Voxel coordinates must be finite and nondecreasing.", name);
    }
}
