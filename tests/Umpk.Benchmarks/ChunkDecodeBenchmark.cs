using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.TestKit.Corpus;

namespace Umpk.Benchmarks;

/// <summary>Decode time and allocation for one recorded <c>level_chunk_with_light</c> per chunk-codec era band.</summary>
/// <remarks>One protocol per band of <c>fixtures/timelines/bands.txt</c>'s <c>level_chunk_with_light</c> row. The 47 band is absent because the protocol-47 corpus carries <c>map_chunk_bulk</c> and no single chunk frame. Chunk decode drains its whole payload up front and then builds the column, so this is the largest single allocation on the inbound path and the one worth watching.</remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class ChunkDecodeBenchmark
{
    [Params(107, 393, 477, 573, 735, 751, 755, 758, 764, 768, 770, 776)]
    public int Protocol { get; set; }

    private BoundPacketCodec _binding = null!;
    private byte[] _body = [];
    private PacketCodecContext _context = null!;

    [GlobalSetup]
    public void Setup()
    {
        CorpusFrameRef reference = CorpusFrames.Require(
            Protocol, ProtocolPhase.Play, PacketFlow.Clientbound, "minecraft:level_chunk_with_light");
        (_binding, _body) = CorpusFrames.Load(reference);
        _context = new PacketCodecContext(JavaGameData.Registries(Protocol), IConnectionCodecState.Empty);
    }

    [Benchmark]
    public object DecodeChunk() => _binding.Decode(_body, _context);
}
