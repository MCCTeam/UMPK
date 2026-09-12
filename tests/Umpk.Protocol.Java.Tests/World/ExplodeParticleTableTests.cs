using System.Buffers;
using System.Buffers.Binary;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.World;

/// <summary>The particle-registry axis inside <c>minecraft:explode</c>, which is independent of the packet frame and must be selected independently for each ordering and component-table boundary.</summary>
/// <remarks>
/// <para>From 1.21.2 the explosion frame carries a PARTICLE mid-body, so it inherits both of the era axes <c>ParticleCodec</c> documents. The particle_type registry orderings group as 766=767 (109 entries), 768 (111), 769 (112), 770=771=772 (114), 773=774 (115), 775 (117), 776 (125) - seven distinct orderings across the band the two collapsed codecs covered. An id that names a payload-carrying particle on one ordering and a payload-free one on its neighbour makes the reader consume the wrong number of bytes and then read the sound holder from the wrong offset, and decode is frame-exact, so that is a session kill rather than a wrong value.</para>
/// <para>The 775 case was fatal for the ORDINARY explosion, not just an exotic one: <c>minecraft:explosion_emitter</c> is id 22 and <c>minecraft:explosion</c> is id 23 on 775, and the 776 table names those two ids <c>dust_color_transition</c> (an int/int/float payload, 12 bytes) and <c>effect</c> (an int/float payload, 8 bytes). Every TNT block on 26.1 sends one of the two.</para>
/// <para>Assertions here are frame-LENGTH and cross-era-rejection shaped. A round trip cannot see a table misbinding, because encode captures the option bytes the decode consumed and hands them back verbatim: the two agree with each other no matter which table is bound.</para>
/// </remarks>
public sealed class ExplodeParticleTableTests
{
    private const string Explode = "minecraft:explode";

    /// <summary>One case per era whose particle table this suite pins, chosen so the id under test carries a FIXED-WIDTH payload on that era. Reading the width right is only possible with the era's own table, so the decoded option-byte count is the whole assertion.</summary>
    public static TheoryData<int, int, int, string> EraParticles => new()
    {
        // protocol, particle id, option bytes on that era, the particle that id names there
        { 768, 35, 4, "minecraft:sculk_charge" },   // Float roll
        { 769, 36, 4, "minecraft:sculk_charge" },   // the same particle, shifted by one
        { 770, 37, 4, "minecraft:sculk_charge" },   // and again at 1.21.5
        { 772, 37, 4, "minecraft:sculk_charge" },
        { 773, 8, 4, "minecraft:dragon_breath" },   // 1.21.9 gave dragon_breath a float payload
        { 774, 8, 4, "minecraft:dragon_breath" },
        { 775, 22, 0, "minecraft:explosion_emitter" },
        { 776, 22, 12, "minecraft:dust_color_transition" }, // int, int, float
    };

    /// <summary>The era's own table is consulted, so the frame is exactly as wide as that era says and the option bytes come back intact. A wrong table either leaves trailing bytes (which <c>BoundPacketCodec.Decode</c> faults on) or runs past the end.</summary>
    [Theory]
    [MemberData(nameof(EraParticles))]
    public void ExplosionParticle_TakesTheWireLayoutOwnTable(int protocol, int particleId, int optionBytes, string particleName)
    {
        Assert.NotEmpty(particleName); // documents which particle the id names; see the data files
        byte[] options = Filler(optionBytes);
        byte[] frame = Frame(protocol, particleId, options);

        Assert.Equal(BodyWidth(protocol) + 1 + optionBytes, frame.Length);

        var decoded = (ClientboundExplodePacket)Decode(protocol, frame);
        ParticleData particle = Assert.IsType<ParticleData>(decoded.Particle);
        Assert.Equal(particleId, particle.TypeId);
        Assert.Equal(options, particle.Options);
        Assert.Equal(SoundId, Assert.IsType<SoundEventHolder>(decoded.Sound).SoundId);

        // A raw-carried payload is only honest if it goes back out unchanged.
        Assert.Equal(frame, Encode(protocol, decoded));
    }

    /// <summary>Cross-era rejection between two protocols that share a frame shape and differ ONLY in the particle table, so nothing but the table can explain the rejection.</summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>775 vs 776 on id 22: <c>explosion_emitter</c> (no payload) against
    /// <c>dust_color_transition</c> (12 bytes).</item>
    /// <item>768 vs 769 on id 35: <c>sculk_charge</c> (a 4-byte float) against <c>sculk_soul</c> (no
    /// payload). 1.21.4 inserted an entry below both, shifting everything from 35 up.</item>
    /// </list>
    /// </remarks>
    [Theory]
    [InlineData(775, 776, 22)]
    [InlineData(776, 775, 22)]
    [InlineData(768, 769, 35)]
    [InlineData(769, 768, 35)]
    public void AParticleFrameFromOneWireLayout_IsRejectedByItsNeighbour(int builtFor, int decodedOn, int particleId)
    {
        int builtWidth = OptionWidth(builtFor, particleId);
        int otherWidth = OptionWidth(decodedOn, particleId);
        Assert.NotEqual(builtWidth, otherWidth); // the premise: the two tables disagree on this id

        byte[] frame = Frame(builtFor, particleId, Filler(builtWidth));

        bool rejected;
        try
        {
            var decoded = (ClientboundExplodePacket)Decode(decodedOn, frame);
            rejected = Assert.IsType<ParticleData>(decoded.Particle).Options.Length != builtWidth;
        }
        catch (Exception ex) when (ex is ProtocolViolationException or ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            rejected = true;
        }

        Assert.True(rejected, $"a protocol-{builtFor} explosion particle frame was silently accepted on protocol {decodedOn}");
    }

    /// <summary>On 26.1, the two particles used by an explosion carry no payload and therefore use the narrow frame. Interpreting their ids with the 776 table would consume 12 and 8 extra bytes.</summary>
    [Theory]
    [InlineData(22)] // minecraft:explosion_emitter on 775
    [InlineData(23)] // minecraft:explosion on 775
    public void TwentySixOne_DefaultExplosionParticles_CarryNoPayload(int particleId)
    {
        byte[] frame = Frame(775, particleId, []);
        Assert.Equal(BodyWidth(775) + 1, frame.Length);

        var decoded = (ClientboundExplodePacket)Decode(775, frame);
        Assert.Empty(Assert.IsType<ParticleData>(decoded.Particle).Options);
        Assert.Equal(SoundId, Assert.IsType<SoundEventHolder>(decoded.Sound).SoundId);
    }

    /// <summary>Every ordering boundary in the band resolves a distinct bound codec. This is the direct statement that the table is no longer collapsed, and it is the one assertion that would still fail if a future edit merged two of these entries back together while every frame in the suite happened to use a payload-free particle.</summary>
    [Fact]
    public void EveryOrderingBoundary_ResolvesItsOwnCodec()
    {
        int[] boundaries = [768, 769, 770, 771, 773, 774, 775, 776];
        string[] identities = [.. boundaries.Select(p =>
            BoundCodec.At(p, PacketFlow.Clientbound, Explode).CodecIdentity)];

        Assert.Equal(identities.Length, identities.Distinct(StringComparer.Ordinal).Count());

        // 772 shares 771's tables (1.21.6-1.21.8 are one item-component era), so it must NOT be its own.
        Assert.Equal(
            BoundCodec.At(771, PacketFlow.Clientbound, Explode).CodecIdentity,
            BoundCodec.At(772, PacketFlow.Clientbound, Explode).CodecIdentity);
    }

    // Frame construction.

    /// <summary>A sound-event registry id, written as the holder's id+1 VarInt (one byte here).</summary>
    private const int SoundId = 100;

    /// <summary>The option bytes are arbitrary but non-zero, so a decoder that consumed the wrong count cannot land on the same value by reading zeros.</summary>
    private static byte[] Filler(int count) => [.. Enumerable.Range(1, count).Select(i => (byte)i)];

    /// <summary>Bytes in the frame other than the particle: the 768-772 body is center + absent knockback + sound; the 773+ body adds radius, block count and an empty weighted list.</summary>
    private static int BodyWidth(int protocol) => protocol >= 773 ? (24 + 4 + 4 + 1 + 1 + 1) : (24 + 1 + 1);

    private static int OptionWidth(int protocol, int particleId) => (protocol, particleId) switch
    {
        (775, 22) => 0,   // explosion_emitter
        (776, 22) => 12,  // dust_color_transition: int from, int to, float scale
        (768, 35) => 4,   // sculk_charge: float roll
        (769, 35) => 0,   // sculk_soul
        _ => throw new ArgumentOutOfRangeException(nameof(particleId), particleId, "no pinned width for this pair"),
    };

    private static byte[] Frame(int protocol, int particleId, byte[] particleOptions)
    {
        var buffer = new ArrayBufferWriter<byte>();
        WriteDouble(buffer, 5.0);
        WriteDouble(buffer, 6.0);
        WriteDouble(buffer, 7.0);

        if (protocol >= 773)
        {
            WriteFloat(buffer, 3.5f);                        // radius
            buffer.Write(new byte[] { 0x00, 0x00, 0x00, 0x2A }); // block count 42
        }

        buffer.Write(new byte[] { 0x00 });                   // knockback absent
        Assert.InRange(particleId, 0, 127);                  // single-byte VarInt keeps the arithmetic honest
        buffer.Write(new byte[] { (byte)particleId });
        buffer.Write(particleOptions);
        buffer.Write(new byte[] { SoundId + 1 });            // sound holder: registry id + 1

        if (protocol >= 773)
        {
            buffer.Write(new byte[] { 0x00 });               // empty weighted block-particle list
        }

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
