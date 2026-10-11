using Xunit;

namespace Umpk.Protocol.Bedrock.Tests.World;

public sealed class BedrockUpdateSubChunkBlocksCodecTests
{
    [Fact]
    public void RoundTripsStandardAndExtraLayers()
    {
        var packet = new BedrockUpdateSubChunkBlocksPacket(
            0,
            64,
            32,
            [
                new BedrockSubChunkBlockChange(6, 64, 38, 42u, 2u, -1L, BedrockSubChunkBlockTransition.None),
                new BedrockSubChunkBlockChange(-300, -64, 123456, 987654u, 11u, 0u, BedrockSubChunkBlockTransition.Create),
            ],
            [
                new BedrockSubChunkBlockChange(7, 65, 39, 7u, 0u, 123456789L, BedrockSubChunkBlockTransition.Destroy),
            ]);

        byte[] encoded = BedrockUpdateSubChunkBlocksCodecs.Encode(packet);
        BedrockUpdateSubChunkBlocksPacket decoded = BedrockUpdateSubChunkBlocksCodecs.Decode(encoded);

        Assert.Equal(packet, decoded);
        Assert.Equal(encoded, BedrockUpdateSubChunkBlocksCodecs.Encode(decoded));
    }

    [Fact]
    public void PreservesRawFlagsEntityBitsAndExtremeIds()
    {
        var packet = new BedrockUpdateSubChunkBlocksPacket(
            -16,
            -4,
            -33,
            [new BedrockSubChunkBlockChange(int.MinValue, int.MaxValue, 0, uint.MaxValue, uint.MaxValue, -1L, BedrockSubChunkBlockTransition.None)],
            []);

        BedrockUpdateSubChunkBlocksPacket decoded = BedrockUpdateSubChunkBlocksCodecs.Decode(BedrockUpdateSubChunkBlocksCodecs.Encode(packet));

        Assert.Equal(uint.MaxValue, decoded.Blocks[0].BlockRuntimeId);
        Assert.Equal(uint.MaxValue, decoded.Blocks[0].Flags);
        Assert.Equal(-1L, decoded.Blocks[0].SyncedEntityId);
        Assert.Equal(packet, decoded);
    }

    [Fact]
    public void LiveDiamondFillDecodesWithAssertedValuesAndRoundTrips()
    {
        byte[] payload = Convert.FromHexString("00800140090C80014C90F299B80502FFFFFFFFFFFFFFFFFF01000C80014E90F299B80502FFFFFFFFFFFFFFFFFF01000C80015090F299B80502FFFFFFFFFFFFFFFFFF01000E80014C90F299B80502FFFFFFFFFFFFFFFFFF01000E80014E90F299B80502FFFFFFFFFFFFFFFFFF01000E80015090F299B80502FFFFFFFFFFFFFFFFFF01001080014C90F299B80502FFFFFFFFFFFFFFFFFF01001080014E90F299B80502FFFFFFFFFFFFFFFFFF01001080015090F299B80502FFFFFFFFFFFFFFFFFF010000");

        BedrockUpdateSubChunkBlocksPacket decoded = BedrockUpdateSubChunkBlocksCodecs.Decode(payload);

        Assert.Equal(0, decoded.X);
        Assert.Equal(64, decoded.Y);
        Assert.Equal(32, decoded.Z);
        Assert.Equal(9, decoded.Blocks.Count);
        Assert.Empty(decoded.Extra);
        Assert.Equal([6, 6, 6, 7, 7, 7, 8, 8, 8], decoded.Blocks.Select(static change => change.X));
        Assert.All(decoded.Blocks, static change => Assert.Equal(64, change.Y));
        Assert.Equal([38, 39, 40, 38, 39, 40, 38, 39, 40], decoded.Blocks.Select(static change => change.Z));
        Assert.All(decoded.Blocks, static change => Assert.Equal(1460042000u, change.BlockRuntimeId));
        Assert.All(decoded.Blocks, static change => Assert.Equal(2u, change.Flags));
        Assert.All(decoded.Blocks, static change => Assert.Equal(-1L, change.SyncedEntityId));
        Assert.All(decoded.Blocks, static change => Assert.Equal(BedrockSubChunkBlockTransition.None, change.Transition));
        Assert.Equal(payload, BedrockUpdateSubChunkBlocksCodecs.Encode(decoded));
    }

    [Fact]
    public void RejectsUnknownTransitionOrdinal()
    {
        byte[] payload = [0x00, 0x00, 0x00, 0x01, 0x02, 0x04, 0x06, 0x04, 0x02, 0x05, 0x03, 0x00];

        Assert.Throws<BedrockCodecException>(() => BedrockUpdateSubChunkBlocksCodecs.Decode(payload));
        Assert.Throws<BedrockCodecException>(() => new BedrockSubChunkBlockChange(0, 0, 0, 1u, 2u, 3L, (BedrockSubChunkBlockTransition)3));
    }

    [Fact]
    public void RejectsOverBudgetEntryCounts()
    {
        byte[] blocksOverflow = [0x00, 0x00, 0x00, 0x81, 0x20];
        byte[] extraOverflow = [0x00, 0x00, 0x00, 0x00, 0x81, 0x20];

        Assert.Throws<BedrockCodecException>(() => BedrockUpdateSubChunkBlocksCodecs.Decode(blocksOverflow));
        Assert.Throws<BedrockCodecException>(() => BedrockUpdateSubChunkBlocksCodecs.Decode(extraOverflow));
    }

    [Fact]
    public void RejectsTruncatedPayload()
    {
        var packet = new BedrockUpdateSubChunkBlocksPacket(
            1,
            2,
            3,
            [new BedrockSubChunkBlockChange(4, 5, 6, 7u, 2u, -1L, BedrockSubChunkBlockTransition.None)],
            []);
        byte[] encoded = BedrockUpdateSubChunkBlocksCodecs.Encode(packet);

        Assert.Throws<BedrockCodecException>(() => BedrockUpdateSubChunkBlocksCodecs.Decode(encoded[..^1]));
    }

    [Fact]
    public void RejectsTrailingBytes()
    {
        var packet = new BedrockUpdateSubChunkBlocksPacket(
            1,
            2,
            3,
            [new BedrockSubChunkBlockChange(4, 5, 6, 7u, 2u, -1L, BedrockSubChunkBlockTransition.None)],
            []);
        byte[] encoded = BedrockUpdateSubChunkBlocksCodecs.Encode(packet);
        byte[] trailing = [.. encoded, 0x00];

        Assert.Throws<BedrockCodecException>(() => BedrockUpdateSubChunkBlocksCodecs.Decode(trailing));
    }
}
