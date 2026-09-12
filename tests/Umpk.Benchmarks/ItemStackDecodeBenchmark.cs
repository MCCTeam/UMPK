using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.TestKit.Corpus;

namespace Umpk.Benchmarks;

/// <summary>Decode time and allocation for a recorded <c>container_set_content</c>, the packet that carries a whole inventory of item stacks, on one protocol per component-era table.</summary>
/// <remarks>766 is the first structured-component table, 770 changes the component id ordering, and 776 is the current tail. The recorded inventories are a player's own 46-slot container as an offline server hands it over, so the stacks are mostly empty and the number is the per-slot framing cost rather than a worst case for component payloads; the witness corpus is where a loaded inventory belongs.</remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class ItemStackDecodeBenchmark
{
    [Params(766, 770, 776)]
    public int Protocol { get; set; }

    private BoundPacketCodec _binding = null!;
    private byte[] _body = [];
    private PacketCodecContext _context = null!;

    [GlobalSetup]
    public void Setup()
    {
        CorpusFrameRef reference = CorpusFrames.Require(
            Protocol, ProtocolPhase.Play, PacketFlow.Clientbound, "minecraft:container_set_content");
        (_binding, _body) = CorpusFrames.Load(reference);
        _context = new PacketCodecContext(JavaGameData.Registries(Protocol), IConnectionCodecState.Empty);
    }

    [Benchmark]
    public object DecodeContainerContents() => _binding.Decode(_body, _context);
}
