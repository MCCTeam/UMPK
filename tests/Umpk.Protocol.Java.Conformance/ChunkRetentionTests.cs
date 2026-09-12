using System.Reflection;
using System.Runtime.CompilerServices;
using Umpk.Data.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Umpk.TestKit.Corpus;
using Xunit;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>A decoded chunk packet must not keep its structural model alive. For a 24-section column that model is roughly 168 objects and 48 KB of <c>long[]</c>, and a delivered packet sits in a bounded inbound channel until the consumer drains it, so retaining it multiplies the resident cost of every column by the channel depth. The model's only consumer rebuilds it from the retained bytes on demand.</summary>
public sealed class ChunkRetentionTests
{
    /// <summary>The reachability question, asked of the type rather than of one instance: no property of either chunk packet may hold a structural body, because any such property retains the whole graph for as long as the packet lives.</summary>
    [Theory]
    [InlineData(typeof(ClientboundLevelChunkPacket))]
    [InlineData(typeof(ClientboundMapChunkBulkPacket))]
    public void NoChunkPacketProperty_HoldsAStructuralBody(Type packet)
    {
        PropertyInfo[] holders = [.. packet
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(static p => typeof(ChunkWireBody).IsAssignableFrom(p.PropertyType))];

        Assert.Empty(holders);
    }

    /// <summary>The same question asked of a real frame: rebuild the model, drop the only strong reference, and it goes away while the packet it came from is still alive.</summary>
    [Fact]
    public async Task ARebuiltModel_DiesWhileItsPacketLives()
    {
        ClientboundLevelChunkPacket packet = await FirstModernChunkAsync();

        WeakReference model = BuildAndForget(packet);
        GC.Collect(0, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();

        Assert.False(model.IsAlive, "the decoded packet still reaches a structural chunk body.");
        Assert.NotNull(packet.RawBody);
    }

    // Separated so the rebuilt body has no live local slot left in the caller's frame when the collect runs.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference BuildAndForget(ClientboundLevelChunkPacket packet) =>
        new(ChunkCodecs.DecodeStructure(packet));

    private static async Task<ClientboundLevelChunkPacket> FirstModernChunkAsync()
    {
        Identifier levelChunk = PlayPackets.Clientbound.LevelChunk.Id;
        foreach (string path in CorpusLoader.DiscoverCaptures(FixturePaths.CorpusRoot))
        {
            LoadedCorpus corpus = await CorpusLoader.LoadFileAsync(path);
            if (corpus.Protocol != 770)
                continue;

            Assert.True(JavaVersions.TryGetByProtocol(corpus.Protocol, out JavaVersion? version));
            Assert.True(version!.Protocol.TryGetRegistry(
                ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry play));

            foreach ((int wireId, PacketType type) in play.Packets)
            {
                if (type.Id != levelChunk || !play.TryGetInbound(wireId, out BoundPacketCodec codec))
                    continue;

                foreach (RecordedFrame frame in corpus.Frames)
                    if (frame.WireId == wireId
                        && CorpusEnumMapping.ToFlow(frame.Direction) == PacketFlow.Clientbound
                        && CorpusEnumMapping.ToProtocolPhase(frame.Phase) == ProtocolPhase.Play)
                        return (ClientboundLevelChunkPacket)codec.Decode(frame.Body, PacketCodecContext.Registryless);

            }
        }

        Assert.Fail("no committed 1.21.5 capture carries a level chunk frame.");
        return null!;
    }
}
