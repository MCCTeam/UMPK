using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Internal;

/// <summary>Native packet-coordinate decoding, independent of visual interpolation and entity dimensions.</summary>
internal readonly record struct EntityPacketPosition(int UnitsPerBlock, bool PreserveStationaryAxes, bool RoundToNearest, bool RotationUsesPacketBase, bool TeleportResetsPacketBase)
{
    public static EntityPacketPosition ForProtocol(int protocol)
        => new(protocol < 107 ? 32 : 4096, protocol >= 751, protocol >= 761, protocol < 751, protocol < 768);

    public Vec3d Decode(Vec3d position, int x, int y, int z)
        => new(DecodeAxis(position.X, x), DecodeAxis(position.Y, y), DecodeAxis(position.Z, z));

    public Vec3d Decode(Vec3d position, IReadOnlyList<EntityMoveStep> steps, short x, short y, short z)
    {
        if (steps.Count == 0)
            return Decode(position, x, y, z);
        // VecDelta.Stepped updates the temporary codec base after each step. Summing first loses quantization when opposing steps cancel.
        foreach (EntityMoveStep step in steps)
            position = Decode(position, step.DeltaX, step.DeltaY, step.DeltaZ);
        return position;
    }

    private double DecodeAxis(double position, int delta)
    {
        if (delta == 0 && PreserveStationaryAxes)
            return position;
        double scaled = position * UnitsPerBlock;
        long encoded;
        if (RoundToNearest)
        {
            double floor = Math.Floor(scaled);
            // Java Math.round selects the upper integer at a tie. Adding 0.5 first incorrectly rounds the double immediately below +0.5.
            encoded = TruncateJava(scaled - floor == 0.5 ? floor + 1 : Math.Round(scaled));
        }
        else
        {
            long truncated = TruncateJava(scaled);
            encoded = scaled < truncated ? unchecked(truncated - 1) : truncated;
        }
        long next = UnitsPerBlock == 32 ? unchecked((int)encoded + delta) : unchecked(encoded + delta);
        return next / (double)UnitsPerBlock;
    }

    private static long TruncateJava(double value)
        => double.IsNaN(value) ? 0 : value >= 9223372036854775808.0 ? long.MaxValue : value <= -9223372036854775808.0 ? long.MinValue : (long)value;
}
