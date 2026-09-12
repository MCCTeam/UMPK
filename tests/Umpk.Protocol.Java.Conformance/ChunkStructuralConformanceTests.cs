using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Umpk.TestKit.Corpus;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>The structural chunk verification leg (no round-trip tautology). For EVERY level_chunk frame in EVERY committed corpus: resolve the frame through the protocol descriptor (wire-id truth, no shape sniffing), decode it with the production codec, then re-encode it STRUCTURALLY via <see cref="ChunkCodecs.EncodeStructural"/>, with the <see cref="ClientboundLevelChunkPacket.RawBody"/> verbatim carrier explicitly bypassed, and byte-compare against the recorded frame. Because the expected body predates decoding, a corrupt palette, section calculation, or bit unpacking re-encodes to different bytes and fails here.</summary>
public sealed class ChunkStructuralConformanceTests
{
    // The canonical PacketType id every protocol registers the chunk codec under (the 1.8 dataset name minecraft:level_chunk is mapped to the same PacketType by the registrar).
    private static readonly Identifier LevelChunkId = PlayPackets.Clientbound.LevelChunk.Id;

    private readonly ITestOutputHelper _output;

    public ChunkStructuralConformanceTests(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> Captures()
    {
        foreach (string path in CorpusLoader.DiscoverCaptures(FixturePaths.CorpusRoot))
            yield return [path];

    }

    [Theory]
    [MemberData(nameof(Captures))]
    public async Task EveryChunkFrame_InEveryCorpus_StructurallyReEncodesByteIdentical(string capturePath)
    {
        LoadedCorpus corpus = await CorpusLoader.LoadFileAsync(capturePath);
        Assert.True(JavaVersions.TryGetByProtocol(corpus.Protocol, out JavaVersion? version),
            $"Unknown protocol {corpus.Protocol} in {capturePath}.");
        ProtocolDescriptor descriptor = version!.Protocol;

        Assert.True(descriptor.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry play));
        (int chunkWireId, BoundPacketCodec chunkCodec) = ResolveLevelChunk(play);

        int chunkFrames = 0;
        var failures = new List<string>();
        foreach (RecordedFrame frame in corpus.Frames)
        {
            if (!IsPlayClientboundChunkFrame(descriptor, frame, chunkWireId))
                continue;

            chunkFrames++;
            ClientboundLevelChunkPacket packet;
            try
            {
                packet = (ClientboundLevelChunkPacket)chunkCodec.Decode(frame.Body, PacketCodecContext.Registryless);
            }
            catch (Exception ex) when (ex is ProtocolViolationException or NotSupportedException)
            {
                // Unlike the lenient outcome buckets, a chunk decode throw is a hard failure here: the structural leg exists to certify decode correctness, not to report it.
                failures.Add($"seq {frame.Sequence}: decode threw {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            if (packet.RawBody is null)
            {
                failures.Add($"seq {frame.Sequence}: decode retained no wire body to rebuild from.");
                continue;
            }

            byte[] structural = ChunkCodecs.EncodeStructural(ChunkCodecs.DecodeStructure(packet));
            if (!frame.Body.AsSpan().SequenceEqual(structural))
                failures.Add(
                    $"seq {frame.Sequence}: structural re-encode differs (recorded {frame.Body.Length} bytes, " +
                    $"structural {structural.Length} bytes, first diff at {FirstDiff(frame.Body, structural)}).");

        }

        _output.WriteLine(
            $"{Path.GetFileName(capturePath)} (protocol {corpus.Protocol}): {chunkFrames} chunk frames, " +
            $"{failures.Count} structural failures.");
        foreach (string failure in failures.Take(10))
            _output.WriteLine("  " + failure);

        Assert.Empty(failures);

        // The dedicated chunk scenarios must actually exercise the leg; a corpus or registration regression that yielded zero chunk frames would otherwise verify nothing.
        if (Path.GetFileNameWithoutExtension(capturePath).StartsWith("chunk-", StringComparison.Ordinal))
            Assert.True(chunkFrames > 0, $"no level_chunk frames found in {capturePath}; corpus or registration regressed.");

    }

    private static (int WireId, BoundPacketCodec Codec) ResolveLevelChunk(PhaseRegistry play)
    {
        foreach ((int wireId, PacketType type) in play.Packets)
            if (type.Id == LevelChunkId && play.TryGetInbound(wireId, out BoundPacketCodec codec))
                return (wireId, codec);

        throw new ConformanceViolation("minecraft:level_chunk is not registered in Play/Clientbound.");
    }

    // A frame counts as a Play-clientbound chunk frame when its wire id matches level_chunk and it is either labeled Play, or labeled Login while the Login registry does not know the id (the pre-1.20.2 recorder phase-lag window; the same tolerance ConformanceRunner applies).
    private static bool IsPlayClientboundChunkFrame(ProtocolDescriptor descriptor, RecordedFrame frame, int chunkWireId)
    {
        if (CorpusEnumMapping.ToFlow(frame.Direction) != PacketFlow.Clientbound || frame.WireId != chunkWireId)
            return false;

        ProtocolPhase phase = CorpusEnumMapping.ToProtocolPhase(frame.Phase);
        if (phase == ProtocolPhase.Play)
            return true;

        return phase == ProtocolPhase.Login &&
            (!descriptor.TryGetRegistry(ProtocolPhase.Login, PacketFlow.Clientbound, out PhaseRegistry login) ||
             !login.TryGetInbound(frame.WireId, out _));
    }

    private static int FirstDiff(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        int len = Math.Min(a.Length, b.Length);
        for (int i = 0; i < len; i++)
            if (a[i] != b[i])
                return i;

        return len;
    }
}
