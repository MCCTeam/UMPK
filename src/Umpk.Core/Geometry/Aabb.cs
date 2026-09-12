using System.Runtime.CompilerServices;

namespace Umpk.Geometry;

/// <summary>An immutable axis-aligned bounding box. Transforming methods return new instances.</summary>
public readonly struct Aabb : IEquatable<Aabb>
{
    public static readonly Aabb Empty = new(0, 0, 0, 0, 0, 0);

    public readonly double MinX, MinY, MinZ;
    public readonly double MaxX, MaxY, MaxZ;

    public Aabb(double x1, double y1, double z1, double x2, double y2, double z2)
    {
        MinX = Math.Min(x1, x2);
        MinY = Math.Min(y1, y2);
        MinZ = Math.Min(z1, z2);
        MaxX = Math.Max(x1, x2);
        MaxY = Math.Max(y1, y2);
        MaxZ = Math.Max(z1, z2);
    }

    /// <summary>Creates a player-style AABB centered on feet X/Z with the given width and height.</summary>
    /// <remarks>
    /// <para>Width and height are first rounded to floats, and width is halved with float arithmetic, before the values are widened back to doubles. This is the bounding-box arithmetic used by every supported protocol era.</para>
    /// <para>Halving a double instead makes the box 1.1920929e-8 NARROWER per horizontal side (0.29999999999999999 versus vanilla's 0.300000011920928955) and 4.76837e-8 SHORTER for a 1.8 standing height. The difference is observable in collision results: a representative piston push ends at 3.8100000119209287 with float arithmetic and 3.81 with double arithmetic.</para>
    /// </remarks>
    public static Aabb OfSize(double centerX, double feetY, double centerZ, double width, double height)
    {
        float hw = (float)width / 2.0f;
        float h = (float)height;
        return new Aabb(centerX - hw, feetY, centerZ - hw, centerX + hw, feetY + h, centerZ + hw);
    }

    /// <summary>Full block AABB at the given integer position.</summary>
    public static Aabb BlockAt(int x, int y, int z) =>
        new(x, y, z, x + 1.0, y + 1.0, z + 1.0);

    public double XSize => MaxX - MinX;

    public double YSize => MaxY - MinY;

    public double ZSize => MaxZ - MinZ;

    public double Min(int axis) => axis switch { 0 => MinX, 1 => MinY, _ => MinZ };

    public double Max(int axis) => axis switch { 0 => MaxX, 1 => MaxY, _ => MaxZ };

    /// <summary>Expands each bound in the direction of movement.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Aabb ExpandTowards(double dx, double dy, double dz)
    {
        double minX = MinX, minY = MinY, minZ = MinZ;
        double maxX = MaxX, maxY = MaxY, maxZ = MaxZ;
        if (dx < 0)
            minX += dx;

        else if (dx > 0)
            maxX += dx;

        if (dy < 0)
            minY += dy;

        else if (dy > 0)
            maxY += dy;

        if (dz < 0)
            minZ += dz;

        else if (dz > 0)
            maxZ += dz;

        return new Aabb(minX, minY, minZ, maxX, maxY, maxZ);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Aabb ExpandTowards(Vec3d v) => ExpandTowards(v.X, v.Y, v.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Aabb Inflate(double x, double y, double z) =>
        new(MinX - x, MinY - y, MinZ - z, MaxX + x, MaxY + y, MaxZ + z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Aabb Inflate(double v) => Inflate(v, v, v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Aabb Deflate(double x, double y, double z) => Inflate(-x, -y, -z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Aabb Move(double dx, double dy, double dz) =>
        new(MinX + dx, MinY + dy, MinZ + dz, MaxX + dx, MaxY + dy, MaxZ + dz);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Aabb Move(Vec3d v) => Move(v.X, v.Y, v.Z);

    /// <summary>Strict overlap test (vanilla uses <c>&lt;</c> and <c>&gt;</c>, not <c>&lt;=</c>).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Intersects(Aabb other) =>
        MinX < other.MaxX && MaxX > other.MinX &&
        MinY < other.MaxY && MaxY > other.MinY &&
        MinZ < other.MaxZ && MaxZ > other.MinZ;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Intersects(double x1, double y1, double z1, double x2, double y2, double z2) =>
        MinX < x2 && MaxX > x1 && MinY < y2 && MaxY > y1 && MinZ < z2 && MaxZ > z1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(double x, double y, double z) =>
        x >= MinX && x < MaxX && y >= MinY && y < MaxY && z >= MinZ && z < MaxZ;

    /// <summary>Contact epsilon applied to each index lookup. It remains private because <c>Umpk.Core</c> must not reference <c>Umpk.Physics</c>.</summary>
    private const double CollideEpsilon = 1.0E-7;

    /// <summary>Clips entity movement along X against a block shape (<paramref name="other"/>). Uses collision semantics reduced to a single-box shape.</summary>
    /// <remarks>
    /// <para>Collision is expressed as index-window comparisons with a <c>1.0E-7</c> offset. For a shape with exactly two coordinates per axis, the cross-axis window is non-empty iff <c>box.min(cross) + 1e-7 &lt; shape.max(cross)</c> (strict) AND <c>box.max(cross) - 1e-7 &gt;= shape.min(cross)</c> (inclusive), and the movement-axis window is non-empty iff <c>box.max(axis) - 1e-7 &lt; shape.min(axis)</c> going forward or <c>box.min(axis) + 1e-7 &gt;= shape.max(axis)</c> going backward. The guards use the offset form (<c>a + eps &gt;= b</c>) rather than the algebraically equal difference form (<c>b - a &lt;= eps</c>) so the arithmetic stays bit-identical at world-border magnitudes.</para>
    /// <para>The consequence, and the reason this matters: a face up to <c>1e-7</c> BEHIND the leading edge still clips, so the returned distance may be slightly negative: a micro push-out of at most <c>1e-7</c>. The matching <c>d &gt;= -1e-7</c> / <c>d &lt;= 1e-7</c> acceptance checks are redundant once the window guards hold, but keeping both halves preserves exact arithmetic.</para>
    /// <para>This matters because <see cref="OfSize"/> uses vanilla's FLOAT half width. A player at x=147.7 has <c>maxX = 148.00000001192091758</c>, overlapping the block column at x=148 by 1.1920917586394353e-08. Without the epsilon the strict <c>other.MinX &gt;= MaxX</c> guard is false, forward movement into that wall never clips, and the client walks into solid rock while the server rejects and re-teleports every tick - the flush-contact stall. Symmetrically, that same 1.19e-8 sliver is BELOW the epsilon on a cross axis, so it no longer connects a falling box to a wall it should slide past.</para>
    /// <para>Boundaries. The reduction is exact only for shapes whose per-axis coordinate list has two entries. Multi-box block shapes (stairs, fences) are fed to this method one box at a time, so where a coordinate grid has interior faces this can under-clip. The empty-shape guard and the <c>|movement| &lt; 1e-7</c> collapse to <c>0.0</c> live one level up, in <c>CollisionResolver.CollideAxis</c>, not here.</para>
    /// <para>UMPK applies the epsilon on every protocol even though legacy collision used strict guards. This is the safe compatibility direction: without the epsilon, flush contact can penetrate a block and cause the server to reject every subsequent move. UMPK does not implement a separate player push-out hook, and the usual probe offsets would not sample the neighbouring column in the small-overlap case described above.</para>
    /// </remarks>
    public double CollideX(Aabb other, double movement)
    {
        // Cross-axis gate: the shape clips this axis only when the cross-axis overlap exceeds 1e-7, matching the index-window contact epsilon.
        if (MinY + CollideEpsilon >= other.MaxY || other.MinY > MaxY - CollideEpsilon
            || MinZ + CollideEpsilon >= other.MaxZ || other.MinZ > MaxZ - CollideEpsilon)
            return movement;

        if (movement > 0.0)
        {
            // A face up to 1e-7 behind the leading edge still clips: the result may be slightly negative, a micro push-out of at most 1e-7.
            if (MaxX - CollideEpsilon < other.MinX)
            {
                double d = other.MinX - MaxX;
                if (d >= -CollideEpsilon && d < movement)
                    movement = d;

            }
        }
        else if (movement < 0.0)
        {
            // Mirrored, inclusive at the epsilon.
            if (MinX + CollideEpsilon >= other.MaxX)
            {
                double d = other.MaxX - MinX;
                if (d <= CollideEpsilon && d > movement)
                    movement = d;

            }
        }

        return movement;
    }

    /// <summary>Clips entity movement along Y against a block shape. Same semantics and the same 1e-7 contact epsilon as <see cref="CollideX"/>, with the axes rotated; see that method's remarks for the derivation, micro push-out, and compatibility rule.</summary>
    public double CollideY(Aabb other, double movement)
    {
        if (MinX + CollideEpsilon >= other.MaxX || other.MinX > MaxX - CollideEpsilon
            || MinZ + CollideEpsilon >= other.MaxZ || other.MinZ > MaxZ - CollideEpsilon)
            return movement;

        if (movement > 0.0)
        {
            if (MaxY - CollideEpsilon < other.MinY)
            {
                double d = other.MinY - MaxY;
                if (d >= -CollideEpsilon && d < movement)
                    movement = d;

            }
        }
        else if (movement < 0.0)
            if (MinY + CollideEpsilon >= other.MaxY)
            {
                double d = other.MaxY - MinY;
                if (d <= CollideEpsilon && d > movement)
                    movement = d;

            }

        return movement;
    }

    /// <summary>Clips entity movement along Z against a block shape. Same semantics and the same 1e-7 contact epsilon as <see cref="CollideX"/>, with the axes rotated; see that method's remarks for the derivation, micro push-out, and compatibility rule.</summary>
    public double CollideZ(Aabb other, double movement)
    {
        if (MinX + CollideEpsilon >= other.MaxX || other.MinX > MaxX - CollideEpsilon
            || MinY + CollideEpsilon >= other.MaxY || other.MinY > MaxY - CollideEpsilon)
            return movement;

        if (movement > 0.0)
        {
            if (MaxZ - CollideEpsilon < other.MinZ)
            {
                double d = other.MinZ - MaxZ;
                if (d >= -CollideEpsilon && d < movement)
                    movement = d;

            }
        }
        else if (movement < 0.0)
            if (MinZ + CollideEpsilon >= other.MaxZ)
            {
                double d = other.MaxZ - MinZ;
                if (d <= CollideEpsilon && d > movement)
                    movement = d;

            }

        return movement;
    }

    /// <summary>Clips movement along an axis (0=X, 1=Y, 2=Z) against another AABB.</summary>
    public double Collide(int axis, Aabb other, double movement)
    {
        return axis switch
        {
            0 => CollideX(other, movement),
            1 => CollideY(other, movement),
            2 => CollideZ(other, movement),
            _ => movement,
        };
    }

    public Vec3d GetCenter() => new(
        (MinX + MaxX) * 0.5,
        (MinY + MaxY) * 0.5,
        (MinZ + MaxZ) * 0.5);

    public Vec3d GetBottomCenter() => new(
        (MinX + MaxX) * 0.5,
        MinY,
        (MinZ + MaxZ) * 0.5);

    /// <summary>The squared distance from <paramref name="point"/> to the nearest point ON OR IN this box: per axis, the gap to the nearer face, or zero when the point's coordinate already falls inside the box on that axis. Interaction range measures the distance to a block or entity box, not its centre.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double DistanceSqrTo(Vec3d point)
    {
        double dx = Math.Max(Math.Max(MinX - point.X, point.X - MaxX), 0.0);
        double dy = Math.Max(Math.Max(MinY - point.Y, point.Y - MaxY), 0.0);
        double dz = Math.Max(Math.Max(MinZ - point.Z, point.Z - MaxZ), 0.0);
        return (dx * dx) + (dy * dy) + (dz * dz);
    }

    public bool Equals(Aabb other) =>
        MinX == other.MinX && MinY == other.MinY && MinZ == other.MinZ &&
        MaxX == other.MaxX && MaxY == other.MaxY && MaxZ == other.MaxZ;

    public override bool Equals(object? obj) => obj is Aabb a && Equals(a);

    public override int GetHashCode() => HashCode.Combine(MinX, MinY, MinZ, MaxX, MaxY, MaxZ);

    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"AABB[{MinX:F3},{MinY:F3},{MinZ:F3} -> {MaxX:F3},{MaxY:F3},{MaxZ:F3}]");

    public static bool operator ==(Aabb a, Aabb b) => a.Equals(b);

    public static bool operator !=(Aabb a, Aabb b) => !a.Equals(b);
}
