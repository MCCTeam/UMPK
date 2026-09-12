using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Xunit;

namespace Umpk.Protocol.Java.Tests.World;

/// <summary>Seeded, byte-level round-trip property tests for the "World" family across the three protocols (47 / 770 / 776), plus payload edge cases (empty collections, absent/present optionals, boundary values, and the 776 wire deltas).</summary>
public class WorldCodecTests
{
    private static readonly CommonPlayerSpawnInfo Spawn = new(
        DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 987654321L,
        GameType: 1, PreviousGameType: -1, IsDebug: false, IsFlat: true,
        LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);

    private static readonly BlockPos SamplePos = new(-12345, 200, 54321);

    // Time.

    [Fact]
    public void SetTime_FixedClockBody_RoundTrips()
    {
        var p = new ClientboundSetTimePacket(123456L, -6000L, TickDayTime: false, ClockUpdates: []);
        var d = CodecRoundTrip.Cycle(WorldStateCodecs.SetTimeV1_8, p);
        Assert.Equal(p.GameTime, d.GameTime);
        Assert.Equal(p.DayTime, d.DayTime);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SetTime_TickFlagBody_RoundTrips(bool tick)
    {
        var p = new ClientboundSetTimePacket(1L, 2L, tick, ClockUpdates: []);
        var d = CodecRoundTrip.Cycle(WorldStateCodecs.SetTimeV1_21_2, p);
        Assert.Equal(p.GameTime, d.GameTime);
        Assert.Equal(p.DayTime, d.DayTime);
        Assert.Equal(p.TickDayTime, d.TickDayTime);
    }

    [Fact]
    public void SetTime_ClockMapBody_PreservesRawBytes()
    {
        byte[] clock = [0x02, 0x00, 0x01, 0x02, 0x03];
        var p = new ClientboundSetTimePacket(42L, 0L, false, clock);
        var d = CodecRoundTrip.Cycle(WorldStateCodecs.SetTimeV26_1, p);
        Assert.Equal(p.GameTime, d.GameTime);
        Assert.Equal(clock, d.ClockUpdates);
    }

    [Fact]
    public void SetTime_EmptyClockMap_RoundTrips()
    {
        var p = new ClientboundSetTimePacket(42L, 0L, false, []);
        var d = CodecRoundTrip.Cycle(WorldStateCodecs.SetTimeV26_1, p);
        Assert.Empty(d.ClockUpdates);
    }

    // Default spawn position.

    [Fact]
    public void SetDefaultSpawn_PositionOnlyBody_RoundTrips()
    {
        var p = new ClientboundSetDefaultSpawnPositionPacket(SamplePos, Angle: 0f, Dimension: null, Pitch: 0f);
        var d = CodecRoundTrip.Cycle(WorldStateCodecs.SetDefaultSpawnV1_8, p);
        Assert.Equal(p.Position, d.Position);
    }

    [Fact]
    public void SetDefaultSpawn_AngledBody_RoundTrips()
    {
        var p = new ClientboundSetDefaultSpawnPositionPacket(SamplePos, Angle: 90f, Dimension: null, Pitch: 0f);
        var d = CodecRoundTrip.Cycle(WorldStateCodecs.SetDefaultSpawnV1_17, p);
        Assert.Equal(p.Position, d.Position);
        Assert.Equal(p.Angle, d.Angle);
    }

    [Fact]
    public void SetDefaultSpawn_DimensionAndPitchBody_RoundTrips()
    {
        var p = new ClientboundSetDefaultSpawnPositionPacket(SamplePos, Angle: 45f, Dimension: "minecraft:the_end", Pitch: -30f);
        var d = CodecRoundTrip.Cycle(WorldStateCodecs.SetDefaultSpawnV1_21_9, p);
        Assert.Equal(p.Position, d.Position);
        Assert.Equal(p.Angle, d.Angle);
        Assert.Equal(p.Dimension, d.Dimension);
        Assert.Equal(p.Pitch, d.Pitch);
    }

    [Fact]
    public void SetDefaultSpawn_DimensionBody_EncodesMoreThanAngledBody()
    {
        var p = new ClientboundSetDefaultSpawnPositionPacket(SamplePos, 45f, "minecraft:overworld", -30f);
        byte[] modern = CodecRoundTrip.Encode(WorldStateCodecs.SetDefaultSpawnV1_21_9, p);
        byte[] older = CodecRoundTrip.Encode(WorldStateCodecs.SetDefaultSpawnV1_17, p);
        Assert.True(modern.Length > older.Length);
    }

    // Respawn.

    [Fact]
    public void Respawn_NumericDimensionBody_RoundTrips()
    {
        var p = new ClientboundRespawnPacket(SpawnInfo: null, DataToKeep: 0,
            new LegacyRespawnFields(-1, 2, 1, "flat"));
        var d = CodecRoundTrip.Cycle(WorldStateCodecs.RespawnV1_8, p);
        Assert.Equal(p.Legacy, d.Legacy);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Respawn_RegistryDimensionBody_RoundTrips(byte keep)
    {
        var p = new ClientboundRespawnPacket(Spawn, keep, Legacy: null);
        var d = CodecRoundTrip.Cycle(WorldStateCodecs.RespawnV1_21_2, p);
        Assert.Equal(p.SpawnInfo, d.SpawnInfo);
        Assert.Equal(p.DataToKeep, d.DataToKeep);
    }

    [Fact]
    public void Respawn_LastDeathLocation_RoundTrips()
    {
        long packed = ((long)(100 & 0x3FFFFFF) << 38) | ((long)(-200 & 0x3FFFFFF) << 12) | (64 & 0xFFFL);
        CommonPlayerSpawnInfo info = Spawn with
        {
            LastDeathDimensionAndPos = packed,
            LastDeathDimension = "minecraft:the_nether",
        };
        var p = new ClientboundRespawnPacket(info, 3, Legacy: null);
        var d = CodecRoundTrip.Cycle(WorldStateCodecs.RespawnV1_21_2, p);
        Assert.Equal(info.LastDeathDimensionAndPos, d.SpawnInfo!.LastDeathDimensionAndPos);
        Assert.Equal("minecraft:the_nether", d.SpawnInfo.LastDeathDimension);
    }

    // Difficulty.

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void ChangeDifficulty_BareByteBody_RoundTrips(byte diff)
    {
        var p = new ClientboundChangeDifficultyPacket(diff, Locked: false);
        var d = CodecRoundTrip.Cycle(WorldStateCodecs.ChangeDifficultyV1_8, p);
        Assert.Equal(p.Difficulty, d.Difficulty);
    }

    [Theory]
    [InlineData((byte)2, true)]
    [InlineData((byte)0, false)]
    public void ChangeDifficulty_LockedByteBody_RoundTrips(byte diff, bool locked)
    {
        var p = new ClientboundChangeDifficultyPacket(diff, locked);
        Assert.Equal(p, CodecRoundTrip.Cycle(WorldStateCodecs.ChangeDifficultyV1_14, p));
    }

    [Fact]
    public void ChangeDifficulty_VariableIntegerBody_RoundTrips()
    {
        var p = new ClientboundChangeDifficultyPacket(3, Locked: true);
        Assert.Equal(p, CodecRoundTrip.Cycle(WorldStateCodecs.ChangeDifficultyV26_2, p));
    }

    // Block changes.

    [Fact]
    public void BlockUpdate_RoundTripsAcrossPositionLayouts()
    {
        var p = new ClientboundBlockUpdatePacket(SamplePos, 12345);
        Assert.Equal(p, CodecRoundTrip.Cycle(WorldBlockCodecs.BlockUpdateV1_8, p));
        Assert.Equal(p, CodecRoundTrip.Cycle(WorldBlockCodecs.BlockUpdateV1_14, p));
    }

    [Fact]
    public void SectionBlocksUpdate_CoordinateArrayBody_RoundTrips()
    {
        var changes = new SectionBlockChange[]
        {
            new(unchecked((short)0xAB12), 100),
            new(0x0FF0, 5),
        };
        var p = new ClientboundSectionBlocksUpdatePacket(SectionPos: 0, LegacyChunkX: 5, LegacyChunkZ: -3, changes);
        var d = CodecRoundTrip.Cycle(WorldBlockCodecs.SectionBlocksUpdateV1_8, p);
        Assert.Equal(p.LegacyChunkX, d.LegacyChunkX);
        Assert.Equal(p.LegacyChunkZ, d.LegacyChunkZ);
        Assert.Equal(changes, d.Changes);
    }

    [Fact]
    public void SectionBlocksUpdate_EmptyCoordinateArray_RoundTrips()
    {
        var p = new ClientboundSectionBlocksUpdatePacket(0, 1, 2, []);
        var d = CodecRoundTrip.Cycle(WorldBlockCodecs.SectionBlocksUpdateV1_8, p);
        Assert.Empty(d.Changes);
    }

    [Fact]
    public void SectionBlocksUpdate_PackedVariableLongBody_RoundTrips()
    {
        long section = ((long)5 << 42) | ((long)(0 & 0x3FFFFF) << 20) | (7 & 0xFFFFF);
        var changes = new SectionBlockChange[]
        {
            new(0xFFF, 8191), // max local position, high state id
            new(0, 0),
        };
        var p = new ClientboundSectionBlocksUpdatePacket(section, 0, 0, changes);
        var d = CodecRoundTrip.Cycle(WorldBlockCodecs.SectionBlocksUpdateV1_20, p);
        Assert.Equal(section, d.SectionPos);
        Assert.Equal(changes, d.Changes);
    }

    [Fact]
    public void BlockEntityData_RoundTripsAcrossTagLayouts()
    {
        var compound = new NbtCompound();
        compound.PutString("id", "minecraft:chest");
        var p = new ClientboundBlockEntityDataPacket(SamplePos, 5, compound);
        foreach (var codec in new[]
        {
            WorldBlockCodecs.BlockEntityDataV1_8,
            WorldBlockCodecs.BlockEntityDataV1_14,
            WorldBlockCodecs.BlockEntityDataV1_18,
            WorldBlockCodecs.BlockEntityDataV1_20_2,
        })
        {
            var d = CodecRoundTrip.Cycle(codec, p);
            Assert.Equal(p.Position, d.Position);
            Assert.Equal(p.BlockEntityType, d.BlockEntityType);
            Assert.Equal("minecraft:chest", ((NbtCompound)d.Nbt).GetString("id"));
        }
    }

    [Fact]
    public void BlockEntityData_TagEnd_RoundTripsAcrossTagLayouts()
    {
        var p = new ClientboundBlockEntityDataPacket(SamplePos, 1, NbtEnd.Instance);
        foreach (var codec in new[]
        {
            WorldBlockCodecs.BlockEntityDataV1_8,
            WorldBlockCodecs.BlockEntityDataV1_14,
            WorldBlockCodecs.BlockEntityDataV1_18,
            WorldBlockCodecs.BlockEntityDataV1_20_2,
        })
            Assert.IsType<NbtEnd>(CodecRoundTrip.Cycle(codec, p).Nbt);

    }

    /// <summary>The vanilla wire truth this family must decode: pre-1.20.2 frames carry a NAMED NBT root (type byte + empty root name + body), 1.20.2+ frames carry the unnamed root. The NBT section here is hand-built (not produced by the writer under test) so a reader using the wrong root flavor fails the full-consumption assertion with a "block_entity_data left N trailing bytes" protocol fault.</summary>
    [Fact]
    public void BlockEntityData_DecodesNamedRootFrames()
    {
        // 0x0A, root name "" (2-byte length), string entry id="minecraft:chest", end.
        byte[] namedNbt =
        [
            0x0A, 0x00, 0x00,
            0x08, 0x00, 0x02, (byte)'i', (byte)'d',
            0x00, 0x0F, .. "minecraft:chest"u8,
            0x00,
        ];
        byte[] unnamedNbt = [0x0A, .. namedNbt[3..]];

        AssertDecodes(WorldBlockCodecs.BlockEntityDataV1_8, namedNbt);
        AssertDecodes(WorldBlockCodecs.BlockEntityDataV1_14, namedNbt);
        AssertDecodes(WorldBlockCodecs.BlockEntityDataV1_18, namedNbt);
        AssertDecodes(WorldBlockCodecs.BlockEntityDataV1_20_2, unnamedNbt);

        static void AssertDecodes(PacketCodec<ClientboundBlockEntityDataPacket> codec, byte[] nbtBytes)
        {
            // Reuse the codec's own header (pos + type) via an absent-NBT encode, then splice the hand-built NBT over the trailing End byte.
            byte[] header = CodecRoundTrip.Encode(codec, new ClientboundBlockEntityDataPacket(SamplePos, 5, NbtEnd.Instance));
            Assert.Equal(0x00, header[^1]);
            byte[] frame = [.. header[..^1], .. nbtBytes];

            var d = CodecRoundTrip.Decode(codec, frame);
            Assert.Equal(SamplePos, d.Position);
            Assert.Equal(5, d.BlockEntityType);
            Assert.Equal("minecraft:chest", ((NbtCompound)d.Nbt).GetString("id"));
        }
    }

    [Fact]
    public void BlockEntityData_NamedRootEncodings_CarryTwoRootNameBytes()
    {
        var compound = new NbtCompound();
        compound.PutString("id", "minecraft:sign");
        var p = new ClientboundBlockEntityDataPacket(SamplePos, 5, compound);
        // Same header shape (VarInt type 5 = 1 byte in both), so named vs unnamed differs by exactly the 2-byte empty root name.
        byte[] named = CodecRoundTrip.Encode(WorldBlockCodecs.BlockEntityDataV1_18, p);
        byte[] unnamed = CodecRoundTrip.Encode(WorldBlockCodecs.BlockEntityDataV1_20_2, p);
        Assert.Equal(unnamed.Length + 2, named.Length);
    }

    [Fact]
    public void BlockEvent_RoundTripsAcrossPositionLayouts()
    {
        var p = new ClientboundBlockEventPacket(SamplePos, 1, 2, 25);
        Assert.Equal(p, CodecRoundTrip.Cycle(WorldBlockCodecs.BlockEventV1_8, p));
        Assert.Equal(p, CodecRoundTrip.Cycle(WorldBlockCodecs.BlockEventV1_14, p));
    }

    [Fact]
    public void BlockDestruction_RoundTripsAcrossPositionLayouts()
    {
        var p = new ClientboundBlockDestructionPacket(9999, SamplePos, 7);
        Assert.Equal(p, CodecRoundTrip.Cycle(WorldBlockCodecs.BlockDestructionV1_8, p));
        Assert.Equal(p, CodecRoundTrip.Cycle(WorldBlockCodecs.BlockDestructionV1_14, p));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2097151)]
    public void BlockChangedAck_RoundTrips(int seq)
    {
        var p = new ClientboundBlockChangedAckPacket(seq);
        Assert.Equal(p, CodecRoundTrip.Cycle(WorldBlockCodecs.BlockChangedAckV1_19, p));
    }

    // Chunk metadata.

    [Fact]
    public void ForgetLevelChunk_RoundTrips()
    {
        var p = new ClientboundForgetLevelChunkPacket(new ChunkPos(-5, 12));
        Assert.Equal(p, CodecRoundTrip.Cycle(WorldStateCodecs.ForgetLevelChunkV1_14, p));
    }

    [Fact]
    public void ChunkBatchStart_RoundTrips_Empty()
    {
        byte[] bytes = CodecRoundTrip.Encode(WorldStateCodecs.ChunkBatchStartV1_20_2, new ClientboundChunkBatchStartPacket());
        Assert.Empty(bytes);
        _ = CodecRoundTrip.Cycle(WorldStateCodecs.ChunkBatchStartV1_20_2, new ClientboundChunkBatchStartPacket());
    }

    [Fact]
    public void ChunkBatchFinished_RoundTrips()
    {
        var p = new ClientboundChunkBatchFinishedPacket(42);
        Assert.Equal(p, CodecRoundTrip.Cycle(WorldStateCodecs.ChunkBatchFinishedV1_20_2, p));
    }

    [Fact]
    public void ChunkBatchReceived_RoundTrips()
    {
        var p = new ServerboundChunkBatchReceivedPacket(6.25f);
        Assert.Equal(p, CodecRoundTrip.Cycle(WorldStateCodecs.ChunkBatchReceivedV1_20_2, p));
    }

    [Fact]
    public void ChunksBiomes_RoundTrips_WithEntries()
    {
        var entries = new ChunkBiomeEntry[]
        {
            new(new ChunkPos(1, 2), [1, 2, 3, 4]),
            new(new ChunkPos(-3, 4), []),
        };
        var p = new ClientboundChunksBiomesPacket(entries);
        var d = CodecRoundTrip.Cycle(WorldStateCodecs.ChunksBiomesV1_19_4, p);
        Assert.Equal(2, d.Chunks.Count);
        Assert.Equal(entries[0].Chunk, d.Chunks[0].Chunk);
        Assert.Equal(entries[0].Buffer, d.Chunks[0].Buffer);
        Assert.Empty(d.Chunks[1].Buffer);
    }

    [Fact]
    public void ChunksBiomes_EmptyList_RoundTrips()
    {
        var p = new ClientboundChunksBiomesPacket([]);
        Assert.Empty(CodecRoundTrip.Cycle(WorldStateCodecs.ChunksBiomesV1_19_4, p).Chunks);
    }

    [Fact]
    public void SetChunkCacheCenter_RoundTrips()
    {
        var p = new ClientboundSetChunkCacheCenterPacket(10, -20);
        Assert.Equal(p, CodecRoundTrip.Cycle(WorldStateCodecs.SetChunkCacheCenterV1_14, p));
    }

    [Fact]
    public void SetChunkCacheRadius_And_SimulationDistance_RoundTrip()
    {
        Assert.Equal(12, CodecRoundTrip.Cycle(WorldStateCodecs.SetChunkCacheRadiusV1_14, new ClientboundSetChunkCacheRadiusPacket(12)).Radius);
        Assert.Equal(8, CodecRoundTrip.Cycle(WorldStateCodecs.SetSimulationDistanceV1_18, new ClientboundSetSimulationDistancePacket(8)).SimulationDistance);
    }

    // Light.

    [Fact]
    public void LightUpdate_RoundTrips_WithArrays()
    {
        byte[] sky = new byte[2048];
        byte[] block = new byte[2048];
        for (int i = 0; i < 2048; i++)
        {
            sky[i] = (byte)(i & 0xFF);
            block[i] = (byte)((i * 3) & 0xFF);
        }

        var light = new LightUpdateData([0x1L, 0x2L], [0x3L], [], [0x4L], [sky], [block]);
        var p = new ClientboundLightUpdatePacket(3, -7, light);
        var d = CodecRoundTrip.Cycle(WorldStateCodecs.LightUpdateV1_20, p);
        Assert.Equal(p.ChunkX, d.ChunkX);
        Assert.Equal(p.ChunkZ, d.ChunkZ);
        Assert.Equal(light.SkyYMask, d.Light.SkyYMask);
        Assert.Equal(light.EmptySkyYMask, d.Light.EmptySkyYMask);
        Assert.Single(d.Light.SkyUpdates);
        Assert.Equal(sky, d.Light.SkyUpdates[0]);
        Assert.Equal(block, d.Light.BlockUpdates[0]);
    }

    [Fact]
    public void LightUpdate_EmptyEverything_RoundTrips()
    {
        var light = new LightUpdateData([], [], [], [], [], []);
        var p = new ClientboundLightUpdatePacket(0, 0, light);
        var d = CodecRoundTrip.Cycle(WorldStateCodecs.LightUpdateV1_20, p);
        Assert.Empty(d.Light.SkyUpdates);
        Assert.Empty(d.Light.BlockYMask);
    }

    // Modern split world border.

    [Fact]
    public void InitializeBorder_RoundTrips()
    {
        var p = new ClientboundInitializeBorderPacket(1.5, -2.5, 100.0, 200.0, 5000L, 29999984, 5, 15);
        Assert.Equal(p, CodecRoundTrip.Cycle(WorldBorderCodecs.InitializeBorderV1_17, p));
    }

    [Fact]
    public void BorderSplitPackets_RoundTrip()
    {
        Assert.Equal(new ClientboundSetBorderCenterPacket(3.0, 4.0),
            CodecRoundTrip.Cycle(WorldBorderCodecs.SetBorderCenterV1_17, new ClientboundSetBorderCenterPacket(3.0, 4.0)));
        Assert.Equal(new ClientboundSetBorderSizePacket(64.0),
            CodecRoundTrip.Cycle(WorldBorderCodecs.SetBorderSizeV1_17, new ClientboundSetBorderSizePacket(64.0)));
        Assert.Equal(new ClientboundSetBorderLerpSizePacket(64.0, 128.0, 3000L),
            CodecRoundTrip.Cycle(WorldBorderCodecs.SetBorderLerpSizeV1_17, new ClientboundSetBorderLerpSizePacket(64.0, 128.0, 3000L)));
        Assert.Equal(new ClientboundSetBorderWarningDelayPacket(10),
            CodecRoundTrip.Cycle(WorldBorderCodecs.SetBorderWarningDelayV1_17, new ClientboundSetBorderWarningDelayPacket(10)));
        Assert.Equal(new ClientboundSetBorderWarningDistancePacket(6),
            CodecRoundTrip.Cycle(WorldBorderCodecs.SetBorderWarningDistanceV1_17, new ClientboundSetBorderWarningDistancePacket(6)));
    }

    // Combined 1.8 world border.

    [Theory]
    [InlineData(WorldBorderAction.SetSize)]
    [InlineData(WorldBorderAction.LerpSize)]
    [InlineData(WorldBorderAction.SetCenter)]
    [InlineData(WorldBorderAction.Initialize)]
    [InlineData(WorldBorderAction.SetWarningTime)]
    [InlineData(WorldBorderAction.SetWarningBlocks)]
    public void WorldBorder_ActionDiscriminatedBody_RoundTripsEachAction(WorldBorderAction action)
    {
        var p = new ClientboundWorldBorderPacket(action, 1.0, 2.0, 100.0, 200.0, 5000L, 29999984, 15, 5);
        var d = CodecRoundTrip.Cycle(WorldBorderCodecs.WorldBorderV1_8, p);
        Assert.Equal(action, d.Action);
        switch (action)
        {
            case WorldBorderAction.SetSize:
                Assert.Equal(p.NewSize, d.NewSize);
                break;
            case WorldBorderAction.Initialize:
                Assert.Equal(p, d);
                break;
            case WorldBorderAction.SetCenter:
                Assert.Equal(p.CenterX, d.CenterX);
                Assert.Equal(p.CenterZ, d.CenterZ);
                break;
        }
    }

    // Game and level events.

    [Fact]
    public void GameEvent_RoundTripsAcrossValueLayouts()
    {
        var p = new ClientboundGameEventPacket(3, 1.0f);
        Assert.Equal(p, CodecRoundTrip.Cycle(WorldStateCodecs.GameEventV1_8, p));
        Assert.Equal(p, CodecRoundTrip.Cycle(WorldStateCodecs.GameEventV1_14, p));
    }

    [Fact]
    public void LevelEvent_RoundTripsAcrossPositionLayouts()
    {
        var p = new ClientboundLevelEventPacket(2001, SamplePos, 12345, Global: true);
        Assert.Equal(p, CodecRoundTrip.Cycle(WorldEffectCodecs.LevelEventV1_8, p));
        Assert.Equal(p, CodecRoundTrip.Cycle(WorldEffectCodecs.LevelEventV1_14, p));
    }

    // Sounds.

    [Fact]
    public void Sound_RoundTrips_RegistryHolder()
    {
        var p = new ClientboundSoundPacket(new SoundEventHolder(100, null, null), Source: 3, X: 800, Y: 512, Z: -400, Volume: 1.0f, Pitch: 1.0f, Seed: 42L);
        var d = CodecRoundTrip.Cycle(WorldEffectCodecs.SoundV1_19, p);
        Assert.Equal(p, d);
    }

    [Fact]
    public void Sound_RoundTrips_InlineHolderWithFixedRange()
    {
        var p = new ClientboundSoundPacket(new SoundEventHolder(-1, "minecraft:custom.sound", 16.0f), 0, 1, 2, 3, 0.5f, 2.0f, 7L);
        var d = CodecRoundTrip.Cycle(WorldEffectCodecs.SoundV1_19, p);
        Assert.Equal(p.Sound, d.Sound);
    }

    [Fact]
    public void Sound_RoundTrips_InlineHolderNoFixedRange()
    {
        var p = new ClientboundSoundPacket(new SoundEventHolder(-1, "minecraft:custom.sound", null), 0, 1, 2, 3, 0.5f, 2.0f, 7L);
        var d = CodecRoundTrip.Cycle(WorldEffectCodecs.SoundV1_19, p);
        Assert.Null(d.Sound.FixedRange);
    }

    [Fact]
    public void SoundEntity_RoundTrips()
    {
        var p = new ClientboundSoundEntityPacket(new SoundEventHolder(50, null, null), Source: 1, EntityId: 999, Volume: 1.0f, Pitch: 0.8f, Seed: 3L);
        Assert.Equal(p, CodecRoundTrip.Cycle(WorldEffectCodecs.SoundEntityV1_14, p));
    }

    [Fact]
    public void NamedSound_StringIdentifierBody_RoundTrips()
    {
        var p = new ClientboundNamedSoundPacket("random.pop", 800, 512, -400, 1.0f, 63);
        Assert.Equal(p, CodecRoundTrip.Cycle(WorldEffectCodecs.NamedSoundV1_8, p));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void StopSound_RoundTrips_AllFlagCombos(bool hasSource, bool hasName)
    {
        var p = new ClientboundStopSoundPacket(hasSource ? 2 : null, hasName ? "minecraft:music" : null);
        var d = CodecRoundTrip.Cycle(WorldEffectCodecs.StopSoundV1_13, p);
        Assert.Equal(p.Source, d.Source);
        Assert.Equal(p.Name, d.Name);
    }

    // Explosion.

    [Fact]
    public void Explode_BlockOffsetBody_RoundTrips()
    {
        var blocks = new ExplosionBlock[] { new(1, -2, 3), new(-4, 5, -6) };
        var p = new ClientboundExplodePacket(new Vec3d(10.5, 64.0, -20.5), 4.0f, blocks, 0.1f, -0.2f, 0.3f,
            Knockback: null, Particle: null, Sound: null, Radius: 0f, BlockCount: 0, BlockParticles: []);
        var d = CodecRoundTrip.Cycle(WorldEffectCodecs.ExplodeV1_8, p);
        Assert.Equal((float)p.Center.X, (float)d.Center.X);
        Assert.Equal(blocks, d.LegacyBlocks);
        Assert.Equal(p.LegacyMotionX, d.LegacyMotionX);
    }

    [Fact]
    public void Explode_EmptyBlockOffsetBody_RoundTrips()
    {
        var p = new ClientboundExplodePacket(new Vec3d(0, 0, 0), 1.0f, [], 0f, 0f, 0f, null, null, null, 0f, 0, []);
        Assert.Empty(CodecRoundTrip.Cycle(WorldEffectCodecs.ExplodeV1_8, p).LegacyBlocks);
    }

    [Fact]
    public void Explode_ParticleBody_RoundTripsWithKnockback()
    {
        var particle = new ParticleData(1, EncodeVarInt(42)); // block particle, block-state id
        var p = new ClientboundExplodePacket(new Vec3d(1, 2, 3), 0f, [], 0f, 0f, 0f,
            new Vec3d(0.1, 0.2, 0.3), particle, new SoundEventHolder(100, null, null), 0f, 0, []);
        var d = CodecRoundTrip.Cycle(WorldEffectCodecs.ExplodeV1_21_5, p);
        Assert.Equal(p.Knockback, d.Knockback);
        Assert.Equal(particle.TypeId, d.Particle!.Value.TypeId);
        Assert.Equal(particle.Options, d.Particle.Value.Options);
        Assert.Equal(p.Sound, d.Sound);
    }

    [Fact]
    public void Explode_ParticleBody_RoundTripsWithoutKnockback()
    {
        var particle = new ParticleData(0, []);
        var p = new ClientboundExplodePacket(new Vec3d(1, 2, 3), 0f, [], 0f, 0f, 0f,
            Knockback: null, particle, new SoundEventHolder(-1, "minecraft:boom", null), 0f, 0, []);
        var d = CodecRoundTrip.Cycle(WorldEffectCodecs.ExplodeV1_21_5, p);
        Assert.Null(d.Knockback);
    }

    [Fact]
    public void Explode_BlockParticleBody_RoundTrips()
    {
        var particle = new ParticleData(0, []);
        var blockParticles = new ExplosionParticleInfo[]
        {
            new(new ParticleData(1, EncodeVarInt(10)), 1.0f, 0.5f, 3),
            new(new ParticleData(0, []), 2.0f, 1.5f, 1),
        };
        var p = new ClientboundExplodePacket(new Vec3d(5, 6, 7), 0f, [], 0f, 0f, 0f,
            new Vec3d(0.1, 0.2, 0.3), particle, new SoundEventHolder(100, null, null), Radius: 3.5f, BlockCount: 42, blockParticles);
        var d = CodecRoundTrip.Cycle(WorldEffectCodecs.ExplodeV26_2, p);
        Assert.Equal(p.Radius, d.Radius);
        Assert.Equal(p.BlockCount, d.BlockCount);
        Assert.Equal(2, d.BlockParticles.Count);
        Assert.Equal(blockParticles[0].Weight, d.BlockParticles[0].Weight);
        Assert.Equal(blockParticles[0].Particle.Options, d.BlockParticles[0].Particle.Options);
    }

    [Fact]
    public void Explode_EmptyBlockParticleBody_RoundTrips()
    {
        var p = new ClientboundExplodePacket(new Vec3d(0, 0, 0), 0f, [], 0f, 0f, 0f,
            null, new ParticleData(0, []), new SoundEventHolder(1, null, null), 0f, 0, []);
        Assert.Empty(CodecRoundTrip.Cycle(WorldEffectCodecs.ExplodeV26_2, p).BlockParticles);
    }

    // Map data.

    [Fact]
    public void MapItemData_PackedIconBody_RoundTripsWithPatch()
    {
        var icons = new MapIcon[]
        {
            new(2, 10, -20, 3, DisplayName: null),
            new(0, -5, 5, 0, DisplayName: null),
        };
        byte[] colors = [1, 2, 3, 4, 5, 6];
        var p = new ClientboundMapItemDataPacket(7, Scale: 2, Locked: false, icons, new MapPatch(3, 2, 4, 5, colors), TrackingPosition: null);
        var d = CodecRoundTrip.Cycle(WorldMapCodecs.MapItemDataV1_8, p);
        Assert.Equal(7, d.MapId);
        Assert.Equal(2, d.Icons!.Count);
        Assert.Equal(icons[0].Type, d.Icons[0].Type);
        Assert.Equal(icons[0].X, d.Icons[0].X);
        Assert.Equal(icons[0].Rotation, d.Icons[0].Rotation);
        Assert.Equal(colors, d.Patch.Colors);
    }

    [Fact]
    public void MapItemData_PackedIconBody_RoundTripsWithoutPatch()
    {
        var p = new ClientboundMapItemDataPacket(1, 0, false, [], new MapPatch(0, 0, 0, 0, []), TrackingPosition: null);
        var d = CodecRoundTrip.Cycle(WorldMapCodecs.MapItemDataV1_8, p);
        Assert.Equal(0, d.Patch.Columns);
        Assert.Empty(d.Icons!);
    }

    [Fact]
    public void MapItemData_DecorationBody_RoundTripsWithPatch()
    {
        var icons = new MapIcon[]
        {
            new(5, 12, -34, 8, Component.Text("Home")),
            new(1, 0, 0, 0, DisplayName: null),
        };
        var p = new ClientboundMapItemDataPacket(3, 1, true, icons, new MapPatch(2, 2, 1, 1, [7, 8, 9, 10]), TrackingPosition: null);
        var d = CodecRoundTrip.Cycle(WorldMapCodecs.MapItemDataV1_21_5, p);
        Assert.Equal(p.MapId, d.MapId);
        Assert.True(d.Locked);
        Assert.Equal(2, d.Icons!.Count);
        Assert.Equal(icons[0].Type, d.Icons[0].Type);
        Assert.NotNull(d.Icons[0].DisplayName);
        Assert.Null(d.Icons[1].DisplayName);
        Assert.Equal(p.Patch.Colors, d.Patch.Colors);
    }

    [Fact]
    public void MapItemData_EmptyDecorationBody_RoundTrips()
    {
        var p = new ClientboundMapItemDataPacket(9, 0, false, Icons: null, new MapPatch(0, 0, 0, 0, []), TrackingPosition: null);
        var d = CodecRoundTrip.Cycle(WorldMapCodecs.MapItemDataV1_21_5, p);
        Assert.Null(d.Icons);
        Assert.Equal(0, d.Patch.Columns);
    }

    private static byte[] EncodeVarInt(int value)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteVarInt(value);
        return buffer.WrittenSpan.ToArray();
    }
}
