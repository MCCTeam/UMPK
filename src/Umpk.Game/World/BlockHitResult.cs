using Umpk.Geometry;

namespace Umpk.Game.World;

/// <summary>The outcome of a block raycast: whether a block was hit, and if so the block position, the face entered, the exact hit point, and the distance from the ray origin.</summary>
public readonly struct BlockHitResult : IEquatable<BlockHitResult>
{
    private BlockHitResult(bool hit, BlockPos blockPos, Direction face, Vec3d point, double distance)
    {
        Hit = hit;
        BlockPos = blockPos;
        Face = face;
        Point = point;
        Distance = distance;
    }

    /// <summary>A miss result.</summary>
    public static readonly BlockHitResult Miss = new(false, BlockPos.Zero, Direction.Up, Vec3d.Zero, 0.0);

    /// <summary>Builds a hit result.</summary>
    public static BlockHitResult FromHit(BlockPos blockPos, Direction face, Vec3d point, double distance) =>
        new(true, blockPos, face, point, distance);

    /// <summary>True when the ray hit a block.</summary>
    public bool Hit { get; }

    /// <summary>The block that was hit (undefined on a miss).</summary>
    public BlockPos BlockPos { get; }

    /// <summary>The face of the block the ray entered through (undefined on a miss).</summary>
    public Direction Face { get; }

    /// <summary>The exact world point where the ray met the block surface (undefined on a miss).</summary>
    public Vec3d Point { get; }

    /// <summary>The distance from the ray origin to <see cref="Point"/> (0 on a miss).</summary>
    public double Distance { get; }

    /// <inheritdoc/>
    public bool Equals(BlockHitResult other) =>
        Hit == other.Hit && BlockPos.Equals(other.BlockPos) && Face == other.Face &&
        Point.Equals(other.Point) && Distance.Equals(other.Distance);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is BlockHitResult other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Hit, BlockPos, Face, Point, Distance);

    /// <summary>Value equality.</summary>
    public static bool operator ==(BlockHitResult left, BlockHitResult right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(BlockHitResult left, BlockHitResult right) => !left.Equals(right);
}
