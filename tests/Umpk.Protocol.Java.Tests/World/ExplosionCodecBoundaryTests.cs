using System.Buffers;
using System.Buffers.Binary;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.World;

/// <summary>The six <c>minecraft:explode</c> wire eras, resolved through the REGISTRAR by protocol number and driven with hand-authored wire bytes.</summary>
/// <remarks>
/// <para>The assertions here are deliberately frame-LENGTH and cross-era-rejection shaped rather than round-trip shaped. A round-trip through a wrong codec agrees with itself; only asking "how many bytes does the era the registrar picked consume?" and "does a neighbouring era's frame get rejected?" can see a misbinding.</para>
/// <para>
/// The six wire contracts are:
/// <list type="bullet">
/// <item>47-754: three center floats, power float, int count, three bytes per record, and three motion floats.</item>
/// <item>755-760: the same fields, except the record count uses a VarInt prefix.</item>
/// <item>761-764: the center widens from three floats to three doubles.</item>
/// <item>765-767: block interaction, two particles, and a sound follow the existing fields.</item>
/// <item>768-772: a four-member composite: Vec3, optional Vec3, particle, and sound.</item>
/// <item>773-776: a seven-member composite: Vec3, float radius, int block count, optional Vec3,
/// particle, sound, and a weighted list of explosion particle information.</item>
/// </list>
/// </para>
/// <para>Protocol 773 begins the seven-member form. This boundary is pinned by <see cref="SevenSeventyTwoAndSevenSeventyThree_AreDifferentEras"/>. The particle registry and item-component tables INSIDE the 768+ codecs are a separate axis with its own suite; see <c>ExplodeParticleTableTests</c>.</para>
/// </remarks>
public sealed class ExplosionCodecBoundaryTests
{
    private const string Explode = "minecraft:explode";

    // One protocol per era, plus both ends of each era so a boundary that slips by one is visible.
    private const int Era1Low = 47;      // 1.8
    private const int Era1High = 754;    // 1.16.5
    private const int Era2Low = 755;     // 1.17
    private const int Era2High = 760;    // 1.19.2
    private const int Era3Low = 761;     // 1.19.3
    private const int Era3High = 764;    // 1.20.2
    private const int Era4Low = 765;     // 1.20.3 / 1.20.4
    private const int Era4High = 767;    // 1.21 / 1.21.1
    private const int Era5Low = 768;     // 1.21.2
    private const int Era5High = 772;    // 1.21.8
    private const int Era6Low = 773;     // 1.21.9
    private const int Era6High = 776;    // 26.2

    /// <summary>A zero-block explosion is a different LENGTH in each of the three legacy eras, which is what makes a misbinding visible at all: 12+4+4+12 = 32 with an int count, 29 with a VarInt count, and 41 once the center widens to doubles.</summary>
    [Theory]
    [InlineData(Era1Low, 32)]
    [InlineData(Era1High, 32)]
    [InlineData(Era2Low, 29)]
    [InlineData(Era2High, 29)]
    [InlineData(Era3Low, 41)]
    [InlineData(Era3High, 41)]
    public void ZeroBlockExplosion_HasTheWireLayoutFrameLength(int protocol, int expectedLength)
    {
        byte[] frame = LegacyFrame(protocol, blocks: []);
        Assert.Equal(expectedLength, frame.Length);

        var decoded = (ClientboundExplodePacket)Decode(protocol, frame);
        Assert.Equal(10.5, decoded.Center.X, 3);
        Assert.Equal(64.0, decoded.Center.Y, 3);
        Assert.Equal(-20.5, decoded.Center.Z, 3);
        Assert.Equal(4.0f, decoded.LegacyStrength);
        Assert.Empty(decoded.LegacyBlocks);
        Assert.Equal(0.25f, decoded.LegacyMotionX);
        Assert.Equal(-0.5f, decoded.LegacyMotionY);
        Assert.Equal(0.75f, decoded.LegacyMotionZ);
    }

    /// <summary>The destroyed-block offsets survive on every legacy era, count prefix notwithstanding.</summary>
    [Theory]
    [InlineData(Era1Low)]
    [InlineData(Era1High)]
    [InlineData(Era2Low)]
    [InlineData(Era2High)]
    [InlineData(Era3Low)]
    [InlineData(Era3High)]
    public void BlockOffsets_Decode_OnEveryLegacyWireLayout(int protocol)
    {
        ExplosionBlock[] blocks = [new(1, -2, 3), new(-4, 5, -6)];
        var decoded = (ClientboundExplodePacket)Decode(protocol, LegacyFrame(protocol, blocks));
        Assert.Equal(blocks, decoded.LegacyBlocks);
    }

    /// <summary>Cross-era rejection: a frame built for one era must not be accepted by the era next door. This is the assertion that distinguishes adjacent bindings with incompatible wire shapes.</summary>
    [Theory]
    [InlineData(Era1High, Era2Low)]   // int count vs VarInt count
    [InlineData(Era2Low, Era1High)]
    [InlineData(Era2High, Era3Low)]   // float center vs double center
    [InlineData(Era3Low, Era2High)]
    [InlineData(Era3High, Era5Low)]   // legacy body vs the 1.21.2 composite
    [InlineData(Era1Low, Era5Low)]
    public void AFrameFromOneWireLayout_IsRejectedByItsNeighbour(int builtFor, int decodedOn)
    {
        byte[] frame = LegacyFrame(builtFor, blocks: [new(1, -2, 3), new(-4, 5, -6)]);

        // Frame-exactness makes "consumed a different number of bytes" a fault rather than a silent wrong value, so the wrong era either throws or lands on different field values.
        bool rejected;
        try
        {
            var decoded = (ClientboundExplodePacket)Decode(decodedOn, frame);
            rejected = Math.Abs(decoded.Center.X - 10.5) > 1e-6;
        }
        catch (Exception ex) when (ex is ProtocolViolationException or ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            rejected = true;
        }

        Assert.True(rejected, $"a protocol-{builtFor} explosion frame was silently accepted on protocol {decodedOn}");
    }

    /// <summary>765-767 keep every field ahead of the particle group decoded, and carry the group verbatim so the frame round-trips byte-exactly.</summary>
    [Theory]
    [InlineData(Era4Low)]
    [InlineData(766)]
    [InlineData(Era4High)]
    public void BlockInteractionAndTail_SurviveOnTheTwelveTwentyThreeWireLayout(int protocol)
    {
        ExplosionBlock[] blocks = [new(1, -2, 3)];
        var body = new ArrayBufferWriter<byte>();
        body.Write(LegacyFrame(Era3Low, blocks));           // the 761 body is the 765 prefix
        body.Write(new byte[] { 0x02 });                    // VarInt block interaction = DESTROY_WITH_DECAY
        byte[] tail = [0x00, 0x00, 0x0A, 0x62, 0x6F, 0x6F]; // two particles + an inline sound, opaque here
        body.Write(tail);

        byte[] frame = body.WrittenSpan.ToArray();
        var decoded = (ClientboundExplodePacket)Decode(protocol, frame);

        Assert.Equal(10.5, decoded.Center.X, 3);
        Assert.Equal(blocks, decoded.LegacyBlocks);
        Assert.Equal(2, decoded.BlockInteraction);
        Assert.Equal(tail, decoded.ParticleSoundTail);

        // Byte-exact re-encode: a raw-carried group is only honest if it goes back out unchanged.
        Assert.Equal(frame, Encode(protocol, decoded));
    }

    /// <summary>The legacy eras report no block interaction rather than a plausible-looking zero.</summary>
    [Theory]
    [InlineData(Era1Low)]
    [InlineData(Era2Low)]
    [InlineData(Era3Low)]
    public void LegacyWireLayouts_ReportNoBlockInteraction(int protocol)
    {
        var decoded = (ClientboundExplodePacket)Decode(protocol, LegacyFrame(protocol, blocks: []));
        Assert.Equal(-1, decoded.BlockInteraction);
        Assert.True(decoded.ParticleSoundTail is null or { Length: 0 });
    }

    /// <summary>Every protocol from 773 up carries the radius / block-count / weighted-list wire, not the 1.21.2 one. All four protocols have to accept this frame.</summary>
    [Theory]
    [InlineData(Era6Low)]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(Era6High)]
    public void RadiusWireLayout_CarriesRadiusBlockCountAndWeightedList(int protocol)
    {
        var packet = new ClientboundExplodePacket(
            new Vec3d(5, 6, 7), 0f, [], 0f, 0f, 0f,
            new Vec3d(0.1, 0.2, 0.3), new ParticleData(0, []), new SoundEventHolder(100, null, null),
            Radius: 3.5f, BlockCount: 42,
            BlockParticles: [new(new ParticleData(0, []), 1.0f, 0.5f, 3)]);

        byte[] frame = Encode(protocol, packet);
        var decoded = (ClientboundExplodePacket)Decode(protocol, frame);

        Assert.Equal(3.5f, decoded.Radius);
        Assert.Equal(42, decoded.BlockCount);
        Assert.Equal(3, Assert.Single(decoded.BlockParticles).Weight);
    }

    /// <summary>768-772 is the four-field composite and does NOT carry radius/block count, so 772 and 773 must resolve different codecs. This is the boundary the record got wrong twice: first collapsed onto 477, then corrected to 775 when it is really 773.</summary>
    /// <remarks>Protocol 772 carries <c>Vec3, optional Vec3, particle, sound</c>. Protocol 773 adds a float, an int, and a weighted list of explosion particle information. A frame-length assertion is used rather than a round trip because a round trip through the wrong era agrees with itself; only the width can see a codec that is three fields short.</remarks>
    [Fact]
    public void SevenSeventyTwoAndSevenSeventyThree_AreDifferentWireLayouts()
    {
        var packet = new ClientboundExplodePacket(
            new Vec3d(5, 6, 7), 0f, [], 0f, 0f, 0f,
            new Vec3d(0.1, 0.2, 0.3), new ParticleData(0, []), new SoundEventHolder(100, null, null),
            Radius: 3.5f, BlockCount: 42, BlockParticles: []);

        byte[] onSevenSeventyTwo = Encode(Era5High, packet);
        byte[] onSevenSeventyThree = Encode(Era6Low, packet);

        // 773 adds a float radius, an int block count and a weighted-list count on top of the 772 body.
        Assert.Equal(onSevenSeventyTwo.Length + 4 + 4 + 1, onSevenSeventyThree.Length);

        // And the four-member body is not merely narrower, it is unreadable on 773: the seven-member codec runs past the end of it.
        Assert.Throws<ProtocolViolationException>(() =>
            BoundCodec.At(Era6Low, PacketFlow.Clientbound, Explode)
                .Decode(onSevenSeventyTwo, PacketCodecContext.Registryless));
    }

    // Frame construction.

    /// <summary>Builds a canonical explosion frame for the legacy era that <paramref name="protocol"/> belongs to, straight from the vanilla read order. Deliberately hand-rolled rather than produced by the codec under test: bytes an encoder made cannot disagree with the decoder that made them.</summary>
    private static byte[] LegacyFrame(int protocol, ExplosionBlock[] blocks)
    {
        var buffer = new ArrayBufferWriter<byte>();
        bool doubleCenter = protocol >= Era3Low;
        bool varIntCount = protocol >= Era2Low;

        if (doubleCenter)
        {
            WriteDouble(buffer, 10.5);
            WriteDouble(buffer, 64.0);
            WriteDouble(buffer, -20.5);
        }
        else
        {
            WriteFloat(buffer, 10.5f);
            WriteFloat(buffer, 64.0f);
            WriteFloat(buffer, -20.5f);
        }

        WriteFloat(buffer, 4.0f);

        if (varIntCount)
        {
            Assert.InRange(blocks.Length, 0, 127); // single-byte VarInt keeps the length arithmetic honest
            buffer.Write(new byte[] { (byte)blocks.Length });
        }
        else
        {
            Span<byte> count = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(count, blocks.Length);
            buffer.Write(count);
        }

        foreach (ExplosionBlock block in blocks)
            buffer.Write(new[] { unchecked((byte)block.Dx), unchecked((byte)block.Dy), unchecked((byte)block.Dz) });

        WriteFloat(buffer, 0.25f);
        WriteFloat(buffer, -0.5f);
        WriteFloat(buffer, 0.75f);
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteFloat(ArrayBufferWriter<byte> buffer, float value)
    {
        Span<byte> scratch = stackalloc byte[4];
        BinaryPrimitives.WriteSingleBigEndian(scratch, value);
        buffer.Write(scratch);
    }

    private static void WriteDouble(ArrayBufferWriter<byte> buffer, double value)
    {
        Span<byte> scratch = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(scratch, value);
        buffer.Write(scratch);
    }

    private static object Decode(int protocol, byte[] frame) =>
        BoundCodec.At(protocol, PacketFlow.Clientbound, Explode)
            .Decode(frame, PacketCodecContext.Registryless);

    private static byte[] Encode(int protocol, ClientboundExplodePacket packet)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        BoundCodec.At(protocol, PacketFlow.Clientbound, Explode)
            .Encode(ref writer, packet, PacketCodecContext.Registryless);
        return buffer.WrittenSpan.ToArray();
    }
}
