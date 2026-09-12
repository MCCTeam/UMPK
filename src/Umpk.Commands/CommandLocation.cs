using Umpk.Geometry;

namespace Umpk.Commands;

/// <summary>A parsed coordinate triple: three absolute values, three player-relative (<c>~</c>) offsets, or a fully local (<c>^</c>) triple resolved against a facing direction. Produced by <see cref="Arguments.Location"/>.</summary>
/// <param name="X">The X value: an offset on <see cref="IsRelativeX"/> when <see cref="IsLocal"/> is <c>false</c>; the local "left" component when <see cref="IsLocal"/> is <c>true</c>.</param>
/// <param name="Y">The Y value: an offset on <see cref="IsRelativeY"/> when <see cref="IsLocal"/> is <c>false</c>; the local "up" component when <see cref="IsLocal"/> is <c>true</c>.</param>
/// <param name="Z">The Z value: an offset on <see cref="IsRelativeZ"/> when <see cref="IsLocal"/> is <c>false</c>; the local "forwards" component when <see cref="IsLocal"/> is <c>true</c>.</param>
/// <param name="IsLocal">Whether this is a <c>^ ^ ^</c> local triple rather than an absolute/relative one. A local triple has no meaning without a facing, so resolving it requires <see cref="ToAbsolute(Vec3d, float, float)"/>; <see cref="ToAbsolute(Vec3d)"/> throws for it.</param>
/// <param name="Relativity">Bit flags, meaningful only when <paramref name="IsLocal"/> is <c>false</c>: bit 0 (0b001) means X is <c>~</c>-relative, bit 1 (0b010) means Y is <c>~</c>-relative, bit 2 (0b100) means Z is <c>~</c>-relative. Always 0 for a local triple; its own "relative to the player" character is carried by <see cref="IsLocal"/>, a different relativity concept from the tilde bitmask.</param>
public readonly record struct CommandLocation(double X, double Y, double Z, bool IsLocal, byte Relativity)
{
    /// <summary>Whether the X component is <c>~</c>-relative. Always <c>false</c> for a local triple.</summary>
    public bool IsRelativeX => (Relativity & 0b001) != 0;

    /// <summary>Whether the Y component is <c>~</c>-relative. Always <c>false</c> for a local triple.</summary>
    public bool IsRelativeY => (Relativity & 0b010) != 0;

    /// <summary>Whether the Z component is <c>~</c>-relative. Always <c>false</c> for a local triple.</summary>
    public bool IsRelativeZ => (Relativity & 0b100) != 0;

    /// <summary>Resolves an absolute/relative triple to a world position. Throws for a local (<c>^</c>) triple, deliberately: silently resolving <c>^ ^ ^</c> against an implicit zero facing would return a position nobody asked for, so a local triple must be resolved through <see cref="ToAbsolute(Vec3d, float, float)"/> with the caller's real facing instead.</summary>
    /// <exception cref="InvalidOperationException">This triple is local (<see cref="IsLocal"/>).</exception>
    public Vec3d ToAbsolute(Vec3d current)
    {
        if (IsLocal)
            throw new InvalidOperationException(
                "A local (^) coordinate has no meaning without a facing; call " +
                "ToAbsolute(Vec3d, float, float) instead.");

        return ResolveWorld(current);
    }

    /// <summary>Resolves this triple to a world position. For a local triple, resolves the <c>left</c>/<c>up</c>/ <c>forwards</c> components against the orthogonal basis derived from <paramref name="yaw"/> and <paramref name="pitch"/>. The calculation uses double precision throughout. For an absolute/relative triple, <paramref name="yaw"/> and <paramref name="pitch"/> are unused.</summary>
    public Vec3d ToAbsolute(Vec3d origin, float yaw, float pitch)
    {
        if (!IsLocal)
            return ResolveWorld(origin);

        const double DegToRad = Math.PI / 180.0;
        double f = Math.Cos((yaw + 90.0) * DegToRad);
        double g = Math.Sin((yaw + 90.0) * DegToRad);
        double h = Math.Cos(-pitch * DegToRad);
        double i = Math.Sin(-pitch * DegToRad);
        double j = Math.Cos((-pitch + 90.0) * DegToRad);
        double k = Math.Sin((-pitch + 90.0) * DegToRad);

        var forward = new Vec3d(f * h, i, g * h);
        var up = new Vec3d(f * j, k, g * j);
        var left = Cross(forward, up).Scale(-1.0);

        // X=left, Y=up, Z=forwards. Combine the three basis-vector contributions before adding the resulting offset to the origin.
        var offset = forward.Scale(Z) + up.Scale(Y) + left.Scale(X);
        return origin + offset;
    }

    /// <summary>Resolves this triple to a block position via <see cref="ToAbsolute(Vec3d)"/>; throws under the same condition (a local triple, since it has no facing to resolve against here).</summary>
    /// <exception cref="InvalidOperationException">This triple is local (<see cref="IsLocal"/>).</exception>
    public BlockPos ToBlockPos(Vec3d current) => BlockPos.Containing(ToAbsolute(current));

    private Vec3d ResolveWorld(Vec3d current) => new(
        IsRelativeX ? current.X + X : X,
        IsRelativeY ? current.Y + Y : Y,
        IsRelativeZ ? current.Z + Z : Z);

    private static Vec3d Cross(Vec3d a, Vec3d b) => new(
        (a.Y * b.Z) - (a.Z * b.Y),
        (a.Z * b.X) - (a.X * b.Z),
        (a.X * b.Y) - (a.Y * b.X));
}
