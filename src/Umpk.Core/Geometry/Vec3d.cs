using System.Runtime.CompilerServices;

namespace Umpk.Geometry;

/// <summary>An immutable three-dimensional vector of doubles.</summary>
public readonly struct Vec3d : IEquatable<Vec3d>
{
    public static readonly Vec3d Zero = new(0, 0, 0);

    public readonly double X;
    public readonly double Y;
    public readonly double Z;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vec3d(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vec3d Add(double x, double y, double z) => new(X + x, Y + y, Z + z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vec3d Add(Vec3d other) => new(X + other.X, Y + other.Y, Z + other.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vec3d Subtract(Vec3d other) => new(X - other.X, Y - other.Y, Z - other.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vec3d Subtract(double x, double y, double z) => new(X - x, Y - y, Z - z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vec3d Scale(double factor) => new(X * factor, Y * factor, Z * factor);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vec3d Multiply(double x, double y, double z) => new(X * x, Y * y, Z * z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vec3d Multiply(Vec3d other) => new(X * other.X, Y * other.Y, Z * other.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double LengthSqr() => X * X + Y * Y + Z * Z;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Length() => Math.Sqrt(LengthSqr());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double HorizontalDistanceSqr() => X * X + Z * Z;

    /// <summary>Normalizes to unit length; vectors shorter than 1.0E-5 collapse to <see cref="Zero"/> (vanilla semantics).</summary>
    /// <remarks>
    /// <para>The collapse threshold is <c>1.0E-4</c> from 1.14.4 through 1.21.1 and <c>1.0E-5F</c> from 1.21.2 onward, so this is exact for the modern era and still under-collapses the older one by a factor of ten. It is a deliberate single value: a protocol has no business reaching a geometry primitive, and making it era-aware is a dataset-axis decision rather than a constant.</para>
    /// <para>The threshold is load-bearing in exactly one place. <c>PlayerPhysics.ApplyCurrent</c> normalizes a nearly-cancelled sum of per-cell flows and scales it back up to the 0.0045 minimum push, which is above the 0.003 velocity-zeroing threshold. Under the old 1.0E-7 an impulse in the 1e-7 to 1e-5 window came back out as a REAL push, so a body vanilla leaves stationary drifted.</para>
    /// </remarks>
    public Vec3d Normalize()
    {
        double len = Length();
        return len < 1.0E-5 ? Zero : new Vec3d(X / len, Y / len, Z / len);
    }

    /// <summary>Gets a component by axis index: 0=X, 1=Y, 2=Z.</summary>
    public double Get(int axis) => axis switch
    {
        0 => X,
        1 => Y,
        2 => Z,
        _ => throw new ArgumentOutOfRangeException(nameof(axis)),
    };

    /// <summary>Returns a new vector with one axis replaced.</summary>
    public Vec3d With(int axis, double value) => axis switch
    {
        0 => new Vec3d(value, Y, Z),
        1 => new Vec3d(X, value, Z),
        2 => new Vec3d(X, Y, value),
        _ => throw new ArgumentOutOfRangeException(nameof(axis)),
    };

    public bool Equals(Vec3d other) =>
        X == other.X && Y == other.Y && Z == other.Z;

    public override bool Equals(object? obj) =>
        obj is Vec3d other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(X, Y, Z);

    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"({X:F4}, {Y:F4}, {Z:F4})");

    public static bool operator ==(Vec3d a, Vec3d b) => a.Equals(b);

    public static bool operator !=(Vec3d a, Vec3d b) => !a.Equals(b);

    public static Vec3d operator +(Vec3d a, Vec3d b) => a.Add(b);

    public static Vec3d operator -(Vec3d a, Vec3d b) => a.Subtract(b);

    public static Vec3d operator *(Vec3d a, double s) => a.Scale(s);

    public static Vec3d operator -(Vec3d a) => new(-a.X, -a.Y, -a.Z);
}
