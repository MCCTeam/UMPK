using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;
using static Umpk.Protocol.Java.Tests.Support.LiteralFrame;

namespace Umpk.Protocol.Java.Tests.World;

/// <summary>26.3 (777) <c>minecraft:level_particles</c>: the particle moves to the FRONT of the frame, the single speed splits into three per-axis maximum speeds, the count becomes a VarInt, and a VarInt randomization type trails. Vanilla <c>ClientboundLevelParticlesPacket.STREAM_CODEC</c> order is particle, overrideLimiter, alwaysShow, x, y, z, xDist, yDist, zDist, xMaxSpeed, yMaxSpeed, zMaxSpeed, count (VarInt), randomizationType (VarInt).</summary>
public sealed class LevelParticles777Tests
{
    private const int P777 = 777;
    private const int P776 = 776;

    /// <summary>An option-free particle id on both the 776 and 777 tables (<c>minecraft:angry_villager</c>, absent from both payload tables, so no option bytes follow).</summary>
    private const int PlainParticleId = 0;

    /// <summary>The exact 777 frame, hand-built without production writers: particle VarInt first, two bools, three doubles, three floats, three speed floats, VarInt count, VarInt randomization.</summary>
    private static byte[] Frame777(LevelParticleRandomizationType randomization = LevelParticleRandomizationType.Alternative) =>
        Cat(
            VarInt(PlainParticleId),
            [0x01],
            [0x00],
            F64(1.5),
            F64(65.25),
            F64(-3.75),
            F32(0.5f),
            F32(0.25f),
            F32(0.125f),
            F32(2.5f),
            F32(3.5f),
            F32(4.5f),
            VarInt(7),
            VarInt((int)randomization));

    /// <summary>The 776 frame for the same values under the old shape: bools first, one speed float, big-endian int count, particle VarInt last.</summary>
    private static byte[] Frame776() =>
        Cat(
            [0x01],
            [0x00],
            F64(1.5),
            F64(65.25),
            F64(-3.75),
            F32(0.5f),
            F32(0.25f),
            F32(0.125f),
            F32(2.5f),
            [(byte)(7 >> 24), (byte)(7 >> 16), (byte)(7 >> 8), (byte)7],
            VarInt(PlainParticleId));

    [Theory]
    [InlineData(LevelParticleRandomizationType.Default)]
    [InlineData(LevelParticleRandomizationType.Alternative)]
    [InlineData(LevelParticleRandomizationType.AlternativeWithSpeed)]
    public void LevelParticles777_ExactFrame_DecodesValuesAndRoundTrips(LevelParticleRandomizationType randomization)
    {
        byte[] frame = Frame777(randomization);

        var decoded = (ClientboundLevelParticlesPacket)Clientbound(P777, "level_particles").DecodeFrame(frame);

        Assert.Equal(PlainParticleId, decoded.Particle.TypeId);
        Assert.Empty(decoded.Particle.Options);
        Assert.True(decoded.OverrideLimiter);
        Assert.False(decoded.AlwaysShow);
        Assert.Equal(1.5, decoded.X);
        Assert.Equal(65.25, decoded.Y);
        Assert.Equal(-3.75, decoded.Z);
        Assert.Equal(0.5f, decoded.XDist);
        Assert.Equal(0.25f, decoded.YDist);
        Assert.Equal(0.125f, decoded.ZDist);
        Assert.Equal(2.5f, decoded.MaxSpeed);
        Assert.Equal(3.5f, decoded.YMaxSpeed);
        Assert.Equal(4.5f, decoded.ZMaxSpeed);
        Assert.Equal(7, decoded.Count);
        Assert.Equal(randomization, decoded.Randomization);
        Assert.Equal(frame, Clientbound(P777, "level_particles").Encode(decoded));
    }

    [Fact]
    public void LevelParticles777_PerAxisSpeeds_AreNotCollapsed()
    {
        var decoded = (ClientboundLevelParticlesPacket)Clientbound(P777, "level_particles").DecodeFrame(Frame777());

        Assert.NotEqual(decoded.MaxSpeed, decoded.YMaxSpeed);
        Assert.NotEqual(decoded.MaxSpeed, decoded.ZMaxSpeed);
        Assert.NotEqual(decoded.YMaxSpeed, decoded.ZMaxSpeed);
    }

    [Fact]
    public void LevelParticles776_Rejects777Frame()
    {
        Rejects(Clientbound(P776, "level_particles"), Frame777(), "the 776 codec must not accept a 777 particle-first frame with two extra speed floats");
    }

    [Fact]
    public void LevelParticles777_Rejects776Frame()
    {
        Rejects(Clientbound(P777, "level_particles"), Frame776(), "the 777 codec must not accept a 776 particle-last single-speed frame");
    }

    [Fact]
    public void LevelParticles776_ShortFrame_Unaffected()
    {
        byte[] frame = Frame776();

        var decoded = (ClientboundLevelParticlesPacket)Clientbound(P776, "level_particles").DecodeFrame(frame);

        Assert.Equal(PlainParticleId, decoded.Particle.TypeId);
        Assert.Equal(2.5f, decoded.MaxSpeed);
        Assert.Equal(2.5f, decoded.YMaxSpeed);
        Assert.Equal(2.5f, decoded.ZMaxSpeed);
        Assert.Equal(7, decoded.Count);
        Assert.Equal(LevelParticleRandomizationType.Default, decoded.Randomization);
        Assert.Equal(frame, Clientbound(P776, "level_particles").Encode(decoded));
    }
}
