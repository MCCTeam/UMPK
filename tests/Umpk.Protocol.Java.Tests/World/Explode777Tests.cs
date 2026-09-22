using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;
using static Umpk.Protocol.Java.Tests.Support.LiteralFrame;

namespace Umpk.Protocol.Java.Tests.World;

/// <summary>26.3 (777) <c>minecraft:explode</c>: after the weighted block-particle list, vanilla <c>ClientboundExplodePacket.STREAM_CODEC</c> writes a BOOL <c>playSound</c>. The 776 codec must leave that byte alone, so 777 resolves its own codec that carries the trailing bool while 776 keeps the seven-member composite unchanged.</summary>
public sealed class Explode777Tests
{
    private const int P777 = 777;
    private const int P776 = 776;

    /// <summary>An option-free particle id on both the 776 and 777 tables, so no option bytes follow either particle.</summary>
    private const int PlainParticleId = 0;

    /// <summary>A sound-event registry id, written as the holder's id+1 VarInt (one byte here).</summary>
    private const int SoundId = 100;

    /// <summary>The exact 777 frame, hand-built without production writers: Vec3 center, float radius, int block count, absent knockback, particle VarInt, sound holder, one weighted block-particle entry, then the trailing playSound bool.</summary>
    private static byte[] Frame777(bool playSound) =>
        Cat(
            F64(1.5),
            F64(65.25),
            F64(-3.75),
            F32(3.5f),
            I32(42),
            [0x00],
            VarInt(PlainParticleId),
            VarInt(SoundId + 1),
            VarInt(1),
            VarInt(PlainParticleId),
            F32(1.0f),
            F32(0.5f),
            VarInt(3),
            [playSound ? (byte)0x01 : (byte)0x00]);

    /// <summary>The 776 frame for the same values under the old shape: the same seven-member composite with no trailing bool.</summary>
    private static byte[] Frame776() =>
        Cat(
            F64(1.5),
            F64(65.25),
            F64(-3.75),
            F32(3.5f),
            I32(42),
            [0x00],
            VarInt(PlainParticleId),
            VarInt(SoundId + 1),
            VarInt(1),
            VarInt(PlainParticleId),
            F32(1.0f),
            F32(0.5f),
            VarInt(3));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Explode777_ExactFrame_DecodesValuesAndRoundTrips(bool playSound)
    {
        byte[] frame = Frame777(playSound);

        var decoded = (ClientboundExplodePacket)Clientbound(P777, "explode").DecodeFrame(frame);

        Assert.Equal(1.5, decoded.Center.X);
        Assert.Equal(65.25, decoded.Center.Y);
        Assert.Equal(-3.75, decoded.Center.Z);
        Assert.Equal(3.5f, decoded.Radius);
        Assert.Equal(42, decoded.BlockCount);
        Assert.Null(decoded.Knockback);
        Assert.Equal(PlainParticleId, Assert.IsType<Umpk.Protocol.Java.Codecs.ParticleData>(decoded.Particle).TypeId);
        Assert.Empty(Assert.IsType<Umpk.Protocol.Java.Codecs.ParticleData>(decoded.Particle).Options);
        Assert.Equal(SoundId, decoded.Sound!.SoundId);
        var entry = Assert.Single(decoded.BlockParticles);
        Assert.Equal(PlainParticleId, entry.Particle.TypeId);
        Assert.Equal(1.0f, entry.Scaling);
        Assert.Equal(0.5f, entry.Speed);
        Assert.Equal(3, entry.Weight);
        Assert.Equal(playSound, decoded.PlaySound);
        Assert.Equal(frame, Clientbound(P777, "explode").Encode(decoded));
    }

    [Fact]
    public void Explode776_Rejects777Frame()
    {
        Rejects(Clientbound(P776, "explode"), Frame777(playSound: true), "the 776 codec must not accept a 777 frame with a trailing playSound bool");
    }

    [Fact]
    public void Explode777_Rejects776Frame()
    {
        Rejects(Clientbound(P777, "explode"), Frame776(), "the 777 codec must not accept a 776 frame missing the trailing playSound bool");
    }

    [Fact]
    public void Explode776_ShortFrame_Unaffected()
    {
        byte[] frame = Frame776();

        var decoded = (ClientboundExplodePacket)Clientbound(P776, "explode").DecodeFrame(frame);

        Assert.Equal(3.5f, decoded.Radius);
        Assert.Equal(42, decoded.BlockCount);
        Assert.Equal(3, Assert.Single(decoded.BlockParticles).Weight);
        Assert.True(decoded.PlaySound);
        Assert.Equal(frame, Clientbound(P776, "explode").Encode(decoded));
    }
}
