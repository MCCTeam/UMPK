using Umpk.Protocol.Bedrock;
using Xunit;

namespace Umpk.Client.Bedrock.State.World;

public sealed class BedrockWorldStoreTrailerTests
{
    [Fact]
    public void InlineSectionsCommitAndAnswerQueries()
    {
        var store = new BedrockWorldStore();
        BedrockLevelChunkTrailer trailer = Trailer(
            [Section(0, [5])],
            [Biome(21)],
            [],
            []);

        Assert.True(store.TryApplyTrailer(0, 0, 0, store.WorldGeneration, trailer, 128));

        BedrockBlockQuery query = store.GetBlock(3, 7, 9, 0);
        Assert.Equal(BedrockCellAvailability.Complete, query.Availability);
        Assert.NotNull(query.Cell);
        Assert.Equal(new BedrockBlockStateIdentity(true, 5, false, true), query.Cell.Value.Primary);
        Assert.Null(query.Cell.Value.Secondary);
        Assert.Equal(BedrockColumnAvailability.Partial, store.GetColumnAvailability(0, 0, 0));
    }

    [Fact]
    public void BiomeOnlyTrailerCreatesNoColumnAndNeverReadsAsAir()
    {
        var store = new BedrockWorldStore();
        BedrockLevelChunkTrailer trailer = Trailer([], [Biome(21)], [], []);

        Assert.True(store.TryApplyTrailer(0, 0, 0, store.WorldGeneration, trailer, 26));

        Assert.Equal(BedrockColumnAvailability.Absent, store.GetColumnAvailability(0, 0, 0));
        Assert.Equal(BedrockCellAvailability.Absent, store.GetBlock(0, 70, 0, 0).Availability);
        Assert.Equal(0, store.ColumnCount);
        Assert.Equal(0, store.SectionCount);
    }

    [Fact]
    public void ActorsRetainWithCoordinatesAndSnapshotCounts()
    {
        var store = new BedrockWorldStore();
        BedrockLevelChunkTrailer trailer = Trailer([], [Biome(0)], [], [new BedrockBlockActorData(1, 70, 2, "Chest", [0x0A, 0x00, 0x00])]);

        Assert.True(store.TryApplyTrailer(0, 0, 0, store.WorldGeneration, trailer, 32));

        Assert.Equal(1, store.BlockActorCount);
        Assert.Equal(1, store.CaptureSnapshot().BlockActorCount);
        Assert.Equal(BedrockCellAvailability.Absent, store.GetBlock(1, 70, 2, 0).Availability);
    }

    [Fact]
    public void NegativeSectionYSurvivesBelowZero()
    {
        var store = new BedrockWorldStore();
        BedrockLevelChunkTrailer trailer = Trailer(
            [Section(-4, [9])],
            [Biome(3)],
            [],
            []);

        Assert.True(store.TryApplyTrailer(-2, -3, 0, store.WorldGeneration, trailer, 64));

        BedrockBlockQuery query = store.GetBlock(-2 * 16 + 1, -64, -3 * 16 + 2, 0);
        Assert.Equal(BedrockCellAvailability.Complete, query.Availability);
        Assert.Equal(9, query.Cell!.Value.Primary.RawId);
    }

    [Fact]
    public void NetherTrailerAppliesWithEightBiomes()
    {
        var store = new BedrockWorldStore();
        var biomes = new List<BedrockBlockStorage> { Biome(4) };
        for (int index = 1; index < 8; index++)
            biomes.Add(biomes[0]);
        BedrockLevelChunkTrailer trailer = Trailer([Section(0, [11])], biomes, [], []);

        Assert.True(store.TryApplyTrailer(5, 6, 1, store.WorldGeneration, trailer, 96));

        Assert.Equal(11, store.GetBlock(5 * 16, 3, 6 * 16, 1).Cell!.Value.Primary.RawId);
    }

    [Fact]
    public void UnknownDimensionIsRejected()
    {
        var store = new BedrockWorldStore();
        BedrockLevelChunkTrailer trailer = Trailer([], [Biome(0)], [], []);

        Assert.Throws<BedrockCodecException>(() => store.TryApplyTrailer(0, 0, 999, store.WorldGeneration, trailer, 8));
    }

    [Fact]
    public void StaleWorldGenerationIsRefused()
    {
        var store = new BedrockWorldStore();
        int stale = store.WorldGeneration;
        store.InvalidateWorld();
        BedrockLevelChunkTrailer trailer = Trailer([Section(0, [1])], [Biome(0)], [], [new BedrockBlockActorData(0, 5, 0, null, [0x01])]);

        Assert.False(store.TryApplyTrailer(0, 0, 0, stale, trailer, 64));
        Assert.Equal(0, store.SectionCount);
        Assert.Equal(0, store.BlockActorCount);
    }

    [Fact]
    public void DuplicateSectionYIsRejected()
    {
        var store = new BedrockWorldStore();
        BedrockLevelChunkTrailer trailer = Trailer([Section(1, [1]), Section(1, [2])], [Biome(0)], [], []);

        Assert.Throws<BedrockCodecException>(() => store.TryApplyTrailer(0, 0, 0, store.WorldGeneration, trailer, 64));
    }

    [Fact]
    public void ActorOutsideHeightIsRejected()
    {
        var store = new BedrockWorldStore();
        BedrockLevelChunkTrailer trailer = Trailer([], [Biome(0)], [], [new BedrockBlockActorData(0, 400, 0, null, [0x01])]);

        Assert.Throws<BedrockCodecException>(() => store.TryApplyTrailer(0, 0, 0, store.WorldGeneration, trailer, 32));
    }

    private static BedrockLevelChunkTrailer Trailer(
        IReadOnlyList<BedrockChunkSection> sections,
        IReadOnlyList<BedrockBlockStorage> biomes,
        byte[] border,
        IReadOnlyList<BedrockBlockActorData> actors) =>
        new(sections, biomes, new BedrockBorderBlockData(border), actors);

    private static BedrockChunkSection Section(int sectionY, int[] palette)
    {
        var cells = new int[BedrockBlockStorage.CellCount];
        return new BedrockChunkSection(sectionY, [new BedrockBlockStorage(1, true, palette, cells)]);
    }

    private static BedrockBlockStorage Biome(int entry) =>
        new(0, true, [entry], new int[BedrockBlockStorage.CellCount]);
}
