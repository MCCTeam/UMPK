using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit.Corpus;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Client.Tests;

/// <summary>A recorded 1.8 <c>minecraft:map_chunk_bulk</c> frame, driven through the real applier chain, must leave the world holding the blocks it carried.</summary>
/// <remarks>
/// <para>A codec test cannot prove that decoded columns reach the world. This test asserts world content and published events, not only that decoding returned an object.</para>
/// <para>Terrain is only assertable at coordinates the recorded frame actually covers, so every assertion is anchored to the frame's own column list rather than to a hardcoded position.</para>
/// </remarks>
public sealed class MapChunkBulkApplierTests
{
    private const int Protocol1_8 = 47;
    private const int MapChunkBulkWireId = 0x26;

    private readonly ITestOutputHelper _output;

    public MapChunkBulkApplierTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task A_Recorded_Bulk_Frame_Puts_Its_Terrain_Into_The_World()
    {
        (JavaVersion version, ClientboundMapChunkBulkPacket packet) = await DecodeFirstBulkFrameAsync();

        var harness = new ApplierHarness(version);
        harness.State.Registries = JavaGameData.Registries(Protocol1_8);
        var loaded = new List<ChunkPos>();
        harness.Events.Subscribe<ChunkLoaded>(e => loaded.Add(e.Position));

        await harness.ApplyAsync(JoinPacket());
        await harness.ApplyAsync(packet);

        Umpk.Game.World.World world = harness.State.World;
        _output.WriteLine($"bulk frame: {packet.Columns.Count} columns, skyLight={packet.SkyLight}");

        // One announcement per column, at the columns the frame actually carried.
        Assert.Equal(packet.Columns.Count, loaded.Count);
        Assert.Equal(
            packet.Columns.Select(c => c.Position).OrderBy(p => p.X).ThenBy(p => p.Z).ToList(),
            loaded.OrderBy(p => p.X).ThenBy(p => p.Z).ToList());

        // Every column is installed and readable through the world, not merely present on the packet.
        foreach (Umpk.Game.World.ChunkColumn column in packet.Columns)
            Assert.NotNull(world.GetColumn(column.Position));

        // The blocks themselves. A vanilla overworld column has a solid floor at y=0 across all 256 of its cells, so this fails both for "no world" and for "world with the wrong bytes in it".
        long floorCells = 0;
        long nonAir = 0;
        foreach (Umpk.Game.World.ChunkColumn column in packet.Columns)
            for (int x = 0; x < 16; x++)
                for (int z = 0; z < 16; z++)
                {
                    int worldX = (column.Position.X << 4) + x;
                    int worldZ = (column.Position.Z << 4) + z;
                    if (world.GetBlockStateId(new BlockPos(worldX, 0, worldZ)) != 0)
                        floorCells++;

                    for (int y = 0; y < 64; y++)
                        if (world.GetBlockStateId(new BlockPos(worldX, y, worldZ)) != 0)
                            nonAir++;

                }

        _output.WriteLine($"world after apply: {world.LoadedColumns.Count} columns, {nonAir} non-air blocks below y=64.");
        Assert.Equal(packet.Columns.Count * 256, floorCells);

        // Below y=64 a vanilla overworld column is nearly solid stone. Requiring most of that volume to be non-air rules out a decode that produced structurally valid but empty sections.
        long volume = packet.Columns.Count * 256L * 64L;
        Assert.True(nonAir > volume / 2, $"only {nonAir} of {volume} blocks below y=64 were non-air.");
    }

    /// <summary>Before the world exists the applier must still OWN the frame. Falling through would log it as an unhandled packet and would be the same silence the unbound codec produced.</summary>
    [Fact]
    public async Task A_Bulk_Frame_Before_Join_Is_Owned_Rather_Than_Unhandled()
    {
        (JavaVersion version, ClientboundMapChunkBulkPacket packet) = await DecodeFirstBulkFrameAsync();
        var harness = new ApplierHarness(version);

        Assert.True(await harness.TryApplyAsync(packet), "a pre-join bulk frame fell through the whole applier chain.");
        Assert.False(harness.State.HasWorld);
    }

    private static ClientboundLoginPacket JoinPacket()
    {
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);
        return new ClientboundLoginPacket(
            PlayerId: 1, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 10, SimulationDistance: 10, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false, Legacy: null);
    }

    private static async Task<(JavaVersion Version, ClientboundMapChunkBulkPacket Packet)> DecodeFirstBulkFrameAsync()
    {
        string root = FindCorpusRoot() ?? throw new InvalidOperationException("corpus root not found.");
        Assert.True(JavaVersions.TryGetByProtocol(Protocol1_8, out JavaVersion? version));
        Assert.True(version!.Protocol.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry play));
        Assert.True(play.TryGetInbound(MapChunkBulkWireId, out BoundPacketCodec codec));
        Assert.True(codec.IsImplemented, "map_chunk_bulk resolved to a marker on protocol 47.");

        foreach (string path in CorpusLoader.DiscoverCaptures(root))
        {
            LoadedCorpus corpus = await CorpusLoader.LoadFileAsync(path);
            if (corpus.Protocol != Protocol1_8)
                continue;

            foreach (RecordedFrame frame in corpus.Frames)
                if (frame.Direction == CorpusDirection.Clientbound && frame.WireId == MapChunkBulkWireId)
                    return (version, Assert.IsType<ClientboundMapChunkBulkPacket>(
                        codec.Decode(frame.Body, PacketCodecContext.Registryless)));

        }

        throw new InvalidOperationException("no recorded 1.8 map_chunk_bulk frame in the corpora.");
    }

    private static string? FindCorpusRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "fixtures", "corpus");
            if (Directory.Exists(candidate))
                return candidate;

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }
}
