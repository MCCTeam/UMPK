using System.Buffers.Binary;
using Umpk.Geometry;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>Shared LP vector values. Decode retains the existing captured-velocity fallback; packet readers establish strict framing before calling it.</summary>
internal static class LowPrecisionVec3
{
    /// <summary>The mask for one packed 15-bit coordinate.</summary>
    private const long DataBitsMask = 32767L;

    private const double MaxQuantizedValue = 32766.0;

    /// <summary>Decodes a captured block. A block that is empty, zero-flagged, or too short to hold its own header decodes to <see cref="Vec3d.Zero"/>: the zero vector is what vanilla writes for "no motion", and a truncated block has no honest value to invent.</summary>
    public static Vec3d Decode(ReadOnlySpan<byte> block)
    {
        if (block.Length == 0)
            return Vec3d.Zero;

        byte lowest = block[0];
        if (lowest == 0 || block.Length < 6)
            return Vec3d.Zero;

        byte middle = block[1];
        uint highest = BinaryPrimitives.ReadUInt32BigEndian(block[2..6]);

        long magnitude = lowest & 3;
        if ((lowest & 0x04) != 0 && TryReadVarInt(block[6..], out uint continuation))
            magnitude |= (long)continuation << 2;

        long packed = ((long)highest << 16) | ((long)middle << 8) | lowest;
        return new Vec3d(
            Unpack(packed >> 3) * magnitude,
            Unpack(packed >> 18) * magnitude,
            Unpack(packed >> 33) * magnitude);
    }

    private static double Unpack(long field)
        => (Math.Min(field & DataBitsMask, (long)MaxQuantizedValue) * 2.0 / MaxQuantizedValue) - 1.0;

    private static bool TryReadVarInt(ReadOnlySpan<byte> span, out uint value)
    {
        value = 0;
        int shift = 0;
        for (int i = 0; i < span.Length && shift <= 28; i++)
        {
            byte b = span[i];
            value |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return true;

            shift += 7;
        }

        value = 0;
        return false;
    }

    /// <summary>Writes the original LP vector quantization, sanitization and scale continuation.</summary>
    internal static void Write(ref PacketWriter writer, Vec3d value)
    {
        const double maximum = 1.7179869183E10;
        const double minimum = 3.051944088384301E-5;
        double x = Sanitize(value.X), y = Sanitize(value.Y), z = Sanitize(value.Z);
        double magnitude = Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z)));
        if (magnitude < minimum)
        {
            writer.WriteByte(0);
            return;
        }

        long scale = (long)Math.Ceiling(magnitude);
        bool continuation = (scale & 3L) != scale;
        long markers = continuation ? (scale & 3L) | 4L : scale;
        long packed = markers | (Pack(x / scale) << 3) | (Pack(y / scale) << 18) | (Pack(z / scale) << 33);
        writer.WriteByte(unchecked((byte)packed));
        writer.WriteByte(unchecked((byte)(packed >> 8)));
        writer.WriteInt(unchecked((int)(packed >> 16)));
        if (continuation)
            writer.WriteVarInt(unchecked((int)(scale >> 2)));

        static double Sanitize(double component) => double.IsNaN(component) ? 0 : Math.Clamp(component, -maximum, maximum);
        // Java Math.round uses floor(value + .5), including exact midpoint values.
        static long Pack(double component) => (long)Math.Floor((component * .5 + .5) * MaxQuantizedValue + .5);
    }
}
