using Umpk.Geometry;

namespace Umpk.Game.World;

/// <summary>The outcome of an entity raycast against a set of candidate AABBs: whether an AABB was hit, the index of the closest candidate that was hit, the hit point, and the distance. Entities themselves are the entity module's model; this API takes their bounding boxes as <see cref="Umpk.Geometry.Aabb"/> so it stays decoupled from the entity type.</summary>
public readonly struct EntityHitResult : IEquatable<EntityHitResult>
{
    private EntityHitResult(bool hit, int candidateIndex, Vec3d point, double distance)
    {
        Hit = hit;
        CandidateIndex = candidateIndex;
        Point = point;
        Distance = distance;
    }

    /// <summary>A miss result.</summary>
    public static readonly EntityHitResult Miss = new(false, -1, Vec3d.Zero, 0.0);

    /// <summary>Builds a hit result.</summary>
    public static EntityHitResult FromHit(int candidateIndex, Vec3d point, double distance) =>
        new(true, candidateIndex, point, distance);

    /// <summary>True when the ray hit a candidate.</summary>
    public bool Hit { get; }

    /// <summary>The index into the candidate list that was hit (-1 on a miss).</summary>
    public int CandidateIndex { get; }

    /// <summary>The world point where the ray entered the hit AABB (undefined on a miss).</summary>
    public Vec3d Point { get; }

    /// <summary>The distance from the ray origin to <see cref="Point"/> (0 on a miss).</summary>
    public double Distance { get; }

    /// <inheritdoc/>
    public bool Equals(EntityHitResult other) =>
        Hit == other.Hit && CandidateIndex == other.CandidateIndex &&
        Point.Equals(other.Point) && Distance.Equals(other.Distance);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is EntityHitResult other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Hit, CandidateIndex, Point, Distance);

    /// <summary>Value equality.</summary>
    public static bool operator ==(EntityHitResult left, EntityHitResult right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(EntityHitResult left, EntityHitResult right) => !left.Equals(right);
}
