using Xunit;

namespace Umpk.Protocol.Bedrock.Tests.World;

public sealed class BedrockSubChunkCodecTests
{
    [Fact]
    public void RequestRoundTripsWithOffsetsAndCenter()
    {
        var packet = new BedrockSubChunkRequestPacket(
            0,
            [new BedrockSubChunkOffset(1, -2, 3), new BedrockSubChunkOffset(-128, 0, 127)],
            new BedrockSubChunkPosition(16, 4, -32));
        byte[] expected = Convert.FromHexString("000201FE0380007F1000000004000000E0FFFFFF");

        Assert.Equal(expected, BedrockSubChunkCodecs.EncodeRequest(packet));
        Assert.Equal(packet, BedrockSubChunkCodecs.DecodeRequest(expected));
    }

    [Fact]
    public void ResponseRoundTripsWithMixedEntries()
    {
        byte[] heightmap = HeightMapPattern();
        var packet = new BedrockSubChunkResponsePacket(
            false,
            0,
            new BedrockSubChunkPosition(0, 0, 0),
            [
                new BedrockSubChunkEntry(
                    new BedrockSubChunkOffset(0, 0, 0),
                    BedrockSubChunkResult.Success,
                    [0xAA, 0xBB],
                    BedrockHeightMapDataType.NoData,
                    null,
                    BedrockHeightMapDataType.NoData,
                    null,
                    null),
                new BedrockSubChunkEntry(
                    new BedrockSubChunkOffset(1, 0, -1),
                    BedrockSubChunkResult.SuccessAllAir,
                    null,
                    BedrockHeightMapDataType.HasData,
                    heightmap,
                    BedrockHeightMapDataType.TooLow,
                    null,
                    0x0102030405060708UL),
            ]);

        byte[] encoded = BedrockSubChunkCodecs.EncodeResponse(packet);
        BedrockSubChunkResponsePacket decoded = BedrockSubChunkCodecs.DecodeResponse(encoded);

        Assert.Equal(packet, decoded);
        Assert.Equal(encoded, BedrockSubChunkCodecs.EncodeResponse(decoded));
    }

    [Fact]
    public void ResponseEntryKeepsOpaquePayload()
    {
        byte[] payload = Convert.FromHexString("0901000100AC02");
        byte[] bytes = EntryBytes("000000", "01", PayloadField(payload), "0000000000");

        BedrockSubChunkResponsePacket packet = DecodeSingleEntryResponse(bytes);

        BedrockSubChunkEntry entry = Assert.Single(packet.Entries);
        Assert.Equal(BedrockSubChunkResult.Success, entry.Result);
        Assert.Equal(payload, entry.Payload);
    }

    [Fact]
    public void UnknownResultOrdinalTerminatesDecode()
    {
        byte[] bytes = EntryBytes("000000", "07", "00", "000000");

        Assert.Throws<BedrockCodecException>(() => DecodeSingleEntryResponse(bytes));
    }

    [Fact]
    public void UnknownHeightMapOrdinalTerminatesDecode()
    {
        byte[] bytes = EntryBytes("000000", "01", "00", "050000");

        Assert.Throws<BedrockCodecException>(() => DecodeSingleEntryResponse(bytes));
    }

    [Fact]
    public void UnknownRenderHeightMapOrdinalTerminatesDecode()
    {
        byte[] bytes = EntryBytes("000000", "01", "00", "000005");

        Assert.Throws<BedrockCodecException>(() => DecodeSingleEntryResponse(bytes));
    }

    [Fact]
    public void TruncatedHeightMapTerminatesDecode()
    {
        byte[] bytes = EntryBytes("000000", "01", "00", "0101AA");

        Assert.Throws<BedrockCodecException>(() => DecodeSingleEntryResponse(bytes));
    }

    [Fact]
    public void OversizedPayloadTerminatesDecode()
    {
        byte[] bytes = EntryBytes("000000", "01", PayloadField(new byte[16]), "000000");
        var limits = new BedrockCodecLimits(MaxByteArrayBytes: 8);

        Assert.Throws<BedrockCodecException>(() => DecodeSingleEntryResponse(bytes, limits));
    }

    [Fact]
    public void ExcessEntriesTerminateDecode()
    {
        byte[] bytes = Convert.FromHexString("000000000000000000000000000002" + "00000001000000000000" + "00000001000000000000");
        var limits = new BedrockCodecLimits(MaxCollectionElements: 1);

        Assert.Throws<BedrockCodecException>(() => BedrockSubChunkCodecs.DecodeResponse(bytes, limits));
    }

    [Fact]
    public void TrailingBytesTerminateResponseDecode()
    {
        var packet = new BedrockSubChunkRequestPacket(1, [], new BedrockSubChunkPosition(0, 0, 0));
        byte[] encoded = BedrockSubChunkCodecs.EncodeRequest(packet);

        Assert.Throws<BedrockCodecException>(() => BedrockSubChunkCodecs.DecodeRequest([.. encoded, 0x00]));
    }

    [Fact]
    public void RequestOffsetOutsideInt8RangeIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BedrockSubChunkOffset(128, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BedrockSubChunkOffset(0, -129, 0));
    }

    [Fact]
    public void HeightMapWithWrongLengthIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new BedrockSubChunkEntry(
            new BedrockSubChunkOffset(0, 0, 0),
            BedrockSubChunkResult.Success,
            null,
            BedrockHeightMapDataType.HasData,
            [0x01],
            BedrockHeightMapDataType.NoData,
            null,
            null));
    }

    [Fact]
    public void LevelChunkAssemblySplitsSectionsInOrder()
    {
        byte[] first = BedrockChunkPayloadCodecs.EncodeSection(SingletonSection(-4, 11));
        byte[] second = BedrockChunkPayloadCodecs.EncodeSection(SingletonSection(3, 22));
        var packet = new LevelChunkPacket(1, -2, 0, 2, null, false, [], [.. first, .. second]);

        BedrockLevelChunkBody body = BedrockLevelChunkAssembly.Assemble(packet);

        Assert.Equal(2, body.Sections.Count);
        Assert.Equal(-4, body.Sections[0].SectionY);
        Assert.Equal(11, body.Sections[0].Layers[0].Get(0, 0, 0));
        Assert.Equal(3, body.Sections[1].SectionY);
        Assert.Equal(22, body.Sections[1].Layers[0].Get(15, 15, 15));
    }

    [Fact]
    public void LevelChunkAssemblyRejectsSectionCountMismatch()
    {
        byte[] section = BedrockChunkPayloadCodecs.EncodeSection(SingletonSection(0, 5));
        var packet = new LevelChunkPacket(0, 0, 0, 2, null, false, [], section);

        Assert.Throws<BedrockCodecException>(() => BedrockLevelChunkAssembly.Assemble(packet));
    }

    [Fact]
    public void LevelChunkAssemblyRejectsTrailingBytes()
    {
        byte[] section = BedrockChunkPayloadCodecs.EncodeSection(SingletonSection(0, 5));
        var packet = new LevelChunkPacket(0, 0, 0, 1, null, false, [], [.. section, 0x00]);

        Assert.Throws<BedrockCodecException>(() => BedrockLevelChunkAssembly.Assemble(packet));
    }

    [Fact]
    public void LevelChunkAssemblyRejectsCacheEnabledPayload()
    {
        byte[] section = BedrockChunkPayloadCodecs.EncodeSection(SingletonSection(0, 5));
        var packet = new LevelChunkPacket(0, 0, 0, 1, null, true, [], section);

        Assert.Throws<BedrockCodecException>(() => BedrockLevelChunkAssembly.Assemble(packet));
    }

    [Fact]
    public void LevelChunkAssemblyRejectsExcessiveSectionCount()
    {
        var packet = new LevelChunkPacket(0, 0, 0, 65, null, false, [], []);

        Assert.Throws<BedrockCodecException>(() => BedrockLevelChunkAssembly.Assemble(packet));
    }

    [Fact]
    public void LiveStructuredAllAirColumnDecodesSkySections()
    {
        var center = new BedrockSubChunkPosition(-4, 8, 1);
        var entries = new BedrockSubChunkEntry[24];
        for (int index = 0; index < entries.Length; index++)
            entries[index] = new BedrockSubChunkEntry(
                new BedrockSubChunkOffset(0, -12 + index, 0),
                BedrockSubChunkResult.SuccessAllAir,
                null,
                BedrockHeightMapDataType.NoData,
                null,
                BedrockHeightMapDataType.NoData,
                null,
                null);
        var packet = new BedrockSubChunkResponsePacket(false, 0, center, entries);

        byte[] encoded = BedrockSubChunkCodecs.EncodeResponse(packet);
        BedrockSubChunkResponsePacket decoded = BedrockSubChunkCodecs.DecodeResponse(encoded);

        Assert.Equal(packet, decoded);
        Assert.All(decoded.Entries, static entry => Assert.Equal(BedrockSubChunkResult.SuccessAllAir, entry.Result));
        Assert.Equal(encoded, BedrockSubChunkCodecs.EncodeResponse(decoded));
    }

    [Fact]
    public void CorrelatorMatchesEntriesAndReleasesPending()
    {
        var correlator = new BedrockSubChunkCorrelator();
        var request = new BedrockSubChunkRequestPacket(
            0,
            [new BedrockSubChunkOffset(0, 0, 0), new BedrockSubChunkOffset(1, 0, 0)],
            new BedrockSubChunkPosition(4, 2, 6));

        Assert.True(correlator.TryRegister(request, generation: 7));

        BedrockSubChunkMatchResult match = correlator.Match(SingleEntryResponse(0, new BedrockSubChunkPosition(4, 2, 6), new BedrockSubChunkOffset(0, 0, 0)), 7);

        Assert.False(match.Stale);
        Assert.Single(match.Matched);
        Assert.Empty(match.Unsolicited);

        BedrockSubChunkMatchResult second = correlator.Match(SingleEntryResponse(0, new BedrockSubChunkPosition(4, 2, 6), new BedrockSubChunkOffset(0, 0, 0)), 7);

        Assert.False(second.Stale);
        Assert.Empty(second.Matched);
        Assert.Single(second.Unsolicited);
    }

    [Fact]
    public void CorrelatorTreatsDimensionMismatchAsUnsolicited()
    {
        var correlator = new BedrockSubChunkCorrelator();
        var request = new BedrockSubChunkRequestPacket(0, [new BedrockSubChunkOffset(0, 0, 0)], new BedrockSubChunkPosition(0, 0, 0));
        Assert.True(correlator.TryRegister(request, generation: 1));

        BedrockSubChunkMatchResult match = correlator.Match(SingleEntryResponse(2, new BedrockSubChunkPosition(0, 0, 0), new BedrockSubChunkOffset(0, 0, 0)), 1);

        Assert.False(match.Stale);
        Assert.Empty(match.Matched);
        Assert.Single(match.Unsolicited);
    }

    [Fact]
    public void CorrelatorRejectsStaleGenerationWithoutMutation()
    {
        var correlator = new BedrockSubChunkCorrelator();
        var request = new BedrockSubChunkRequestPacket(0, [new BedrockSubChunkOffset(0, 0, 0)], new BedrockSubChunkPosition(0, 0, 0));
        Assert.True(correlator.TryRegister(request, generation: 3));
        correlator.Invalidate(generation: 4);

        BedrockSubChunkMatchResult stale = correlator.Match(SingleEntryResponse(0, new BedrockSubChunkPosition(0, 0, 0), new BedrockSubChunkOffset(0, 0, 0)), 3);

        Assert.True(stale.Stale);
        Assert.Empty(stale.Matched);
    }

    [Fact]
    public void CorrelatorRefusesRegistrationBeyondCapacity()
    {
        var correlator = new BedrockSubChunkCorrelator();
        var offsets = new List<BedrockSubChunkOffset>();
        for (int index = 0; index < BedrockSubChunkCorrelator.MaxPendingOffsets + 1; index++)
            offsets.Add(new BedrockSubChunkOffset((sbyte)(index % 5), 0, 0));
        var request = new BedrockSubChunkRequestPacket(0, offsets, new BedrockSubChunkPosition(0, 0, 0));

        Assert.False(correlator.TryRegister(request, generation: 1));
    }

    private static BedrockSubChunkResponsePacket SingleEntryResponse(int dimension, BedrockSubChunkPosition center, BedrockSubChunkOffset offset)
    {
        return new BedrockSubChunkResponsePacket(
            false,
            dimension,
            center,
            [new BedrockSubChunkEntry(offset, BedrockSubChunkResult.Success, null, BedrockHeightMapDataType.NoData, null, BedrockHeightMapDataType.NoData, null, null)]);
    }

    private static BedrockSubChunkResponsePacket DecodeSingleEntryResponse(byte[] bytes, BedrockCodecLimits? limits = null)
    {
        return BedrockSubChunkCodecs.DecodeResponse(bytes, limits);
    }

    private static byte[] EntryBytes(string offsetHex, string resultHex, string payloadHex, string tailHex)
    {
        return Convert.FromHexString("000000000000000000000000000001" + offsetHex + resultHex + payloadHex + tailHex);
    }

    private static string PayloadField(byte[] payload)
    {
        return "01" + Convert.ToHexString([(byte)payload.Length]) + Convert.ToHexString(payload);
    }

    private static byte[] HeightMapPattern()
    {
        var heightmap = new byte[BedrockSubChunkCodecs.HeightMapLength];
        for (int index = 0; index < heightmap.Length; index++)
            heightmap[index] = unchecked((byte)index);
        return heightmap;
    }

    private static BedrockChunkSection SingletonSection(int sectionY, int paletteValue)
    {
        var cells = new int[BedrockBlockStorage.CellCount];
        return new BedrockChunkSection(sectionY, [new BedrockBlockStorage(0, true, [paletteValue], cells)]);
    }
}
