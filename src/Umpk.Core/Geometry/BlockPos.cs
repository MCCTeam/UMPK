using System.Runtime.CompilerServices;

namespace Umpk.Geometry;

/// <summary>An integer block position.</summary>
public readonly record struct BlockPos(int X, int Y, int Z)
{
    public static readonly BlockPos Zero = new(0, 0, 0);

    /// <summary>The block containing the given point (floor semantics, correct for negatives).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BlockPos Containing(double x, double y, double z) =>
        new((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(z));

    /// <summary>The block containing the given point.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BlockPos Containing(Vec3d position) => Containing(position.X, position.Y, position.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BlockPos Offset(int dx, int dy, int dz) => new(X + dx, Y + dy, Z + dz);

    public BlockPos Offset(Direction direction) => Offset(direction, 1);

    public BlockPos Offset(Direction direction, int count) =>
        new(X + direction.StepX() * count, Y + direction.StepY() * count, Z + direction.StepZ() * count);

    public BlockPos Above(int count = 1) => new(X, Y + count, Z);

    public BlockPos Below(int count = 1) => new(X, Y - count, Z);

    /// <summary>The center point of this block.</summary>
    public Vec3d Center => new(X + 0.5, Y + 0.5, Z + 0.5);

    /// <summary>The bottom-center point of this block (feet position for a standing entity).</summary>
    public Vec3d BottomCenter => new(X + 0.5, Y, Z + 0.5);

    /// <summary>The minimum corner of this block as a vector.</summary>
    public Vec3d MinCorner => new(X, Y, Z);

    public override string ToString() => $"({X}, {Y}, {Z})";
}
