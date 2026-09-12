using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.TestKit.Corpus;

namespace Umpk.Benchmarks;

/// <summary>The per-frame resolve-plus-decode sequence the read loop runs for one inbound frame: the wire-id lookup and decode, the bundle-identity pair, then the three pause-gate identity checks.</summary>
/// <remarks>
/// <para>The measured body is <see cref="FrameDispatchSequence"/>, which is what the connection's frame handler calls at each of its three sites, so this is the production path rather than a copy of it.</para>
/// <para><see cref="ResolveAcrossTheTable"/> isolates the identity half: it sweeps synthetic wire ids across the whole play clientbound table with no payload, so it measures the resolution and the four identity answers against table size (about 70 entries on 47, about 250 on 776) with no codec work in the number.</para>
/// <para>The corpus carries no clientbound <c>move_entity_pos</c> for protocol 47, so that pairing is absent from the case list rather than authored.</para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class FrameDispatchBenchmark
{
    /// <summary>Protocol and packet, as <c>protocol/packet</c>.</summary>
    public static IEnumerable<string> Cases =>
    [
        "47/keep_alive",
        "393/keep_alive",
        "393/move_entity_pos",
        "770/keep_alive",
        "770/move_entity_pos",
        "776/keep_alive",
        "776/move_entity_pos",
    ];

    [ParamsSource(nameof(Cases))]
    public string Case { get; set; } = "776/keep_alive";

    private DescriptorFrameCodecBinding _binding = null!;
    private byte[] _body = [];
    private int _wireId;
    private int _tableSize;

    [GlobalSetup]
    public void Setup()
    {
        string[] parts = Case.Split('/');
        int protocol = int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
        string packet = "minecraft:" + parts[1];

        JavaVersion version = JavaVersions.TryGetByProtocol(protocol, out JavaVersion? found)
            ? found!
            : throw new InvalidOperationException($"Unknown protocol {protocol}.");

        CorpusFrameRef reference =
            CorpusFrames.Require(protocol, ProtocolPhase.Play, PacketFlow.Clientbound, packet);
        (BoundPacketCodec binding, byte[] body) = CorpusFrames.Load(reference);
        _body = body;
        _wireId = binding.WireId;
        _binding = new DescriptorFrameCodecBinding(
            version.Protocol,
            new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty));

        _tableSize = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound).Packets.Count();
    }

    /// <summary>Decode plus the four identity lookups, which is what one delivered frame costs.</summary>
    [Benchmark]
    public object? DispatchOneFrame() =>
        FrameDispatchSequence.Run(_binding, ProtocolPhase.Play, PacketFlow.Clientbound, _wireId, _body).Packet;

    /// <summary>The decode alone, so the identity half can be read as the difference.</summary>
    [Benchmark]
    public bool DecodeOnly() =>
        FrameDispatchSequence.ResolveAndDecode(
            _binding, ProtocolPhase.Play, PacketFlow.Clientbound, _wireId, _body, decode: true,
            out _, out _, out _);

    /// <summary>The identity lookups alone, swept across every wire id the play table holds.</summary>
    [Benchmark]
    public int ResolveAcrossTheTable()
    {
        int hits = 0;
        for (int id = 0; id < _tableSize; id++)
        {
            FrameDispatchSequence.ResolveAndDecode(
                _binding, ProtocolPhase.Play, PacketFlow.Clientbound, id, [], decode: false,
                out bool resolved, out ResolvedFrame frame, out _);
            FrameDispatchSequence.ReadBundleIdentity(
                _binding, resolved, in frame, ProtocolPhase.Play, PacketFlow.Clientbound, id, [],
                out bool delimiter, out bool terminal);
            FrameDispatchSequence.ReadGates(
                _binding, resolved, in frame, ProtocolPhase.Play, PacketFlow.Clientbound, id, [], decoded: true,
                out bool compression, out bool encryption, out bool phaseChange);
            hits += (delimiter ? 1 : 0) + (terminal ? 1 : 0)
                + (compression ? 1 : 0) + (encryption ? 1 : 0) + (phaseChange ? 1 : 0);
        }

        return hits;
    }
}
