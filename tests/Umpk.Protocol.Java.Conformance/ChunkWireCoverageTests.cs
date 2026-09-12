using Umpk.Data.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Umpk.TestKit.Corpus;
using Xunit;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>The chunk family re-serialises through one virtual call, so a body without an encoder no longer compiles. Rebuilding that body from a decoded packet still goes through a switch, and this walks it: every <c>ChunkWireEra</c> value gets a committed frame from its own band, and the rebuild has to return a body that re-encodes to the recorded bytes.</summary>
/// <remarks>The protocol under each era is written here rather than read off the descriptors, so a binding that moved a band cannot make this pass by agreeing with itself.</remarks>
public sealed class ChunkWireCoverageTests
{
    private static readonly Identifier LevelChunkId = PlayPackets.Clientbound.LevelChunk.Id;

    // One protocol per era, written here rather than derived, with the era each one must report.
    private static readonly (int Protocol, ChunkWireEra Era)[] Expected =
    [
        (47, ChunkWireEra.Legacy1_8),           // the flat ushort chunk
        (340, ChunkWireEra.Pre1_13),            // verbatim section buffer, inline light
        (477, ChunkWireEra.TrailingBiomes),     // 1.14 biomes ride after the sections
        (578, ChunkWireEra.LeadingBiomes),      // 1.15 moves them before the buffer
        (735, ChunkWireEra.Bitmask1_16),        // VarInt mask plus forget-old-data
        (754, ChunkWireEra.Bitmask1_16_2),      // the 1.16.2 section-mask reshape
        (755, ChunkWireEra.Bitmask1_17),        // BitSet mask, no full-chunk flag
        (761, ChunkWireEra.NamedHeightmap757),  // 1.18 full column, NAMED heightmap root
        (765, ChunkWireEra.Prefixed764),        // 1.20.2 unnamed root, checked long arrays
        (769, ChunkWireEra.NbtHeightmap),       // 1.21.2 raw-prefixed long arrays
        (770, ChunkWireEra.Modern),             // 1.21.5 packed heightmap map, fixed-size arrays
        (776, ChunkWireEra.Modern),             // 26.2, whose sections carry the fluid count
    ];

    public static TheoryData<int> Bands
    {
        get
        {
            var data = new TheoryData<int>();
            foreach ((int protocol, _) in Expected)
                data.Add(protocol);

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Bands))]
    public async Task EveryBand_RebuildsAStructuralBodyThatReEncodesToItsFrame(int protocol)
    {
        (ClientboundLevelChunkPacket packet, byte[] body) = await FirstChunkFrameAsync(protocol);

        Assert.Equal(Expected.First(e => e.Protocol == protocol).Era, packet.WireEra);
        ChunkWireBody rebuilt = ChunkCodecs.DecodeStructure(packet);
        Assert.Equal(body, ChunkCodecs.EncodeStructural(rebuilt));
    }

    /// <summary>The rebuild's switch has a default arm, so it stays exhaustive only while every era value has a band above it. A new member without one leaves that arm reachable and unexercised.</summary>
    [Fact]
    public void EveryWireLayoutValue_HasABandInThisSuite() =>
        Assert.Empty(Enum.GetValues<ChunkWireEra>().Except(Expected.Select(static e => e.Era)));

    /// <summary>The bulk sibling is a different record with its own body, so it has its own entry point.</summary>
    [Fact]
    public async Task TheBulkSibling_RebuildsFromItsOwnOverload()
    {
        LoadedCorpus corpus = await LoadAsync(47);
        Assert.True(JavaVersions.TryGetByProtocol(47, out JavaVersion? version));
        ProtocolDescriptor descriptor = version!.Protocol;
        Assert.True(descriptor.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry play));
        (int wireId, BoundPacketCodec codec) = Resolve(play, PlayPackets.Clientbound.MapChunkBulk.Id);

        foreach (RecordedFrame frame in corpus.Frames)
        {
            if (!IsClientboundPlayFrame(frame, wireId))
                continue;

            var packet = (ClientboundMapChunkBulkPacket)codec.Decode(frame.Body, PacketCodecContext.Registryless);
            ChunkWireBody rebuilt = ChunkCodecs.DecodeStructure(packet);
            Assert.Equal(frame.Body, ChunkCodecs.EncodeStructural(rebuilt));
            return;
        }

        Assert.Fail("protocol 47's corpus carries no map_chunk_bulk frame.");
    }

    /// <summary>A packet built in code carries no wire body, so the rebuild has nothing to read and says so rather than handing back an empty model.</summary>
    [Fact]
    public void ACodeBuiltPacket_HasNoWireBodyToRebuild()
    {
        var packet = new ClientboundLevelChunkPacket(0, 0, ChunkColumnFactory.Create(0, 0, sectionCount: 0));

        Assert.Throws<NotSupportedException>(() => ChunkCodecs.DecodeStructure(packet));
    }

    private static async Task<(ClientboundLevelChunkPacket Packet, byte[] Body)> FirstChunkFrameAsync(int protocol)
    {
        LoadedCorpus corpus = await LoadAsync(protocol);
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        ProtocolDescriptor descriptor = version!.Protocol;
        Assert.True(descriptor.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry play));
        (int wireId, BoundPacketCodec codec) = Resolve(play, LevelChunkId);

        foreach (RecordedFrame frame in corpus.Frames)
        {
            if (!IsClientboundPlayFrame(frame, wireId))
                continue;

            return ((ClientboundLevelChunkPacket)codec.Decode(frame.Body, PacketCodecContext.Registryless), frame.Body);
        }

        // 1.8 delivers nearly all terrain in bulk form and sends single level_chunk frames only for stragglers, so its capture has none. Its band's witness payload is the frame it does not have.
        var key = new ProtocolTimeline.PacketKey(ProtocolPhase.Play, PacketFlow.Clientbound, LevelChunkId.ToString());
        Assert.True(
            Witnesses.For(protocol).TryGetValue(key, out ResolvedWitness? witness),
            $"protocol {protocol}'s corpus carries no level chunk frame and its band has no witness either.");

        return ((ClientboundLevelChunkPacket)codec.Decode(witness!.Payload, PacketCodecContext.Registryless), witness.Payload);
    }

    private static async Task<LoadedCorpus> LoadAsync(int protocol)
    {
        foreach (string path in CorpusLoader.DiscoverCaptures(FixturePaths.CorpusRoot))
        {
            LoadedCorpus corpus = await CorpusLoader.LoadFileAsync(path);
            if (corpus.Protocol == protocol)
                return corpus;

        }

        Assert.Fail($"no committed capture for protocol {protocol}.");
        return null!;
    }

    private static bool IsClientboundPlayFrame(RecordedFrame frame, int wireId) =>
        frame.WireId == wireId
        && CorpusEnumMapping.ToFlow(frame.Direction) == PacketFlow.Clientbound
        && CorpusEnumMapping.ToProtocolPhase(frame.Phase) == ProtocolPhase.Play;

    private static (int WireId, BoundPacketCodec Codec) Resolve(PhaseRegistry registry, Identifier id)
    {
        foreach ((int wireId, PacketType type) in registry.Packets)
            if (type.Id == id && registry.TryGetInbound(wireId, out BoundPacketCodec entry) && entry.IsImplemented)
                return (wireId, entry);

        Assert.Fail($"{id} is not bound in this phase.");
        return default;
    }
}
