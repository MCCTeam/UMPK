using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Client.Internal;

/// <summary>Decodes the 1.21.9+ low-precision quantized velocity block carried by <c>ClientboundSetEntityMotionPacket.ModernVelocityRaw</c>.</summary>
/// <remarks>
/// <para>The protocol layer captures the block as raw bytes so the frame round-trips byte-exactly, which is correct for a proxy and useless for a consumer: the packet's three short components are constructed as literal zeroes on 773+ (<c>EntityMoveCodecs.SetEntityMotionV1_21_9</c> returns <c>new ClientboundSetEntityMotionPacket(id, 0, 0, 0, lp)</c>). Reading <c>VelocityX / 8000.0</c> on those protocols therefore yields exactly zero for every entity, tracked or not.</para>
/// <para>The format starts with a byte whose low two bits are the magnitude and whose 0x04 bit says a VarInt carrying the magnitude's high bits follows; then a second byte and a big-endian unsigned int; and three 15-bit fields packed at bit offsets 3, 18 and 33 of the little-endian assembly of those six bytes, each unpacked to [-1, 1] and multiplied by the magnitude. A leading byte of 0 is the zero vector and the whole block is that one byte.</para>
/// </remarks>
internal static class LowPrecisionVelocity
{
    /// <summary>Decodes the existing captured velocity representation through its shared protocol owner.</summary>
    public static Vec3d Decode(ReadOnlySpan<byte> block) => LowPrecisionVec3.Decode(block);
}
