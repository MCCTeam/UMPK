using Umpk.Data.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Umpk.TestKit.Corpus;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>The 1.8 bulk chunk codec (<c>minecraft:map_chunk_bulk</c>, protocol 47, clientbound play <c>0x26</c>), driven against recorded frames in the committed 1.8 corpora rather than a hand-built payload.</summary>
/// <remarks>
/// <para>The corpora carry six <c>0x26</c> frames totalling over three megabytes, next to a single <c>level_chunk</c> frame.</para>
/// <para>The load-bearing assertion here is <see cref="EveryBulkFrame_ConsumesExactlyItsOwnLength"/>, and it is deliberately a FRAME-LENGTH assertion. A round trip cannot catch a framing error, because encode and decode through the same wrong reading agree with each other; the recorded frame length is the only external witness. It matters more for this packet than for most: the per-chunk blob length is not on the wire at all and must be computed from the section mask and skylight flag. A decoder that guessed a length prefix would read the first chunk plausibly and produce garbage for every chunk after it, which looks exactly like a partial success.</para>
/// </remarks>
public sealed class MapChunkBulkCodecTests
{
    private const int Protocol1_8 = 47;
    private const int MapChunkBulkWireId = 0x26;
    private const int LevelChunkWireId = 0x21;

    private readonly ITestOutputHelper _output;

    public MapChunkBulkCodecTests(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> Captures()
    {
        foreach (string path in CorpusLoader.DiscoverCaptures(FixturePaths.CorpusRoot))
            if (Path.GetFileName(Path.GetDirectoryName(path)) == Protocol1_8.ToString(System.Globalization.CultureInfo.InvariantCulture))
                yield return [path];

    }

    /// <summary>The corpora must actually carry bulk frames, otherwise every theory below is vacuous and would stay green if the packet stopped resolving at all.</summary>
    [Fact]
    public async Task The_1_8_Corpora_Carry_Bulk_Frames_To_Test_Against()
    {
        int frames = 0;
        long bytes = 0;
        foreach (object[] row in Captures())
            foreach (RecordedFrame frame in await BulkFramesAsync((string)row[0]))
            {
                frames++;
                bytes += frame.Body.Length;
            }

        _output.WriteLine($"1.8 corpora: {frames} map_chunk_bulk frames, {bytes} bytes.");
        Assert.True(frames >= 6, $"expected at least six recorded bulk frames, found {frames}.");
    }

    /// <summary>The frame-length witness: header plus the per-chunk blobs, each sized from its OWN bitmask and the frame's single sky-light flag, must add up to the recorded frame length exactly. Off by one byte in the size formula and this fails; a round-trip test would not.</summary>
    [Theory]
    [MemberData(nameof(Captures))]
    public async Task EveryBulkFrame_ConsumesExactlyItsOwnLength(string capturePath)
    {
        foreach (RecordedFrame frame in await BulkFramesAsync(capturePath))
        {
            var reader = new PacketReader(frame.Body);
            bool skyLight = reader.ReadBool();
            int count = reader.ReadVarInt();
            Assert.True(count > 0, "a recorded bulk frame carried no chunks.");

            var masks = new ushort[count];
            for (int i = 0; i < count; i++)
            {
                reader.ReadInt();
                reader.ReadInt();
                masks[i] = reader.ReadUShort();
            }

            long expected = frame.Body.Length - reader.Remaining;
            foreach (ushort mask in masks)
                expected += ChunkCodecs.LegacyChunkBlobLength(
                    System.Numerics.BitOperations.PopCount((uint)mask), skyLight, hasBiomes: true);

            Assert.Equal(frame.Body.Length, expected);
        }
    }

    /// <summary>The production codec decodes every recorded frame and re-encodes byte-identically through the STRUCTURAL path, with the verbatim <c>RawBody</c> carrier bypassed. A decode that mis-sliced the block states or lost the light/biome remainder re-encodes to different bytes and fails here, so the decode cannot hide behind a copy of the recorded bytes.</summary>
    [Theory]
    [MemberData(nameof(Captures))]
    public async Task EveryBulkFrame_StructurallyReEncodesByteIdentical(string capturePath)
    {
        foreach (RecordedFrame frame in await BulkFramesAsync(capturePath))
        {
            ClientboundMapChunkBulkPacket packet = Decode(frame);
            Assert.NotNull(packet.RawBody);
            byte[] structural = ChunkCodecs.EncodeStructural(ChunkCodecs.DecodeStructure(packet));
            Assert.Equal(frame.Body.Length, structural.Length);
            Assert.True(frame.Body.AsSpan().SequenceEqual(structural), $"seq {frame.Sequence}: structural re-encode differs.");
        }
    }

    /// <summary>The decode must produce real terrain, not merely a well-formed shape. Every recorded frame is a vanilla overworld region, so every column must have bedrock (a non-air state) at y=0 in all 256 of its horizontal cells, and the frame as a whole must carry far more solid blocks than air-only framing could.</summary>
    [Theory]
    [MemberData(nameof(Captures))]
    public async Task EveryBulkFrame_DecodesToColumnsWithRealTerrain(string capturePath)
    {
        foreach (RecordedFrame frame in await BulkFramesAsync(capturePath))
        {
            ClientboundMapChunkBulkPacket packet = Decode(frame);
            Assert.NotEmpty(packet.Columns);

            foreach (Umpk.Game.World.ChunkColumn column in packet.Columns)
            {
                int floorCells = 0;
                for (int x = 0; x < 16; x++)
                    for (int z = 0; z < 16; z++)
                    {
                        int worldX = (column.Position.X << 4) + x;
                        int worldZ = (column.Position.Z << 4) + z;
                        if (column.GetBlockStateId(worldX, 0, worldZ) != 0)
                            floorCells++;

                    }

                Assert.Equal(256, floorCells);
            }
        }
    }

    /// <summary>Cross-era rejection. The single-chunk 1.8 codec and the bulk codec share the per-chunk section primitive but NOT the framing, and the registration fixture alone cannot tell them apart (it records codec-versus-marker, not which codec). Feeding each codec the other's real recorded frame must not silently succeed, which is what a misbinding of these two neighbours would look like.</summary>
    [Theory]
    [MemberData(nameof(Captures))]
    public async Task TheSingleChunkAndBulkCodecs_RejectEachOthersFrames(string capturePath)
    {
        LoadedCorpus corpus = await CorpusLoader.LoadFileAsync(capturePath);

        RecordedFrame bulk = corpus.Frames.First(IsBulk);
        // The single-chunk codec reads x/z/groundUp/mask and then a VarInt-prefixed blob. A bulk frame begins with the sky-light boolean, so the fields land on the wrong bytes and the length prefix it finds cannot be satisfied.
        Assert.ThrowsAny<Exception>(() => DecodeSingle(bulk.Body));

        RecordedFrame? single = corpus.Frames.FirstOrDefault(
            f => f.Direction == CorpusDirection.Clientbound && f.WireId == LevelChunkWireId && f.Body.Length > 0);
        if (single is not null)
            Assert.ThrowsAny<Exception>(() => DecodeBulk(single.Body));

    }

    /// <summary>The protocol 47 blob-length formula: <c>sections*8192</c> block states, <c>sections*2048</c> block light, the same again for sky light when the frame carries it, and a flat 256 bytes of biomes on a ground-up column. Pinned separately from the corpus so a future edit to the formula fails on the arithmetic itself and not only through a capture.</summary>
    [Theory]
    [InlineData(0, false, false, 0)]
    [InlineData(0, true, true, 256)]
    [InlineData(1, false, true, 8192 + 2048 + 256)]
    [InlineData(1, true, true, 8192 + 2048 + 2048 + 256)]
    [InlineData(5, true, true, (5 * 8192) + (5 * 2048) + (5 * 2048) + 256)]
    [InlineData(16, true, true, (16 * 8192) + (16 * 2048) + (16 * 2048) + 256)]
    public void LegacyChunkBlobLength_MatchesVanillaArithmetic(int sections, bool skyLight, bool biomes, int expected)
        => Assert.Equal(expected, ChunkCodecs.LegacyChunkBlobLength(sections, skyLight, biomes));

    /// <summary>The wire id must resolve to an implemented codec on 47, and to nothing anywhere else.</summary>
    [Fact]
    public void MapChunkBulk_IsBoundOnProtocol47Only()
    {
        int bound = 0;
        foreach (JavaVersion version in JavaVersions.All)
        {
            if (!version.Protocol.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry play))
                continue;

            foreach ((int wireId, PacketType type) in play.Packets)
            {
                if (type.Id != PlayPackets.Clientbound.MapChunkBulk.Id)
                    continue;

                Assert.True(play.TryGetInbound(wireId, out BoundPacketCodec codec));
                Assert.True(codec.IsImplemented, $"map_chunk_bulk is a marker on protocol {version.Version.Protocol}.");
                Assert.Equal(Protocol1_8, version.Version.Protocol);
                Assert.Equal(MapChunkBulkWireId, wireId);
                bound++;
            }
        }

        Assert.Equal(1, bound);
    }

    private static ClientboundLevelChunkPacket DecodeSingle(byte[] body)
    {
        var reader = new PacketReader(body);
        return ChunkCodecs.V1_8.Decode(ref reader, PacketCodecContext.Registryless);
    }

    private static ClientboundMapChunkBulkPacket DecodeBulk(byte[] body)
    {
        var reader = new PacketReader(body);
        return ChunkCodecs.MapChunkBulkV1_8.Decode(ref reader, PacketCodecContext.Registryless);
    }

    private static bool IsBulk(RecordedFrame frame) =>
        frame.Direction == CorpusDirection.Clientbound && frame.WireId == MapChunkBulkWireId;

    private static ClientboundMapChunkBulkPacket Decode(RecordedFrame frame)
    {
        Assert.True(JavaVersions.TryGetByProtocol(Protocol1_8, out JavaVersion? version));
        Assert.True(version!.Protocol.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry play));
        Assert.True(play.TryGetInbound(MapChunkBulkWireId, out BoundPacketCodec codec));
        Assert.True(codec.IsImplemented, "map_chunk_bulk resolved to a marker on protocol 47.");

        // Decoded through the descriptor-resolved binding, not the codec field, so the test proves the packet a live 1.8 session would actually get rather than one this test picked by hand.
        return Assert.IsType<ClientboundMapChunkBulkPacket>(codec.Decode(frame.Body, PacketCodecContext.Registryless));
    }

    private static async Task<IReadOnlyList<RecordedFrame>> BulkFramesAsync(string capturePath)
    {
        LoadedCorpus corpus = await CorpusLoader.LoadFileAsync(capturePath);
        Assert.Equal(Protocol1_8, corpus.Protocol);
        return corpus.Frames.Where(IsBulk).ToList();
    }
}
