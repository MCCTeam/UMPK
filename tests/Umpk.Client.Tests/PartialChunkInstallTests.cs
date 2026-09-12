using System.Buffers;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>A chunk packet that carries only SOME of a column's sections must not erase the sections it does not carry. Every protocol from 1.8 to 1.16.5 puts a full-chunk (ground-up) flag on the wire precisely because the server re-sends a chunk mid-session carrying only the sections that changed.</summary>
/// <remarks>A partial chunk update can replace selected sections only after the full column is loaded. Unselected sections must remain unchanged. The server can use this form when many blocks change in one chunk.</remarks>
public sealed class PartialChunkInstallTests
{
    private const int StoneState = 16;   // (id 1 << 4) | meta 0, the id the live 1.9 capture shows.
    private const int FloorY = 63;       // section 3: the platform floor the capture lost.
    private const int StepY = 64;        // section 4: the step block, which survived.

    /// <summary>The captured ordering, replayed through the real 1.9 codec, the real applier and the real world: full column, then the step's block update, then a NON-FULL chunk packet carrying section 4 alone. The third packet must preserve sections 0-3 so the floor remains present.</summary>
    [Fact]
    public async Task NonFullChunkInstall_KeepsTheSectionsItDoesNotCarry_1_9()
    {
        ApplierHarness harness = await JoinedAsync(JavaVersions.V1_9);

        // 1. the join stream's full column: stone floor filling section 3's top layer, section 4 air.
        await harness.ApplyAsync(DecodePre1_13Frame(
            fullChunk: true,
            sections: [(3, FloorLayer()), (4, Air())]));

        Game.World.World world = harness.State.World;
        Assert.Equal(StoneState, world.GetBlockStateId(new BlockPos(0, FloorY, 0)));

        // 2. the step block, placed by RCON 8 seconds later: one block, one block-update packet.
        await harness.ApplyAsync(new ClientboundBlockUpdatePacket(new BlockPos(2, StepY, 2), StoneState));
        Assert.Equal(StoneState, world.GetBlockStateId(new BlockPos(2, StepY, 2)));

        // 3. the fill's 64-change chunk resend: NOT full, section 4 only, carrying the step.
        await harness.ApplyAsync(DecodePre1_13Frame(
            fullChunk: false,
            sections: [(4, WithStep())]));

        // The section the packet carried is updated...
        Assert.Equal(StoneState, world.GetBlockStateId(new BlockPos(2, StepY, 2)));

        // ...and the section it never mentioned still has the floor. THIS is the captured failure.
        Assert.Equal(StoneState, world.GetBlockStateId(new BlockPos(0, FloorY, 0)));
        Assert.Equal(StoneState, world.GetBlockStateId(new BlockPos(15, FloorY, 15)));
    }

    /// <summary>The same ordering on 1.8, whose <c>minecraft:level_chunk</c> has its own codec family.</summary>
    [Fact]
    public async Task NonFullChunkInstall_KeepsTheSectionsItDoesNotCarry_1_8()
    {
        ApplierHarness harness = await JoinedAsync(JavaVersions.V1_8);

        await harness.ApplyAsync(DecodeLegacyFrame(groundUp: true, sections: [(3, FloorLayer()), (4, Air())]));

        Game.World.World world = harness.State.World;
        Assert.Equal(StoneState, world.GetBlockStateId(new BlockPos(0, FloorY, 0)));

        await harness.ApplyAsync(new ClientboundBlockUpdatePacket(new BlockPos(2, StepY, 2), StoneState));
        await harness.ApplyAsync(DecodeLegacyFrame(groundUp: false, sections: [(4, WithStep())]));

        Assert.Equal(StoneState, world.GetBlockStateId(new BlockPos(2, StepY, 2)));
        Assert.Equal(StoneState, world.GetBlockStateId(new BlockPos(0, FloorY, 0)));
    }

    /// <summary>The other two codec families that carry the flag. 1.14/1.15 and 1.16-1.16.5 frame the column differently (heightmap NBT, chunk-level biomes, paletted containers instead of the pre-1.13 section walk), and each has its own decoder, so each needs its own frame rather than an assumption that fixing one covers the rest.</summary>
    [Theory]
    [InlineData(477)]  // 1.14
    [InlineData(751)]  // 1.16.2-1.16.5
    public async Task NonFullChunkInstall_KeepsTheSectionsItDoesNotCarry_1_14_And_1_16(int protocol)
    {
        JavaVersion version = protocol == 477 ? JavaVersions.V1_14 : JavaVersions.V1_16_2;
        ApplierHarness harness = await JoinedAsync(version);

        // A full column: section 3 is solid stone, section 4 is air.
        await harness.ApplyAsync(DecodeUniformSectionFrame(protocol, fullChunk: true, [(3, StoneState), (4, 0)]));

        Game.World.World world = harness.State.World;
        Assert.Equal(StoneState, world.GetBlockStateId(new BlockPos(0, FloorY, 0)));
        Assert.Equal(0, world.GetBlockStateId(new BlockPos(0, StepY, 0)));

        // A non-full resend of section 4 alone. Section 4 must change; section 3 must not vanish.
        await harness.ApplyAsync(DecodeUniformSectionFrame(protocol, fullChunk: false, [(4, StoneState)]));

        Assert.Equal(StoneState, world.GetBlockStateId(new BlockPos(0, StepY, 0)));
        Assert.Equal(StoneState, world.GetBlockStateId(new BlockPos(0, FloorY, 0)));
    }

    /// <summary>A full chunk packet remains a whole-column install, so a section the new full packet omits must be CLEARED, not preserved. Without this leg a unconditional merge would look correct here but leave the client holding terrain the server has replaced.</summary>
    [Fact]
    public async Task FullChunkInstall_StillReplacesTheWholeColumn_1_9()
    {
        ApplierHarness harness = await JoinedAsync(JavaVersions.V1_9);

        await harness.ApplyAsync(DecodePre1_13Frame(true, [(3, FloorLayer()), (4, Air())]));
        Game.World.World world = harness.State.World;
        Assert.Equal(StoneState, world.GetBlockStateId(new BlockPos(0, FloorY, 0)));

        // A full re-send whose mask no longer names section 3: the floor is gone for real this time.
        await harness.ApplyAsync(DecodePre1_13Frame(true, [(4, Air())]));
        Assert.Equal(0, world.GetBlockStateId(new BlockPos(0, FloorY, 0)));
    }

    /// <summary>The other direction, and the one the first six cases could not see: a section the frame DOES carry must replace the loaded section WHOLESALE, including clearing cells the server no longer has. Every other leg adds stone where there was air. This case also proves that cleared cells replace stale solids.</summary>
    /// <remarks>A block-wise merge that copies only non-air cells would leave removed blocks in the client world. Mutation testing confirmed that only this case detects that implementation.</remarks>
    [Fact]
    public async Task NonFullChunkInstall_ReplacesACarriedSectionWholesale_ClearingWhatTheServerCleared()
    {
        ApplierHarness harness = await JoinedAsync(JavaVersions.V1_9);

        // A full column: the floor in section 3, and a solid block in section 4 at (2, 64, 2).
        await harness.ApplyAsync(DecodePre1_13Frame(
            fullChunk: true,
            sections: [(3, FloorLayer()), (4, SingleBlock(2, 0, 2))]));

        Game.World.World world = harness.State.World;
        Assert.Equal(StoneState, world.GetBlockStateId(new BlockPos(2, StepY, 2)));

        // The server clears that block and sets another; 64+ changes in the tick, so section 4 comes back as a non-full chunk frame. Its copy of the cell is AIR, and it is genuinely carried (stone elsewhere in the same section), so a section that is merely "not all air" cannot fake this.
        await harness.ApplyAsync(DecodePre1_13Frame(
            fullChunk: false,
            sections: [(4, SingleBlock(5, 0, 5))]));

        // The carried section replaced what was there: the cleared cell is air...
        Assert.Equal(0, world.GetBlockStateId(new BlockPos(2, StepY, 2)));

        // ...the new block is present, so the section really was carried and applied...
        Assert.Equal(StoneState, world.GetBlockStateId(new BlockPos(5, StepY, 5)));

        // ...and the section the frame never mentioned is still untouched.
        Assert.Equal(StoneState, world.GetBlockStateId(new BlockPos(0, FloorY, 0)));
    }

    /// <summary>The flag has to survive the codec, or the applier cannot act on it. Read straight off the decoded packet, for all four pre-1.17 codec families and both values.</summary>
    [Fact]
    public void ChunkCodecs_CarryTheFullChunkFlagOffTheWire()
    {
        Assert.True(DecodePre1_13Frame(true, [(4, Air())]).FullChunk);
        Assert.False(DecodePre1_13Frame(false, [(4, Air())]).FullChunk);
        Assert.True(DecodeLegacyFrame(true, [(4, Air())]).FullChunk);
        Assert.False(DecodeLegacyFrame(false, [(4, Air())]).FullChunk);
        Assert.True(DecodeUniformSectionFrame(477, true, [(4, 0)]).FullChunk);
        Assert.False(DecodeUniformSectionFrame(477, false, [(4, 0)]).FullChunk);
        Assert.True(DecodeUniformSectionFrame(751, true, [(4, 0)]).FullChunk);
        Assert.False(DecodeUniformSectionFrame(751, false, [(4, 0)]).FullChunk);
    }

    private static async Task<ApplierHarness> JoinedAsync(JavaVersion version)
    {
        var harness = new ApplierHarness(version);
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: -1, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);
        await harness.ApplyAsync(new ClientboundLoginPacket(
            PlayerId: 1, Hardcore: false, Dimensions: [], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false,
            Legacy: new LegacyLoginFields(Dimension: 0, Difficulty: 2, LevelType: "default")));
        return harness;
    }

    /// <summary>4096 block-state ids for a section whose TOP layer (local y 15, world y 63) is stone.</summary>
    private static int[] FloorLayer()
    {
        var states = new int[4096];
        for (int z = 0; z < 16; z++)
            for (int x = 0; x < 16; x++)
                states[CellIndex(x, 15, z)] = StoneState;

        return states;
    }

    private static int[] Air() => new int[4096];

    /// <summary>Section 4 as the server holds it after the step block was placed at (2, 64, 2).</summary>
    private static int[] WithStep() => SingleBlock(2, 0, 2);

    /// <summary>4096 block-state ids: air everywhere but one stone cell at the given section-local x/y/z.</summary>
    private static int[] SingleBlock(int x, int y, int z)
    {
        var states = new int[4096];
        states[CellIndex(x, y, z)] = StoneState;
        return states;
    }

    // The pre-1.13 section cell order the decoder unpacks: index = (y << 8) | (z << 4) | x.
    private static int CellIndex(int x, int y, int z) => (y << 8) | (z << 4) | x;

    /// <summary>A 1.14 or 1.16.2-1.16.5 <c>minecraft:level_chunk</c> frame whose present sections are uniform. Wire: int x, int z, bool fullChunk, VarInt bitmask, named-root NBT heightmaps, then (full chunks only) the chunk-level biomes - a VarInt array before the section buffer on 1.16.2+, an int[256] riding at the END of the buffer on 1.14 - the VarInt-prefixed section buffer, and the block-entity list. Each section is a 4-bit indirect paletted container over [air, stone]; four bits divide 64 evenly, so the 1.14 straddling storage and the 1.16 padded storage are the same bytes and one builder serves both.</summary>
    private static ClientboundLevelChunkPacket DecodeUniformSectionFrame(
        int protocol, bool fullChunk, (int Section, int State)[] sections)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var bw = new PacketWriter(buffer);
        int mask = 0;
        foreach ((int section, int state) in sections)
        {
            mask |= 1 << section;
            bw.WriteShort(state == 0 ? (short)0 : (short)4096);   // non-empty block count
            bw.WriteByte(4);                                      // bits per entry: 4, indirect palette
            bw.WriteVarInt(2);
            bw.WriteVarInt(0);                                    // palette[0] = air
            bw.WriteVarInt(StoneState);                           // palette[1] = stone
            bw.WriteVarInt(256);                                  // 4096 cells * 4 bits / 64
            long packed = state == 0 ? 0L : unchecked((long)0x1111111111111111UL);
            for (int i = 0; i < 256; i++)
                bw.WriteLong(packed);

        }

        if (fullChunk && protocol == 477)
        {
            bw.WriteBytes(new byte[256 * 4]);   // 1.14 biomes ride at the tail of the buffer
        }

        var frame = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(frame);
        w.WriteInt(0);
        w.WriteInt(0);
        w.WriteBool(fullChunk);
        w.WriteVarInt(mask);
        w.WriteNbt(new NbtCompound(), NbtWireFormat.JavaNamedRoot);

        if (fullChunk && protocol != 477)
        {
            w.WriteVarInt(1024);                // 1.16.2+ biomes: a VarInt array before the buffer
            for (int i = 0; i < 1024; i++)
                w.WriteVarInt(0);

        }

        w.WriteVarInt(buffer.WrittenCount);
        w.WriteBytes(buffer.WrittenSpan);
        w.WriteVarInt(0);                       // empty block-entity list

        byte[] bytes = frame.WrittenSpan.ToArray();
        var reader = new PacketReader(bytes);
        PacketCodec<ClientboundLevelChunkPacket> codec = protocol == 477 ? ChunkCodecs.V1_14 : ChunkCodecs.V1_16_2;
        return codec.Decode(ref reader, PacketCodecContext.Registryless);
    }

    /// <summary>A 1.9-1.12.2 <c>minecraft:level_chunk</c> frame: int x, int z, bool fullChunk, VarInt bitmask, VarInt-prefixed section buffer, and on a full chunk the trailing byte[256] biome array. Each section is bitsPerBlock=8 over the direct/global palette, then block light and sky light.</summary>
    private static ClientboundLevelChunkPacket DecodePre1_13Frame(
        bool fullChunk, (int Section, int[] States)[] sections)
    {
        var body = new ArrayBufferWriter<byte>();
        var bw = new PacketWriter(body);
        int mask = 0;
        foreach ((int section, int[] states) in sections)
        {
            mask |= 1 << section;
            bw.WriteByte(8);          // bits per block
            bw.WriteVarInt(0);        // palette length 0 = the direct/global palette
            bw.WriteVarInt(512);      // 4096 cells * 8 bits / 64 = 512 longs
            for (int i = 0; i < 512; i++)
            {
                ulong packed = 0;
                for (int slot = 0; slot < 8; slot++)
                    packed |= (ulong)(uint)states[(i * 8) + slot] << (slot * 8);

                bw.WriteLong((long)packed);
            }

            bw.WriteBytes(new byte[2048]);   // block light
            bw.WriteBytes(new byte[2048]);   // sky light (overworld)
        }

        if (fullChunk)
        {
            bw.WriteBytes(new byte[256]);    // biomes, present on a ground-up chunk only
        }

        var frame = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(frame);
        w.WriteInt(0);
        w.WriteInt(0);
        w.WriteBool(fullChunk);
        w.WriteVarInt(mask);
        w.WriteVarInt(body.WrittenCount);
        w.WriteBytes(body.WrittenSpan);

        byte[] bytes = frame.WrittenSpan.ToArray();
        var reader = new PacketReader(bytes);
        return ChunkCodecs.V1_9.Decode(ref reader, PacketCodecContext.Registryless);
    }

    /// <summary>A 1.8 <c>minecraft:level_chunk</c> frame: int x, int z, bool groundUp, ushort bitmask, then a VarInt-prefixed blob of flat little-endian ushort block states per present section, its light, and on a ground-up chunk the byte[256] biome array.</summary>
    private static ClientboundLevelChunkPacket DecodeLegacyFrame(
        bool groundUp, (int Section, int[] States)[] sections)
    {
        var body = new ArrayBufferWriter<byte>();
        var bw = new PacketWriter(body);
        int mask = 0;
        foreach ((int section, int[] states) in sections)
        {
            mask |= 1 << section;
            for (int y = 0; y < 16; y++)
                for (int z = 0; z < 16; z++)
                    for (int x = 0; x < 16; x++)
                    {
                        int state = states[CellIndex(x, y, z)];
                        bw.WriteByte((byte)(state & 0xFF));
                        bw.WriteByte((byte)((state >> 8) & 0xFF));
                    }

        }

        foreach ((int _, int[] _) in sections)
        {
            bw.WriteBytes(new byte[2048]);   // block light, one array per present section
        }

        foreach ((int _, int[] _) in sections)
        {
            bw.WriteBytes(new byte[2048]);   // sky light, one array per present section
        }

        if (groundUp)
            bw.WriteBytes(new byte[256]);

        var frame = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(frame);
        w.WriteInt(0);
        w.WriteInt(0);
        w.WriteBool(groundUp);
        w.WriteUShort((ushort)mask);
        w.WriteVarInt(body.WrittenCount);
        w.WriteBytes(body.WrittenSpan);

        byte[] bytes = frame.WrittenSpan.ToArray();
        var reader = new PacketReader(bytes);
        return ChunkCodecs.V1_8.Decode(ref reader, PacketCodecContext.Registryless);
    }
}
