using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Internal;

/// <summary>Native modern teleport target arithmetic, independent of controller and packet-base ownership.</summary>
internal readonly record struct EntityTeleportProfile(bool ClampPitchBeforeRotation, bool WideTrigIndex)
{
    public static EntityTeleportProfile ForProtocol(int protocol) => new(protocol >= 770, protocol >= 774);

    public PositionMoveRotation Resolve(PositionMoveRotation source, PositionMoveRotation change, int flags)
    {
        var position = new Vec3d(
            ((flags & 1) != 0 ? source.Position.X : 0.0) + change.Position.X,
            ((flags & 2) != 0 ? source.Position.Y : 0.0) + change.Position.Y,
            ((flags & 4) != 0 ? source.Position.Z : 0.0) + change.Position.Z);
        float yaw = ((flags & 8) != 0 ? source.YRot : 0.0F) + change.YRot;
        float pitch = ((flags & 16) != 0 ? source.XRot : 0.0F) + change.XRot;
        if (ClampPitchBeforeRotation)
            pitch = Math.Clamp(pitch, -90.0F, 90.0F);
        Vec3d current = source.DeltaMovement;
        if ((flags & 256) != 0)
        {
            float x = (float)((source.XRot - pitch) * (Math.PI / 180.0));
            float y = (float)((source.YRot - yaw) * (Math.PI / 180.0));
            (float cosX, float sinX) = SinCos(x);
            current = new Vec3d(current.X, (current.Y * cosX) + (current.Z * sinX), (current.Z * cosX) - (current.Y * sinX));
            (float cosY, float sinY) = SinCos(y);
            current = new Vec3d((current.X * cosY) + (current.Z * sinY), current.Y, (current.Z * cosY) - (current.X * sinY));
        }
        var movement = new Vec3d(
            (flags & 32) != 0 ? current.X + change.DeltaMovement.X : change.DeltaMovement.X,
            (flags & 64) != 0 ? current.Y + change.DeltaMovement.Y : change.DeltaMovement.Y,
            (flags & 128) != 0 ? current.Z + change.DeltaMovement.Z : change.DeltaMovement.Z);
        return new PositionMoveRotation(position, movement, yaw, pitch);
    }

    private (float Cos, float Sin) SinCos(float radians)
    {
        long sinIndex;
        long cosIndex;
        if (WideTrigIndex)
        {
            double scaled = radians * 10430.378350470453;
            sinIndex = JavaLong(scaled);
            cosIndex = JavaLong(scaled + 16384.0);
        }
        else
        {
            float scaled = radians * 10430.378F;
            sinIndex = JavaInt(scaled);
            cosIndex = JavaInt(scaled + 16384.0F);
        }
        return (Sine.Values[(int)(cosIndex & 65535)], Sine.Values[(int)(sinIndex & 65535)]);
    }

    private static int JavaInt(float value)
        => float.IsNaN(value) ? 0 : value >= 2147483648.0F ? int.MaxValue : value <= -2147483648.0F ? int.MinValue : (int)value;

    private static long JavaLong(double value)
        => double.IsNaN(value) ? 0 : value >= 9223372036854775808.0 ? long.MaxValue : value <= -9223372036854775808.0 ? long.MinValue : (long)value;

    private static class Sine
    {
        internal static readonly float[] Values = Create();

        private static float[] Create()
        {
            var values = new float[65536];
            for (int index = 0; index < values.Length; index++)
                values[index] = (float)Math.Sin(index * Math.PI * 2.0 / 65536.0);
            return values;
        }
    }
}
