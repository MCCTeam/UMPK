using Umpk.Nbt;
using Xunit;

namespace Umpk.Protocol.Bedrock.Tests.World;

public sealed class BedrockLevelChunkTrailerTests
{
    [Fact]
    public void LiveStructuredRepeatPayloadDecodesAndRoundTrips()
    {
        byte[] payload = [0x01, 0x2A, .. RepeatPrevious(23), 0x00];

        BedrockLevelChunkTrailer trailer = BedrockLevelChunkTrailerCodecs.DecodeTrailer(payload, 0, 24);

        Assert.Empty(trailer.BlockSections);
        Assert.Equal(24, trailer.BiomeSections.Count);
        foreach (BedrockBlockStorage biome in trailer.BiomeSections)
            Assert.Equal([21], biome.Palette);
        Assert.Same(trailer.BiomeSections[0], trailer.BiomeSections[23]);
        Assert.Empty(trailer.Border.Columns);
        Assert.Empty(trailer.Actors);
        Assert.Equal(payload, BedrockLevelChunkTrailerCodecs.EncodeTrailer(trailer));
    }

    [Fact]
    public void ExplicitBiomesWithBorderAndActorRoundTrip()
    {
        var actorState = new NbtCompound();
        actorState.Put("id", new NbtString("Chest"));
        actorState.Put("x", new NbtInt(1));
        actorState.Put("y", new NbtInt(2));
        actorState.Put("z", new NbtInt(3));
        byte[] actor = BedrockNbtCodec.Encode(actorState, BedrockNbtWireFormat.NetworkLittleVarInt);
        byte[] payload = [0x01, 0x00, 0x01, 0x2A, 0x02, 0x07, 0xC8, .. actor];

        BedrockLevelChunkTrailer trailer = BedrockLevelChunkTrailerCodecs.DecodeTrailer(payload, 0, 2);

        Assert.Equal(2, trailer.BiomeSections.Count);
        Assert.Equal([0], trailer.BiomeSections[0].Palette);
        Assert.Equal([21], trailer.BiomeSections[1].Palette);
        Assert.Equal([7, 200], trailer.Border.Columns);
        BedrockBlockActorData retained = Assert.Single(trailer.Actors);
        Assert.Equal(1, retained.X);
        Assert.Equal(2, retained.Y);
        Assert.Equal(3, retained.Z);
        Assert.Equal("Chest", retained.Identifier);
        Assert.Equal(actor, retained.Payload);
        Assert.Equal(payload, BedrockLevelChunkTrailerCodecs.EncodeTrailer(trailer));
    }

    [Fact]
    public void InlineBlockSectionDecodesWithExactConsumption()
    {
        var storage = new BedrockBlockStorage(0, true, [9], new int[BedrockBlockStorage.CellCount]);
        byte[] section = BedrockChunkPayloadCodecs.EncodeSection(new BedrockChunkSection(3, [storage]));
        byte[] payload = [.. section, 0x01, 0x00, 0x00];

        BedrockLevelChunkTrailer trailer = BedrockLevelChunkTrailerCodecs.DecodeTrailer(payload, 1, 1);

        Assert.Equal(3, Assert.Single(trailer.BlockSections).SectionY);
        Assert.Equal([0], Assert.Single(trailer.BiomeSections).Palette);
        Assert.Equal(payload, BedrockLevelChunkTrailerCodecs.EncodeTrailer(trailer));
    }

    [Fact]
    public void WideBiomeStorageDecodesAlongsideSingletons()
    {
        var wide = new BedrockBlockStorage(1, true, [4, 21], new int[BedrockBlockStorage.CellCount]);
        byte[] wideBytes = BedrockChunkPayloadCodecs.EncodeStorage(wide);
        byte[] payload = [0x01, 0x2A, .. wideBytes, 0x00];

        BedrockLevelChunkTrailer trailer = BedrockLevelChunkTrailerCodecs.DecodeTrailer(payload, 0, 2);

        Assert.Equal([21], trailer.BiomeSections[0].Palette);
        Assert.Equal([4, 21], trailer.BiomeSections[1].Palette);
        Assert.Equal(payload, BedrockLevelChunkTrailerCodecs.EncodeTrailer(trailer));
    }

    [Fact]
    public void LoneEndRootMeansNoActors()
    {
        byte[] payload = [0x01, 0x00, 0x00, 0x00];

        BedrockLevelChunkTrailer trailer = BedrockLevelChunkTrailerCodecs.DecodeTrailer(payload, 0, 1);

        Assert.Empty(trailer.Actors);
    }

    [Fact]
    public void RepeatMarkerWithoutPreviousStorageIsRejected()
    {
        Assert.Throws<BedrockCodecException>(() => BedrockLevelChunkTrailerCodecs.DecodeTrailer([0xFF, 0x00], 0, 1));
    }

    [Fact]
    public void TruncatedBiomeStorageIsRejected()
    {
        Assert.Throws<BedrockCodecException>(() => BedrockLevelChunkTrailerCodecs.DecodeTrailer([0x01], 0, 1));
    }

    [Fact]
    public void NegativeBiomeEntryIsRejected()
    {
        Assert.Throws<BedrockCodecException>(() => BedrockLevelChunkTrailerCodecs.DecodeTrailer([0x01, 0x01, 0x00], 0, 1));
    }

    [Fact]
    public void TruncatedBorderIsRejected()
    {
        Assert.Throws<BedrockCodecException>(() => BedrockLevelChunkTrailerCodecs.DecodeTrailer([0x01, 0x00, 0x02, 0x07], 0, 1));
    }

    [Fact]
    public void TrailingBytesAreRejected()
    {
        Assert.Throws<BedrockCodecException>(() => BedrockLevelChunkTrailerCodecs.DecodeTrailer([0x01, 0x00, 0x00, 0x00, 0x07], 0, 1));
    }

    [Fact]
    public void ActorWithoutCoordinatesIsRejected()
    {
        var state = new NbtCompound();
        state.Put("id", new NbtString("Chest"));
        byte[] payload = [0x01, 0x00, 0x00, .. BedrockNbtCodec.Encode(state, BedrockNbtWireFormat.NetworkLittleVarInt)];

        Assert.Throws<BedrockCodecException>(() => BedrockLevelChunkTrailerCodecs.DecodeTrailer(payload, 0, 1));
    }

    [Fact]
    public void ActorEndRootAfterEntriesIsRejected()
    {
        var state = new NbtCompound();
        state.Put("id", new NbtString("Chest"));
        state.Put("x", new NbtInt(1));
        state.Put("y", new NbtInt(2));
        state.Put("z", new NbtInt(3));
        byte[] payload = [0x01, 0x00, 0x00, .. BedrockNbtCodec.Encode(state, BedrockNbtWireFormat.NetworkLittleVarInt), 0x00];

        Assert.Throws<BedrockCodecException>(() => BedrockLevelChunkTrailerCodecs.DecodeTrailer(payload, 0, 1));
    }

    [Fact]
    public void NonCompoundActorEntryIsRejected()
    {
        byte[] payload = [0x01, 0x00, 0x00, 0x01, 0x00, 0x2A];

        Assert.Throws<BedrockCodecException>(() => BedrockLevelChunkTrailerCodecs.DecodeTrailer(payload, 0, 1));
    }

    private static byte[] RepeatPrevious(int count)
    {
        var markers = new byte[count];
        Array.Fill(markers, (byte)0xFF);
        return markers;
    }
}
