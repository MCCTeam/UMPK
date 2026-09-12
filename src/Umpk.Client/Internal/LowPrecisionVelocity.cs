using System.Buffers.Binary;
using Umpk.Geometry;

namespace Umpk.Client.Internal;

/// <summary>Decodes the 1.21.9+ low-precision quantized velocity block carried by <c>ClientboundSetEntityMotionPacket.ModernVelocityRaw</c>.</summary>
/// <remarks>
/// <para>The protocol layer captures the block as raw bytes so the frame round-trips byte-exactly, which is correct for a proxy and useless for a consumer: the packet's three short components are constructed as literal zeroes on 773+ (<c>EntityMoveCodecs.SetEntityMotionV1_21_9</c> returns <c>new ClientboundSetEntityMotionPacket(id, 0, 0, 0, lp)</c>). Reading <c>VelocityX / 8000.0</c> on those protocols therefore yields exactly zero for every entity, tracked or not.</para>
/// <para>The format starts with a byte whose low two bits are the magnitude and whose 0x04 bit says a VarInt carrying the magnitude's high bits follows; then a second byte and a big-endian unsigned int; and three 15-bit fields packed at bit offsets 3, 18 and 33 of the little-endian assembly of those six bytes, each unpacked to [-1, 1] and multiplied by the magnitude. A leading byte of 0 is the zero vector and the whole block is that one byte.</para>
/// </remarks>
internal static class LowPrecisionVelocity
{
    /// <summary>The unit magnitude below which vanilla writes the single zero byte.</summary>
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
}
