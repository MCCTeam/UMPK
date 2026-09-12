using Umpk.Data.Java;
using Umpk.Game.World;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Umpk.TestKit.Corpus;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>The queryable-block-grid verification for the mid eras (protocols 477-756, MC 1.14-1.17.1). For every recorded level_chunk frame in these corpora, the production codec must decode the paletted sections into a populated <see cref="ChunkColumn"/> (block states queryable) ALONGSIDE the verbatim wire body that the structural/relay path re-encodes byte-for-byte (asserted by the neighbouring structural and corpus conformance legs). This closes the opaque-column gap: 1.14-1.15.2 use the pre-1.16 straddling bit storage and 1.16-1.17.1 the padded storage, both unpacked into a real grid so a spawn-area chunk yields at least one non-air block.</summary>
public sealed class PalettedChunkBlockGridTests
{
    private static readonly Identifier LevelChunkId = PlayPackets.Clientbound.LevelChunk.Id;

    private readonly ITestOutputHelper _output;

    public PalettedChunkBlockGridTests(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> Captures()
    {
        foreach (string path in CorpusLoader.DiscoverCaptures(FixturePaths.CorpusRoot))
            if (ProtocolFromPath(path) is >= 477 and <= 756)
                yield return [path];

    }

    [Theory]
    [MemberData(nameof(Captures))]
    public async Task PalettedChunkFrames_PopulateQueryableBlockGrid(string capturePath)
    {
        LoadedCorpus corpus = await CorpusLoader.LoadFileAsync(capturePath);

        Assert.True(JavaVersions.TryGetByProtocol(corpus.Protocol, out JavaVersion? version));
        ProtocolDescriptor descriptor = version!.Protocol;
        Assert.True(descriptor.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry play));

        int chunkWireId = -1;
        BoundPacketCodec? chunkCodec = null;
        foreach ((int wireId, PacketType type) in play.Packets)
            if (type.Id == LevelChunkId && play.TryGetInbound(wireId, out BoundPacketCodec codec))
            {
                chunkWireId = wireId;
                chunkCodec = codec;
                break;
            }

        Assert.NotNull(chunkCodec);

        int chunkFrames = 0;
        int populatedColumns = 0;
        long totalNonAir = 0;
        foreach (RecordedFrame frame in corpus.Frames)
        {
            if (CorpusEnumMapping.ToFlow(frame.Direction) != PacketFlow.Clientbound || frame.WireId != chunkWireId)
                continue;

            if (CorpusEnumMapping.ToProtocolPhase(frame.Phase) != ProtocolPhase.Play)
                continue;

            chunkFrames++;
            var packet = (ClientboundLevelChunkPacket)chunkCodec!.Decode(frame.Body, PacketCodecContext.Registryless);
            int nonAir = CountNonAir(packet.Column);
            totalNonAir += nonAir;
            if (nonAir > 0)
                populatedColumns++;

        }

        _output.WriteLine(
            $"{Path.GetFileName(capturePath)} (protocol {corpus.Protocol}): {chunkFrames} chunk frames, " +
            $"{populatedColumns} populated columns, {totalNonAir} non-air cells.");

        if (chunkFrames > 0)
            Assert.True(
                totalNonAir > 0,
                $"protocol {corpus.Protocol}: {chunkFrames} chunk frames decoded but the block grid was empty; " +
                "the mid-era (1.14-1.17.1) section decode did not populate any block state.");

    }

    private static int ProtocolFromPath(string path)
    {
        string? directoryName = Path.GetFileName(Path.GetDirectoryName(path));
        return int.Parse(
            directoryName ?? throw new InvalidOperationException($"Capture path has no protocol directory: {path}"),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int CountNonAir(ChunkColumn column)
    {
        int count = 0;
        for (int s = 0; s < column.SectionCount; s++)
        {
            ChunkSection? section = column.GetSection(s);
            if (section is null)
                continue;

            for (int y = 0; y < ChunkSection.Size; y++)
                for (int x = 0; x < ChunkSection.Size; x++)
                    for (int z = 0; z < ChunkSection.Size; z++)
                        if (section.GetBlockStateId(x, y, z) != 0)
                            count++;

        }

        return count;
    }
}
