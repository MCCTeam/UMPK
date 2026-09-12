using System.Buffers;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Entity;

/// <summary>
/// Byte-exact pins for the <c>minecraft:player_position</c> relative bitset.
/// <para>The relative-motion set runs to bit 8 (<c>ROTATE_DELTA</c>) and occupies a big-endian int. The modern codec decoded that int and then NARROWED it into a byte field, so every bit above 7 was dropped and a frame carrying <c>ROTATE_DELTA</c> did not round-trip byte-exactly. Nothing caught it because the position maths only ever read bits 0-4 and every existing pin used a bitset that fitted in a byte.</para>
/// <para>The frames here are built field by field from the wire contract, never by running the encoder, and they go through <see cref="BoundCodec"/> so the assertion covers the codec the registrar actually selects for the protocol rather than one the test named.</para>
/// </summary>
public sealed class PlayerPositionRelativeFlagsTests
{
    private const string PlayerPosition = "player_position";

    /// <summary>Rotation delta, bit 8: the first relative-motion bit a byte field cannot hold.</summary>
    private const int RotateDelta = 1 << 8;

    /// <summary>Every relative-motion value at once: bits 0-8 packed.</summary>
    private const int AllRelatives = 0x1FF;

    /// <summary>The protocols bound to the modern int-bitset form (1.21.2 onward).</summary>
    public static TheoryData<int> ModernProtocols => [768, 770, 772, 776];

    /// <summary>The modern frame's length, derived from vanilla's field arithmetic rather than from our own encoder: VarInt teleport id (2 bytes for 4242) + Vec3 position (3 doubles, 24) + Vec3 deltaMovement (3 doubles, 24) + yRot float (4) + xRot float (4) + the relative bitset as a big-endian int (4) = 62. A byte-narrowed bitset makes this 59.</summary>
    private const int ModernFrameLength = 62;

    [Theory]
    [MemberData(nameof(ModernProtocols))]
    public void ModernPlayerPosition_WithRotateDelta_RoundTripsByteExactly(int protocol)
    {
        byte[] frame = BuildModernFrame(AllRelatives);

        // Frame LENGTH, asserted independently of the codec. A round trip through a narrowed field agrees with itself, so length is what actually distinguishes the int bitset from a byte one: this pins the hand-built frame against vanilla's layout before the codec ever sees it.
        Assert.Equal(ModernFrameLength, frame.Length);

        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, PlayerPosition);
        var decoded = Assert.IsType<ClientboundPlayerPositionPacket>(bound.DecodeFrame(frame));

        // The whole bitset survived the decode: bit 8 is the one a byte field silently ate.
        Assert.Equal(AllRelatives, decoded.RelativeFlags);
        Assert.NotEqual(0, decoded.RelativeFlags & RotateDelta);

        // And the re-encoded frame is the same bytes, which is what a byte field could not produce. The length is asserted on the ENCODER output too, so a narrowed writer fails here even if the reader were left intact.
        byte[] reencoded = bound.Encode(decoded);
        Assert.Equal(ModernFrameLength, reencoded.Length);
        Assert.Equal(frame, reencoded);
    }

    /// <summary>The bits above 7 are carried individually, not just as part of a saturated bitset: a mask that keeps only <c>ROTATE_DELTA</c> and the three <c>DELTA_*</c> bits is the shape a teleport that hands the client rotated momentum actually uses.</summary>
    [Fact]
    public void ModernPlayerPosition_DeltaOnlyBitset_RoundTripsByteExactly()
    {
        // Delta x, y, z, and rotation occupy bits 5 through 8.
        const int DeltaSet = (1 << 5) | (1 << 6) | (1 << 7) | (1 << 8);
        byte[] frame = BuildModernFrame(DeltaSet);

        BoundPacketCodec bound = BoundCodec.At(770, PacketFlow.Clientbound, PlayerPosition);
        var decoded = Assert.IsType<ClientboundPlayerPositionPacket>(bound.DecodeFrame(frame));

        Assert.Equal(DeltaSet, decoded.RelativeFlags);
        Assert.Equal(frame, bound.Encode(decoded));
    }

    /// <summary>The pre-1.21.2 wires genuinely carry one byte with five relative-motion values, so widening the field must not widen those frames. A bitset that fits still round-trips byte-exactly, and one that does not is rejected instead of being truncated into a different frame.</summary>
    [Theory]
    [InlineData(47)]
    [InlineData(340)]
    [InlineData(755)]
    [InlineData(762)]
    public void LegacyPlayerPosition_KeepsTheSingleFlagByte(int protocol)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, PlayerPosition);

        // 0x1F sets x, y, z, y_rot, and x_rot on those eras.
        var fits = new ClientboundPlayerPositionPacket(
            1.0, 64.0, -3.0, 90f, 12f, 0x1F, protocol == 47 ? null : 7, null);
        byte[] frame = bound.Encode(fits);
        var decoded = Assert.IsType<ClientboundPlayerPositionPacket>(bound.DecodeFrame(frame));
        Assert.Equal(0x1F, decoded.RelativeFlags);
        Assert.Equal(frame, bound.Encode(decoded));

        var overflows = fits with { RelativeFlags = AllRelatives };
        Assert.Throws<ProtocolViolationException>(() => bound.Encode(overflows));
    }

    /// <summary>A 1.21.2+ <c>player_position</c> frame: VarInt teleport id, then Vec3 position, Vec3 delta movement, float yRot, float xRot, then the relative bitset as a big-endian int.</summary>
    private static byte[] BuildModernFrame(int relatives)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);

        w.WriteVarInt(4242);            // teleport id

        w.WriteDouble(128.5);           // position
        w.WriteDouble(71.0);
        w.WriteDouble(-64.5);
        w.WriteDouble(0.25);            // delta movement, deliberately non-zero on every axis
        w.WriteDouble(-0.5);
        w.WriteDouble(0.125);
        w.WriteFloat(45f);              // yRot
        w.WriteFloat(-11f);             // xRot

        w.WriteInt(relatives);
        return buffer.WrittenSpan.ToArray();
    }
}
