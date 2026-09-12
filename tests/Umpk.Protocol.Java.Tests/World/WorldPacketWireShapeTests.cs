using System.Buffers;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Xunit;
using static Umpk.Protocol.Java.Tests.Support.LiteralFrame;

namespace Umpk.Protocol.Java.Tests.World;

/// <summary>Pins literal frames and incompatible layouts for world-state packets.</summary>
public sealed class WorldPacketWireShapeTests
{
    [Theory]
    [InlineData(107)]
    [InlineData(404)]
    public void BlockDestruction_LiteralFrame_UsesPackedBlockPosition(int protocol)
    {
        byte[] frame = Cat(VarInt(4242), PackedBlockPosition(-300, 71, 1500), [7]);
        var p = (ClientboundBlockDestructionPacket)Clientbound(protocol, "block_destruction").DecodeFrame(frame);
        Assert.Equal(4242, p.EntityId);
        Assert.Equal(new BlockPos(-300, 71, 1500), p.Position);
        Assert.Equal(7, p.Progress);
    }

    [Theory]
    [InlineData(107)]
    [InlineData(404)]
    public void BlockEntityData_LiteralFrame_CarriesNamedRootNbt(int protocol)
    {
        // A NAMED-root compound: TAG_Compound, name "", one TAG_String "Text1" = "hi", TAG_End.
        byte[] nbt =
        [
            0x0A, 0x00, 0x00,
            0x08, 0x00, 0x05, (byte)'T', (byte)'e', (byte)'x', (byte)'t', (byte)'1',
            0x00, 0x02, (byte)'h', (byte)'i',
            0x00,
        ];
        byte[] frame = Cat(PackedBlockPosition(11, 64, -22), [9], nbt);
        var p = (ClientboundBlockEntityDataPacket)Clientbound(protocol, "block_entity_data").DecodeFrame(frame);
        Assert.Equal(new BlockPos(11, 64, -22), p.Position);
        Assert.Equal(9, p.BlockEntityType);
        Assert.Equal(frame, Clientbound(protocol, "block_entity_data").Encode(p));
    }

    [Theory]
    [InlineData(107)]
    [InlineData(404)]
    public void ChangeDifficulty_LiteralFrame_IsABareByte(int protocol)
    {
        var p = (ClientboundChangeDifficultyPacket)Clientbound(protocol, "change_difficulty").DecodeFrame([3]);
        Assert.Equal(3, p.Difficulty);
        Assert.False(p.Locked);
    }

    [Theory]
    [InlineData(107)]
    [InlineData(404)]
    public void Explode_LiteralFrame_CarriesBlockOffsets(int protocol)
    {
        byte[] frame = Cat(
            F32(10.5f), F32(64.25f), F32(-3.75f), F32(4f),
            I32(2),
            [0xFF, 0x01, 0x02],   // -1, 1, 2
            [0x03, 0xFE, 0x00],   // 3, -2, 0
            F32(0.1f), F32(0.2f), F32(-0.3f));
        var p = (ClientboundExplodePacket)Clientbound(protocol, "explode").DecodeFrame(frame);
        Assert.Equal(4f, p.LegacyStrength);
        Assert.Equal(2, p.LegacyBlocks.Count);
        Assert.Equal(new ExplosionBlock(-1, 1, 2), p.LegacyBlocks[0]);
        Assert.Equal(new ExplosionBlock(3, -2, 0), p.LegacyBlocks[1]);
        Assert.Equal(-0.3f, p.LegacyMotionZ);
    }

    [Theory]
    [InlineData(107)]
    [InlineData(404)]
    public void ForgetLevelChunk_LiteralFrame_IsTwoPlainInts(int protocol)
    {
        byte[] frame = Cat(I32(-7), I32(19));
        var p = (ClientboundForgetLevelChunkPacket)Clientbound(protocol, "forget_level_chunk").DecodeFrame(frame);
        Assert.Equal(new ChunkPos(-7, 19), p.Chunk);
        Assert.Equal(frame, Clientbound(protocol, "forget_level_chunk").Encode(p));
    }

    [Theory]
    [InlineData(107)]
    [InlineData(404)]
    public void LevelEvent_LiteralFrame(int protocol)
    {
        byte[] frame = Cat(I32(2001), PackedBlockPosition(5, 60, -5), I32(1234), [1]);
        var p = (ClientboundLevelEventPacket)Clientbound(protocol, "level_event").DecodeFrame(frame);
        Assert.Equal(2001, p.EffectId);
        Assert.Equal(new BlockPos(5, 60, -5), p.Position);
        Assert.Equal(1234, p.Data);
        Assert.True(p.Global);
    }

    [Theory]
    [InlineData(107)]
    [InlineData(340)]
    public void LevelParticles_NumericIdFrame_HasVarIntArguments(int protocol)
    {
        // Particle id 36 (iconcrack) is the two-argument legacy particle: its tail is exactly two VarInts.
        byte[] frame = Cat(
            I32(36), [1],
            F32(1f), F32(2f), F32(3f),
            F32(0.1f), F32(0.2f), F32(0.3f),
            F32(0.5f), I32(20),
            VarInt(264), VarInt(0));
        var p = (ClientboundLevelParticlesPacket)Clientbound(protocol, "level_particles").DecodeFrame(frame);
        Assert.Equal(36, p.Particle.TypeId);
        Assert.Equal(20, p.Count);
        Assert.Equal(Cat(VarInt(264), VarInt(0)), p.Particle.Options);
        Assert.Equal(frame, Clientbound(protocol, "level_particles").Encode(p));
    }

    [Theory]
    [InlineData(393)]
    [InlineData(404)]
    public void LevelParticles_NamedIdFrame_KeepsTypedTailRaw(int protocol)
    {
        byte[] frame = Cat(
            I32(3), [0],
            F32(-8f), F32(70f), F32(12f),
            F32(0f), F32(1f), F32(0f),
            F32(0.25f), I32(5),
            VarInt(1234));      // block-state id, the 1.13 typed particle payload
        var p = (ClientboundLevelParticlesPacket)Clientbound(protocol, "level_particles").DecodeFrame(frame);
        Assert.Equal(3, p.Particle.TypeId);
        Assert.Equal(5, p.Count);
        Assert.Equal(VarInt(1234), p.Particle.Options);
        Assert.Equal(frame, Clientbound(protocol, "level_particles").Encode(p));
    }

    [Theory]
    [InlineData(107)]
    [InlineData(340)]
    public void MapItemData_PackedIconFrame_PacksTypeHighRotationLow(int protocol)
    {
        byte[] frame = Cat(
            VarInt(7), [2], [1],                    // id, scale, trackingPosition
            VarInt(2),
            [0x63], [10], [0xEC],                   // type 6, rotation 3, x 10, z -20
            [0x04], [0xF6], [0x00],                 // type 0, rotation 4, x -10, z 0
            [3], [2], [4], [5],
            VarInt(6), [1, 2, 3, 4, 5, 6]);
        var p = (ClientboundMapItemDataPacket)Clientbound(protocol, "map_item_data").DecodeFrame(frame);
        Assert.Equal(7, p.MapId);
        Assert.True(p.TrackingPosition);
        Assert.Equal(2, p.Icons!.Count);
        Assert.Equal(6, p.Icons[0].Type);
        Assert.Equal(3, p.Icons[0].Rotation);
        Assert.Equal(10, p.Icons[0].X);
        Assert.Equal(-20, p.Icons[0].Z);
        Assert.Equal(0, p.Icons[1].Type);
        Assert.Equal(4, p.Icons[1].Rotation);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, p.Patch.Colors);
        Assert.Equal(frame, Clientbound(protocol, "map_item_data").Encode(p));
    }

    [Theory]
    [InlineData(393)]
    [InlineData(404)]
    public void MapItemData_NamedIconFrame_HasVarIntTypesAndJsonNames(int protocol)
    {
        byte[] frame = Cat(
            VarInt(3), [1], [1],
            VarInt(2),
            VarInt(9), [12], [0xDE], [7], [1], Str("{\"text\":\"Home\"}"),
            VarInt(1), [0], [0], [0], [0],
            [2], [2], [1], [1],
            VarInt(4), [7, 8, 9, 10]);
        var p = (ClientboundMapItemDataPacket)Clientbound(protocol, "map_item_data").DecodeFrame(frame);
        Assert.Equal(3, p.MapId);
        Assert.True(p.TrackingPosition);
        Assert.Equal(2, p.Icons!.Count);
        Assert.Equal(9, p.Icons[0].Type);
        Assert.Equal(12, p.Icons[0].X);
        Assert.Equal(-34, p.Icons[0].Z);
        Assert.Equal(7, p.Icons[0].Rotation);
        Assert.Equal("Home", p.Icons[0].DisplayName!.ToPlainText());
        Assert.Null(p.Icons[1].DisplayName);

        // The icon display name is a JSON component, which re-encodes in canonical form, so the round trip is asserted on a re-decode rather than on the bytes.
        var again = (ClientboundMapItemDataPacket)Clientbound(protocol, "map_item_data")
            .DecodeFrame(Clientbound(protocol, "map_item_data").Encode(p));
        Assert.Equal(p.Icons, again.Icons);
        Assert.Equal(p.Patch.Colors, again.Patch.Colors);
    }

    [Theory]
    [InlineData(107)]
    [InlineData(404)]
    public void Respawn_LiteralFrame_CarriesTheDimension(int protocol)
    {
        byte[] frame = Cat(I32(-1), [2], [1], Str("flat"));
        var p = (ClientboundRespawnPacket)Clientbound(protocol, "respawn").DecodeFrame(frame);
        Assert.NotNull(p.Legacy);
        Assert.Equal(-1, p.Legacy!.Dimension);
        Assert.Equal(2, p.Legacy.Difficulty);
        Assert.Equal(1, p.Legacy.GameMode);
        Assert.Equal("flat", p.Legacy.LevelType);
    }

    [Theory]
    [InlineData(107)]
    [InlineData(110)]
    public void Sound_BytePitchFrame_Decodes(int protocol)
    {
        byte[] frame = Cat(VarInt(300), VarInt(4), I32(80), I32(512), I32(-96), F32(1f), [0xC0]);
        var p = (ClientboundSoundPacket)Clientbound(protocol, "sound").DecodeFrame(frame);
        Assert.Equal(300, p.Sound.SoundId);
        Assert.Equal(4, p.Source);
        Assert.Equal(80, p.X);
        Assert.Equal(-96, p.Z);
        Assert.Equal(192f, p.Pitch);
        Assert.Equal(frame, Clientbound(protocol, "sound").Encode(p));
    }

    [Theory]
    [InlineData(210)]
    [InlineData(340)]
    [InlineData(404)]
    public void Sound_FloatPitchFrame_Decodes(int protocol)
    {
        byte[] frame = Cat(VarInt(300), VarInt(4), I32(80), I32(512), I32(-96), F32(1f), F32(1.5f));
        var p = (ClientboundSoundPacket)Clientbound(protocol, "sound").DecodeFrame(frame);
        Assert.Equal(1.5f, p.Pitch);
        Assert.Equal(frame, Clientbound(protocol, "sound").Encode(p));
    }

    [Fact]
    public void Sound_AlternatePitchFrames_AreRejected()
    {
        byte[] pitchByte = Cat(VarInt(300), VarInt(4), I32(80), I32(512), I32(-96), F32(1f), [0xC0]);
        byte[] pitchFloat = Cat(VarInt(300), VarInt(4), I32(80), I32(512), I32(-96), F32(1f), F32(1.5f));

        Rejects(Clientbound(210, "sound"), pitchByte, "the 1.10 float-pitch codec must not accept a 1.9 byte-pitch frame");
        Rejects(Clientbound(107, "sound"), pitchFloat, "the 1.9 byte-pitch codec must not accept a 1.10 float-pitch frame");
    }

    [Fact]
    public void MapItemData_AlternateWireShapes_AreRejected()
    {
        byte[] v1_8 = Cat(VarInt(7), [2], VarInt(1), [0x63], [10], [0xEC], [0]);
        byte[] v1_9 = Cat(VarInt(7), [2], [1], VarInt(1), [0x63], [10], [0xEC], [0]);
        byte[] v1_13 = Cat(VarInt(7), [2], [1], VarInt(1), VarInt(9), [12], [0xDE], [7], [0], [0]);

        Rejects(Clientbound(107, "map_item_data"), v1_8, "1.9 requires the tracking-position bool");
        Rejects(Clientbound(393, "map_item_data"), v1_9, "1.13 icons are VarInt-typed with a rotation byte");
        Rejects(Clientbound(340, "map_item_data"), v1_13, "1.12.2 icons pack type and rotation into one byte");
    }

    [Fact]
    public void LevelParticles_NumericAndNamedIdFrames_RejectEachOther()
    {
        byte[] header = Cat(
            I32(36), [1], F32(1f), F32(2f), F32(3f), F32(0.1f), F32(0.2f), F32(0.3f), F32(0.5f), I32(20));

        // Legacy particle 36 (iconcrack) carries exactly two VarInt arguments, so the 1.12.2 codec must refuse a one-argument tail. The 1.13 codec keeps whatever trails as an opaque payload and therefore accepts it: that asymmetry is the point, since a 1.13 particle tail is type dispatched and cannot be read as a fixed VarInt run.
        Rejects(Clientbound(340, "level_particles"), Cat(header, VarInt(264)), "iconcrack needs two arguments on 1.12.2");
        var flat = (ClientboundLevelParticlesPacket)Clientbound(393, "level_particles").DecodeFrame(Cat(header, VarInt(264)));
        Assert.Equal(VarInt(264), flat.Particle.Options);
    }

    [Fact]
    public void ForgetLevelChunk_RejectsPackedChunkPositionFrame()
    {
        // The 1.14 form is the same eight bytes with z high and x low, so this can only be caught by comparing the decoded coordinates, not by a length check.
        byte[] legacy = Cat(I32(-7), I32(19));
        var packed = (ClientboundForgetLevelChunkPacket)Clientbound(477, "forget_level_chunk").DecodeFrame(legacy);
        Assert.NotEqual(new ChunkPos(-7, 19), packed.Chunk);

        var legacyDecoded = (ClientboundForgetLevelChunkPacket)Clientbound(404, "forget_level_chunk").DecodeFrame(legacy);
        Assert.Equal(new ChunkPos(-7, 19), legacyDecoded.Chunk);
    }
}
