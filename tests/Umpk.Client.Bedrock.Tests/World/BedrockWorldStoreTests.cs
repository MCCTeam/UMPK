using Umpk.Protocol.Bedrock;
using Xunit;

namespace Umpk.Client.Bedrock.State.World;

public sealed class BedrockWorldStoreTests
{
    [Fact]
    public void AbsentColumnNeverAnswersAsAir()
    {
        var store = new BedrockWorldStore();

        BedrockBlockQuery query = store.GetBlock(0, 0, 0, 0);

        Assert.Equal(BedrockCellAvailability.Absent, query.Availability);
        Assert.Null(query.Cell);
        Assert.False(query.CollisionAvailable);
    }

    [Fact]
    public void BoundaryCoordinatesResolveToOwningColumn()
    {
        var store = new BedrockWorldStore();
        store.TryApplyLevelChunk(0, 0, 0, store.WorldGeneration, ChunkBody(-4, layers: 1, paletteValue: 11), 64);

        Assert.Equal(BedrockCellAvailability.Complete, store.GetBlock(0, -64, 0, 0).Availability);
        Assert.Equal(BedrockCellAvailability.Complete, store.GetBlock(15, -49, 15, 0).Availability);
        Assert.Equal(BedrockCellAvailability.Absent, store.GetBlock(16, -64, 0, 0).Availability);
        Assert.Equal(BedrockCellAvailability.Absent, store.GetBlock(0, -64, 16, 0).Availability);
    }

    [Fact]
    public void NegativeSectionYStoresAndQueries()
    {
        var store = new BedrockWorldStore();
        store.TryApplyLevelChunk(0, 0, 0, store.WorldGeneration, ChunkBody(-4, layers: 1, paletteValue: 21), 64);

        BedrockBlockQuery query = store.GetBlock(3, -60, 5, 0);

        Assert.Equal(BedrockCellAvailability.Complete, query.Availability);
        Assert.Equal(21, query.Cell!.Value.Primary.RawId);
        Assert.True(query.Cell.Value.Primary.IsRuntime);
        Assert.True(query.Cell.Value.Primary.IsUnknown);
    }

    [Fact]
    public void SectionYOutsideDimensionRangeIsRejected()
    {
        var store = new BedrockWorldStore();

        Assert.Throws<BedrockCodecException>(() =>
            store.TryApplyLevelChunk(0, 0, 0, store.WorldGeneration, ChunkBody(20, layers: 1, paletteValue: 1), 64));
        Assert.Equal(0, store.ColumnCount);
    }

    [Fact]
    public void MultipleDimensionsStayIsolated()
    {
        var store = new BedrockWorldStore();
        store.TryApplyLevelChunk(0, 0, 0, store.WorldGeneration, ChunkBody(-4, layers: 1, paletteValue: 31), 64);
        store.TryApplyLevelChunk(0, 0, 1, store.WorldGeneration, ChunkBody(0, layers: 1, paletteValue: 32), 64);

        Assert.Equal(31, store.GetBlock(0, -64, 0, 0).Cell!.Value.Primary.RawId);
        Assert.Equal(32, store.GetBlock(0, 0, 0, 1).Cell!.Value.Primary.RawId);
        Assert.Equal(BedrockCellAvailability.Absent, store.GetBlock(0, 0, 0, 2).Availability);
    }

    [Fact]
    public void UnknownDimensionIsRejected()
    {
        var store = new BedrockWorldStore();

        Assert.Throws<BedrockCodecException>(() =>
            store.TryApplyLevelChunk(0, 0, 99, store.WorldGeneration, ChunkBody(0, layers: 1, paletteValue: 1), 64));
        Assert.Equal(BedrockCellAvailability.Absent, store.GetBlock(0, 0, 0, 99).Availability);
    }

    [Fact]
    public void PartialColumnBecomesComplete()
    {
        var store = new BedrockWorldStore();
        store.TryApplyLevelChunk(0, 0, 1, store.WorldGeneration, ChunkBody(0, layers: 1, paletteValue: 1), 64);

        Assert.Equal(BedrockColumnAvailability.Partial, store.GetColumnAvailability(0, 0, 1));

        var full = new BedrockLevelChunkBody(Enumerable.Range(0, 8).Select(y => Section(y, layers: 1, paletteValue: 1)).ToArray());
        store.TryApplyLevelChunk(0, 0, 1, store.WorldGeneration, full, 512);

        Assert.Equal(BedrockColumnAvailability.Complete, store.GetColumnAvailability(0, 0, 1));
    }

    [Fact]
    public void SuccessAllAirReadsAsKnownAir()
    {
        var store = new BedrockWorldStore();
        store.NoteSubChunkRequest(7);
        var entry = new BedrockSubChunkEntry(
            new BedrockSubChunkOffset(0, 0, 0),
            BedrockSubChunkResult.SuccessAllAir,
            null,
            BedrockHeightMapDataType.NoData,
            null,
            BedrockHeightMapDataType.NoData,
            null,
            null);

        Assert.True(store.TryApplySubChunkEntries(new BedrockSubChunkPosition(0, 0, 0), 1, store.WorldGeneration, 7, [entry]));

        BedrockBlockQuery query = store.GetBlock(0, 0, 0, 1);
        Assert.Equal(BedrockCellAvailability.KnownAir, query.Availability);
        Assert.Null(query.Cell);
    }

    [Fact]
    public void MultipleLayersArePreservedIndependently()
    {
        var store = new BedrockWorldStore();
        var section = new BedrockChunkSection(0, [Storage(paletteValue: 41, isRuntime: true), Storage(paletteValue: 77, isRuntime: false)]);
        store.TryApplyLevelChunk(0, 0, 1, store.WorldGeneration, new BedrockLevelChunkBody([section]), 128);

        BedrockBlockQuery query = store.GetBlock(1, 2, 3, 1);

        Assert.Equal(BedrockCellAvailability.Complete, query.Availability);
        Assert.Equal(41, query.Cell!.Value.Primary.RawId);
        Assert.True(query.Cell.Value.Primary.IsRuntime);
        Assert.Equal(77, query.Cell.Value.Secondary!.Value.RawId);
        Assert.False(query.Cell.Value.Secondary.Value.IsRuntime);
    }

    [Fact]
    public void UnknownRuntimeValuesAreRetainedWithoutBecomingAir()
    {
        var store = new BedrockWorldStore();
        store.TryApplyLevelChunk(0, 0, 1, store.WorldGeneration, ChunkBody(0, layers: 1, paletteValue: 987654), 64);

        BedrockBlockQuery query = store.GetBlock(4, 5, 6, 1);

        Assert.Equal(BedrockCellAvailability.Complete, query.Availability);
        Assert.Equal(987654, query.Cell!.Value.Primary.RawId);
        Assert.True(query.Cell.Value.Primary.IsUnknown);
    }

    [Fact]
    public void ExtremeCustomIdsArePreservedExactly()
    {
        var store = new BedrockWorldStore();
        var section = new BedrockChunkSection(0, [Storage(paletteValue: int.MaxValue, isRuntime: true)]);
        store.TryApplyLevelChunk(0, 0, 1, store.WorldGeneration, new BedrockLevelChunkBody([section]), 64);

        Assert.Equal(int.MaxValue, store.GetBlock(0, 0, 0, 1).Cell!.Value.Primary.RawId);
    }

    [Fact]
    public void StaleWorldGenerationIsRejectedAtomically()
    {
        var store = new BedrockWorldStore();
        store.TryApplyLevelChunk(0, 0, 1, store.WorldGeneration, ChunkBody(0, layers: 1, paletteValue: 1), 64);
        int stale = store.WorldGeneration;
        store.InvalidateWorld();

        Assert.False(store.TryApplyLevelChunk(0, 0, 1, stale, ChunkBody(0, layers: 1, paletteValue: 2), 64));
        Assert.Equal(BedrockCellAvailability.Absent, store.GetBlock(0, 0, 0, 1).Availability);
        Assert.Equal(0, store.ColumnCount);
    }

    [Fact]
    public void StaleRequestGenerationIsRejectedAtomically()
    {
        var store = new BedrockWorldStore();
        store.NoteSubChunkRequest(9);
        store.NoteSubChunkRequest(10);
        var entry = new BedrockSubChunkEntry(
            new BedrockSubChunkOffset(0, 0, 0),
            BedrockSubChunkResult.Success,
            BedrockChunkPayloadCodecs.EncodeSection(Section(0, layers: 1, paletteValue: 5)),
            BedrockHeightMapDataType.NoData,
            null,
            BedrockHeightMapDataType.NoData,
            null,
            null);

        Assert.False(store.TryApplySubChunkEntries(new BedrockSubChunkPosition(0, 0, 0), 1, store.WorldGeneration, 9, [entry]));
        Assert.Equal(BedrockCellAvailability.Absent, store.GetBlock(0, 0, 0, 1).Availability);
    }

    [Fact]
    public void MalformedSubChunkPayloadLeavesTheStoreUnchanged()
    {
        var store = new BedrockWorldStore();
        store.NoteSubChunkRequest(3);
        var entry = new BedrockSubChunkEntry(
            new BedrockSubChunkOffset(0, 0, 0),
            BedrockSubChunkResult.Success,
            [0x09, 0x01],
            BedrockHeightMapDataType.NoData,
            null,
            BedrockHeightMapDataType.NoData,
            null,
            null);

        Assert.Throws<BedrockCodecException>(() =>
            store.TryApplySubChunkEntries(new BedrockSubChunkPosition(0, 0, 0), 1, store.WorldGeneration, 3, [entry]));
        Assert.Equal(0, store.ColumnCount);
    }

    [Fact]
    public void OversizedColumnRetentionIsRejected()
    {
        var store = new BedrockWorldStore();
        for (int index = 0; index < BedrockWorldStore.MaxColumns; index++)
            store.TryApplyLevelChunk(index, 0, 1, store.WorldGeneration, ChunkBody(0, layers: 1, paletteValue: 1), 64);

        Assert.Equal(BedrockWorldStore.MaxColumns, store.ColumnCount);
        Assert.Throws<InvalidOperationException>(() =>
            store.TryApplyLevelChunk(BedrockWorldStore.MaxColumns, 0, 1, store.WorldGeneration, ChunkBody(0, layers: 1, paletteValue: 1), 64));
        Assert.Equal(BedrockWorldStore.MaxColumns, store.ColumnCount);
    }

    [Fact]
    public void OversizedByteRetentionIsRejected()
    {
        var store = new BedrockWorldStore();

        Assert.Throws<InvalidOperationException>(() =>
            store.TryApplyLevelChunk(0, 0, 1, store.WorldGeneration, ChunkBody(0, layers: 1, paletteValue: 1), int.MaxValue));
        Assert.Equal(0, store.ColumnCount);
        Assert.Equal(0, store.RetainedBytes);
    }

    [Fact]
    public void OversizedBlockActorRetentionIsRejected()
    {
        var store = new BedrockWorldStore();
        for (int index = 0; index < BedrockWorldStore.MaxBlockActors; index++)
            Assert.True(store.TryRetainBlockActor(index, 0, 0, 1, store.WorldGeneration, [0x01]));

        Assert.Equal(BedrockWorldStore.MaxBlockActors, store.BlockActorCount);
        Assert.Throws<InvalidOperationException>(() =>
            store.TryRetainBlockActor(999999, 0, 0, 1, store.WorldGeneration, [0x01]));
    }

    [Fact]
    public void StaleBlockActorWritesAreRejected()
    {
        var store = new BedrockWorldStore();
        int stale = store.WorldGeneration;
        store.InvalidateWorld();

        Assert.False(store.TryRetainBlockActor(0, 0, 0, 1, stale, [0x01]));
        Assert.Equal(0, store.BlockActorCount);
    }

    [Fact]
    public void BurstAppliesStayBoundedUnderMixedGenerations()
    {
        var store = new BedrockWorldStore();
        store.NoteSubChunkRequest(1);
        int fresh = store.WorldGeneration;
        store.InvalidateWorld();
        fresh = store.WorldGeneration;
        store.NoteSubChunkRequest(2);

        for (int index = 0; index < 64; index++)
        {
            Assert.False(store.TryApplyLevelChunk(index, 0, 1, fresh - 1, ChunkBody(0, layers: 1, paletteValue: 1), 64));
            Assert.True(store.TryApplyLevelChunk(index, 0, 1, fresh, ChunkBody(0, layers: 1, paletteValue: 1), 64));
        }

        Assert.Equal(64, store.ColumnCount);
        Assert.True(store.SectionCount <= BedrockWorldStore.MaxSectionsTotal);
        Assert.True(store.RetainedBytes <= BedrockWorldStore.MaxRetainedBytes);
    }

    [Fact]
    public void YOutsideDimensionHeightIsAbsent()
    {
        var store = new BedrockWorldStore();
        store.TryApplyLevelChunk(0, 0, 0, store.WorldGeneration, ChunkBody(-4, layers: 1, paletteValue: 1), 64);

        Assert.Equal(BedrockCellAvailability.Absent, store.GetBlock(0, -65, 0, 0).Availability);
        Assert.Equal(BedrockCellAvailability.Absent, store.GetBlock(0, 320, 0, 0).Availability);
    }

    [Fact]
    public void RefreshingStoredSectionsDoesNotConsumeCapacity()
    {
        var store = new BedrockWorldStore();
        store.NoteSubChunkRequest(1);
        var entry = new BedrockSubChunkEntry(
            new BedrockSubChunkOffset(0, 0, 0),
            BedrockSubChunkResult.Success,
            BedrockChunkPayloadCodecs.EncodeSection(Section(0, layers: 1, paletteValue: 5)),
            BedrockHeightMapDataType.NoData,
            null,
            BedrockHeightMapDataType.NoData,
            null,
            null);

        Assert.True(store.TryApplySubChunkEntries(new BedrockSubChunkPosition(0, 0, 0), 1, store.WorldGeneration, 1, [entry]));
        Assert.True(store.TryApplySubChunkEntries(new BedrockSubChunkPosition(0, 0, 0), 1, store.WorldGeneration, 1, [entry, entry]));

        Assert.Equal(1, store.SectionCount);
        Assert.Equal(5, store.GetBlock(0, 0, 0, 1).Cell!.Value.Primary.RawId);
    }

    [Fact]
    public void SnapshotPreservesExactIdentities()
    {
        var store = new BedrockWorldStore();
        store.TryApplyLevelChunk(0, 0, 1, store.WorldGeneration, ChunkBody(0, layers: 1, paletteValue: 123), 64);
        store.TryRetainBlockActor(0, 0, 0, 1, store.WorldGeneration, [0x0A, 0x0B]);

        BedrockWorldSnapshot snapshot = store.CaptureSnapshot();

        Assert.Equal(store.WorldGeneration, snapshot.WorldGeneration);
        Assert.Equal(1, snapshot.ColumnCount);
        Assert.Equal(1, snapshot.SectionCount);
        Assert.Equal(1, snapshot.BlockActorCount);
        Assert.False(snapshot.CollisionAvailable);
        Assert.False(snapshot.NavigationAllowed);
        Assert.Equal(123, store.GetBlock(0, 0, 0, 1).Cell!.Value.Primary.RawId);
    }

    [Fact]
    public void CollisionQueriesStayClosed()
    {
        var store = new BedrockWorldStore();
        store.TryApplyLevelChunk(0, 0, 1, store.WorldGeneration, ChunkBody(0, layers: 1, paletteValue: 1), 64);

        Assert.False(store.GetBlock(0, 0, 0, 1).CollisionAvailable);
        Assert.False(store.CaptureSnapshot().CollisionAvailable);
    }

    [Fact]
    public void DuplicateLevelChunkSectionYIsRejected()
    {
        var store = new BedrockWorldStore();
        var body = new BedrockLevelChunkBody([Section(0, layers: 1, paletteValue: 1), Section(0, layers: 1, paletteValue: 2)]);

        Assert.Throws<BedrockCodecException>(() =>
            store.TryApplyLevelChunk(0, 0, 1, store.WorldGeneration, body, 128));
        Assert.Equal(0, store.ColumnCount);
    }

    [Fact]
    public void SuccessWithoutPayloadIsRejected()
    {
        var store = new BedrockWorldStore();
        store.NoteSubChunkRequest(3);
        var entry = new BedrockSubChunkEntry(
            new BedrockSubChunkOffset(0, 0, 0),
            BedrockSubChunkResult.Success,
            null,
            BedrockHeightMapDataType.NoData,
            null,
            BedrockHeightMapDataType.NoData,
            null,
            null);

        Assert.Throws<BedrockCodecException>(() =>
            store.TryApplySubChunkEntries(new BedrockSubChunkPosition(0, 0, 0), 1, store.WorldGeneration, 3, [entry]));
        Assert.Equal(0, store.ColumnCount);
    }

    [Fact]
    public void SubChunkPayloadSectionYMismatchIsRejected()
    {
        var store = new BedrockWorldStore();
        store.NoteSubChunkRequest(4);
        var entry = new BedrockSubChunkEntry(
            new BedrockSubChunkOffset(0, 1, 0),
            BedrockSubChunkResult.Success,
            BedrockChunkPayloadCodecs.EncodeSection(Section(0, layers: 1, paletteValue: 5)),
            BedrockHeightMapDataType.NoData,
            null,
            BedrockHeightMapDataType.NoData,
            null,
            null);

        Assert.Throws<BedrockCodecException>(() =>
            store.TryApplySubChunkEntries(new BedrockSubChunkPosition(0, 0, 0), 1, store.WorldGeneration, 4, [entry]));
        Assert.Equal(0, store.ColumnCount);
    }

    [Fact]
    public void DuplicateRefreshDoesNotGrowRetainedBytes()
    {
        var store = new BedrockWorldStore();
        store.NoteSubChunkRequest(1);
        var entry = new BedrockSubChunkEntry(
            new BedrockSubChunkOffset(0, 0, 0),
            BedrockSubChunkResult.Success,
            BedrockChunkPayloadCodecs.EncodeSection(Section(0, layers: 1, paletteValue: 5)),
            BedrockHeightMapDataType.NoData,
            null,
            BedrockHeightMapDataType.NoData,
            null,
            null);

        Assert.True(store.TryApplySubChunkEntries(new BedrockSubChunkPosition(0, 0, 0), 1, store.WorldGeneration, 1, [entry]));
        long retained = store.RetainedBytes;
        Assert.True(store.TryApplySubChunkEntries(new BedrockSubChunkPosition(0, 0, 0), 1, store.WorldGeneration, 1, [entry, entry]));

        Assert.Equal(1, store.SectionCount);
        Assert.Equal(retained, store.RetainedBytes);
    }

    [Fact]
    public void UpdateBlockAppliesToPresentLayer()
    {
        var store = new BedrockWorldStore();
        store.TryApplyLevelChunk(0, 0, 1, store.WorldGeneration, ChunkBody(0, layers: 1, paletteValue: 41), 64);

        Assert.True(store.TryApplyUpdateBlock(1, 2, 3, 1, store.WorldGeneration, 99u, 0u));

        BedrockBlockQuery query = store.GetBlock(1, 2, 3, 1);
        Assert.Equal(BedrockCellAvailability.Complete, query.Availability);
        Assert.Equal(99, query.Cell!.Value.Primary.RawId);
        Assert.True(query.Cell.Value.Primary.IsRuntime);
        Assert.True(query.Cell.Value.Primary.IsUnknown);
    }

    [Fact]
    public void UpdateBlockDoesNotFabricateMissingSection()
    {
        var store = new BedrockWorldStore();

        Assert.False(store.TryApplyUpdateBlock(0, 0, 0, 1, store.WorldGeneration, 99u, 0u));
        Assert.Equal(BedrockCellAvailability.Absent, store.GetBlock(0, 0, 0, 1).Availability);
        Assert.Equal(0, store.ColumnCount);
    }

    [Fact]
    public void UpdateBlockDoesNotFabricateMissingLayer()
    {
        var store = new BedrockWorldStore();
        store.TryApplyLevelChunk(0, 0, 1, store.WorldGeneration, ChunkBody(0, layers: 1, paletteValue: 41), 64);

        Assert.False(store.TryApplyUpdateBlock(1, 2, 3, 1, store.WorldGeneration, 99u, 1u));
        Assert.Equal(41, store.GetBlock(1, 2, 3, 1).Cell!.Value.Primary.RawId);
    }

    [Fact]
    public void UpdateBlockAppliesToSecondLayerWhenPresent()
    {
        var store = new BedrockWorldStore();
        var section = new BedrockChunkSection(0, [Storage(paletteValue: 41, isRuntime: true), Storage(paletteValue: 77, isRuntime: false)]);
        store.TryApplyLevelChunk(0, 0, 1, store.WorldGeneration, new BedrockLevelChunkBody([section]), 128);

        Assert.True(store.TryApplyUpdateBlock(1, 2, 3, 1, store.WorldGeneration, 88u, 1u));

        BedrockBlockQuery query = store.GetBlock(1, 2, 3, 1);
        Assert.Equal(41, query.Cell!.Value.Primary.RawId);
        Assert.Equal(88, query.Cell.Value.Secondary!.Value.RawId);
        Assert.True(query.Cell.Value.Secondary.Value.IsUnknown);
    }

    [Fact]
    public void UpdateBlockRejectsStaleWorldGeneration()
    {
        var store = new BedrockWorldStore();
        store.TryApplyLevelChunk(0, 0, 1, store.WorldGeneration, ChunkBody(0, layers: 1, paletteValue: 41), 64);
        int stale = store.WorldGeneration;
        store.InvalidateWorld();

        Assert.False(store.TryApplyUpdateBlock(1, 2, 3, 1, stale, 99u, 0u));
    }

    [Fact]
    public void SubChunkBlocksApplyToPresentLayers()
    {
        var store = new BedrockWorldStore();
        var section = new BedrockChunkSection(0, [Storage(paletteValue: 41, isRuntime: true), Storage(paletteValue: 77, isRuntime: false)]);
        store.TryApplyLevelChunk(0, 0, 1, store.WorldGeneration, new BedrockLevelChunkBody([section]), 128);
        var packet = new BedrockUpdateSubChunkBlocksPacket(
            0,
            0,
            0,
            [new BedrockSubChunkBlockChange(1, 2, 3, 99u, 2u, -1L, BedrockSubChunkBlockTransition.None)],
            [new BedrockSubChunkBlockChange(1, 2, 3, 88u, 0u, -1L, BedrockSubChunkBlockTransition.None)]);

        BedrockSubChunkBlocksApplyResult result = store.TryApplySubChunkBlocks(1, store.WorldGeneration, packet);

        Assert.Equal(2, result.Applied);
        Assert.Equal(0, result.Refused);
        BedrockBlockQuery query = store.GetBlock(1, 2, 3, 1);
        Assert.Equal(BedrockCellAvailability.Complete, query.Availability);
        Assert.Equal(99, query.Cell!.Value.Primary.RawId);
        Assert.True(query.Cell.Value.Primary.IsRuntime);
        Assert.True(query.Cell.Value.Primary.IsUnknown);
        Assert.Equal(88, query.Cell.Value.Secondary!.Value.RawId);
    }

    [Fact]
    public void SubChunkBlocksDoNotFabricateMissingSections()
    {
        var store = new BedrockWorldStore();
        var packet = new BedrockUpdateSubChunkBlocksPacket(
            0,
            0,
            0,
            [new BedrockSubChunkBlockChange(1, 2, 3, 99u, 2u, -1L, BedrockSubChunkBlockTransition.None)],
            [new BedrockSubChunkBlockChange(1, 2, 3, 88u, 0u, -1L, BedrockSubChunkBlockTransition.None)]);

        BedrockSubChunkBlocksApplyResult result = store.TryApplySubChunkBlocks(1, store.WorldGeneration, packet);

        Assert.Equal(0, result.Applied);
        Assert.Equal(2, result.Refused);
        Assert.Equal(BedrockCellAvailability.Absent, store.GetBlock(1, 2, 3, 1).Availability);
        Assert.Equal(0, store.ColumnCount);
    }

    [Fact]
    public void SubChunkBlocksDoNotFabricateMissingLayer()
    {
        var store = new BedrockWorldStore();
        store.TryApplyLevelChunk(0, 0, 1, store.WorldGeneration, ChunkBody(0, layers: 1, paletteValue: 41), 64);
        var packet = new BedrockUpdateSubChunkBlocksPacket(
            0,
            0,
            0,
            [new BedrockSubChunkBlockChange(1, 2, 3, 99u, 2u, -1L, BedrockSubChunkBlockTransition.None)],
            [new BedrockSubChunkBlockChange(1, 2, 3, 88u, 0u, -1L, BedrockSubChunkBlockTransition.None)]);

        BedrockSubChunkBlocksApplyResult result = store.TryApplySubChunkBlocks(1, store.WorldGeneration, packet);

        Assert.Equal(1, result.Applied);
        Assert.Equal(1, result.Refused);
        Assert.Equal(99, store.GetBlock(1, 2, 3, 1).Cell!.Value.Primary.RawId);
    }

    [Fact]
    public void SubChunkBlocksRejectStaleWorldGeneration()
    {
        var store = new BedrockWorldStore();
        store.TryApplyLevelChunk(0, 0, 1, store.WorldGeneration, ChunkBody(0, layers: 1, paletteValue: 41), 64);
        int stale = store.WorldGeneration;
        store.InvalidateWorld();
        var packet = new BedrockUpdateSubChunkBlocksPacket(
            0,
            0,
            0,
            [new BedrockSubChunkBlockChange(1, 2, 3, 99u, 2u, -1L, BedrockSubChunkBlockTransition.None)],
            []);

        BedrockSubChunkBlocksApplyResult result = store.TryApplySubChunkBlocks(1, stale, packet);

        Assert.Equal(0, result.Applied);
        Assert.Equal(1, result.Refused);
        Assert.Equal(BedrockCellAvailability.Absent, store.GetBlock(1, 2, 3, 1).Availability);
        Assert.Equal(0, store.ColumnCount);
    }

    [Fact]
    public void SubChunkPayloadWithTrailingActorAppliesSectionAndRetainsActor()
    {
        var store = new BedrockWorldStore();
        store.NoteSubChunkRequest(11);
        byte[] section = BedrockChunkPayloadCodecs.EncodeSection(Section(0, layers: 1, paletteValue: 5));
        byte[] actor = Convert.FromHexString("0A000312426C6F636B456E7469747956657273696F6E00080269640C53706F7265426C6F73736F6D030178530301791503017A3A00");
        byte[] payload = [.. section, .. actor];
        var entry = new BedrockSubChunkEntry(
            new BedrockSubChunkOffset(0, 0, 0),
            BedrockSubChunkResult.Success,
            payload,
            BedrockHeightMapDataType.NoData,
            null,
            BedrockHeightMapDataType.NoData,
            null,
            null);

        Assert.True(store.TryApplySubChunkEntries(new BedrockSubChunkPosition(0, 0, 0), 0, store.WorldGeneration, 11, [entry]));

        Assert.Equal(BedrockCellAvailability.Complete, store.GetBlock(0, 0, 0, 0).Availability);
        Assert.Equal(1, store.BlockActorCount);
    }

    [Fact]
    public void SubChunkPayloadWithTrailingGarbageIsRejected()
    {
        var store = new BedrockWorldStore();
        store.NoteSubChunkRequest(12);
        byte[] section = BedrockChunkPayloadCodecs.EncodeSection(Section(0, layers: 1, paletteValue: 5));
        byte[] payload = [.. section, 0x7F];
        var entry = new BedrockSubChunkEntry(
            new BedrockSubChunkOffset(0, 0, 0),
            BedrockSubChunkResult.Success,
            payload,
            BedrockHeightMapDataType.NoData,
            null,
            BedrockHeightMapDataType.NoData,
            null,
            null);

        Assert.Throws<BedrockCodecException>(() =>
            store.TryApplySubChunkEntries(new BedrockSubChunkPosition(0, 0, 0), 0, store.WorldGeneration, 12, [entry]));
        Assert.Equal(0, store.ColumnCount);
    }

    private static BedrockBlockStorage Storage(int paletteValue, bool isRuntime)
    {
        int[] palette = [paletteValue, paletteValue + 1];
        int[] cells = new int[BedrockBlockStorage.CellCount];
        return new BedrockBlockStorage(1, isRuntime, palette, cells);
    }

    private static BedrockChunkSection Section(int sectionY, int layers, int paletteValue)
    {
        var storages = new BedrockBlockStorage[layers];
        for (int index = 0; index < storages.Length; index++)
            storages[index] = Storage(paletteValue, isRuntime: true);
        return new BedrockChunkSection(sectionY, storages);
    }

    private static BedrockLevelChunkBody ChunkBody(int sectionY, int layers, int paletteValue) =>
        new([Section(sectionY, layers, paletteValue)]);
}
