using Umpk.Geometry;

namespace Umpk.Client.Movement;

/// <summary>Vanilla's own "the player moved" threshold, applied to a from/to pair of tracked positions.</summary>
/// <remarks>A position packet is sent when squared displacement exceeds the configured threshold, and is separately re-sent every 20 ticks regardless of movement. This type answers only the "did it move" half of that rule: a settled client compares as unmoved and a walking one compares as moved on effectively every tick, without inventing a client-specific epsilon.</remarks>
internal static class PositionReportPolicy
{
    /// <summary>Vanilla's own significance threshold, as squared distance.</summary>
    internal const double SignificanceSquared = 2.0E-4 * 2.0E-4;

    /// <summary>Whether <paramref name="to"/> moved far enough from <paramref name="from"/> to be worth a packet.</summary>
    internal static bool HasMoved(Vec3d from, Vec3d to)
    {
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double dz = to.Z - from.Z;
        return (dx * dx) + (dy * dy) + (dz * dz) > SignificanceSquared;
    }
}
