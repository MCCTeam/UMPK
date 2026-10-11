using System.Collections.Immutable;
using Xunit;

namespace Umpk.Protocol.Bedrock.Tests.Codecs;

public sealed class BedrockAddItemEntityCodecTests
{
    [Fact]
    public void AddItemEntityUsesExactWireForm()
    {
        var packet = new BedrockAddItemEntityPacket(
            10L,
            3UL,
            new BedrockItemInstance(306, 5, 0U, 2, 0U, []),
            new BedrockVector3(1f, 2f, 3f),
            new BedrockVector3(0f, 1f, 0f),
            [new BedrockMetadataEntry(0U, 7U, 0, BedrockMetadataValue.FromLong(0L))],
            true);
        byte[] encoded = BedrockActorCodecs.Encode(packet);
        BedrockAddItemEntityPacket decoded = BedrockActorCodecs.DecodeAddItemEntity(encoded);

        Assert.Equal(packet, decoded);
        Assert.Equal(10L, decoded.UniqueEntityId);
        Assert.Equal(3UL, decoded.RuntimeEntityId);
        Assert.Equal(306, decoded.Item.NetworkId);
        Assert.Equal(5, decoded.Item.Count);
        Assert.Equal(2, decoded.Item.StackNetworkId);
        Assert.Equal(new BedrockVector3(1f, 2f, 3f), decoded.Position);
        Assert.Equal(new BedrockVector3(0f, 1f, 0f), decoded.Velocity);
        Assert.True(decoded.IsFromFishing);
        Assert.Equal(encoded, BedrockActorCodecs.Encode(decoded));
    }

    [Fact]
    public void AddItemEntityPreservesIdsBeyondInt32()
    {
        var packet = new BedrockAddItemEntityPacket(
            5_000_000_000L,
            0x1_0000_0005UL,
            new BedrockItemInstance(0, 0, 0U, null, 0U, []),
            new BedrockVector3(0f, 0f, 0f),
            new BedrockVector3(0f, 0f, 0f),
            [],
            false);
        byte[] encoded = BedrockActorCodecs.Encode(packet);
        BedrockAddItemEntityPacket decoded = BedrockActorCodecs.DecodeAddItemEntity(encoded);

        Assert.Equal(packet, decoded);
        Assert.Equal(5_000_000_000L, decoded.UniqueEntityId);
        Assert.Equal(0x1_0000_0005UL, decoded.RuntimeEntityId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("14")]
    [InlineData("1403")]
    public void AddItemEntityRejectsTruncatedPayloads(string payloadHex)
    {
        Assert.Throws<BedrockCodecException>(() => BedrockActorCodecs.DecodeAddItemEntity(Convert.FromHexString(payloadHex)));
    }

    [Fact]
    public void AddItemEntityRejectsTrailingBytes()
    {
        var packet = new BedrockAddItemEntityPacket(
            10L,
            3UL,
            new BedrockItemInstance(0, 0, 0U, null, 0U, []),
            new BedrockVector3(0f, 0f, 0f),
            new BedrockVector3(0f, 0f, 0f),
            [],
            false);
        byte[] encoded = BedrockActorCodecs.Encode(packet);
        byte[] trailing = [.. encoded, 0x00];

        Assert.Throws<BedrockCodecException>(() => BedrockActorCodecs.DecodeAddItemEntity(trailing));
    }

    [Fact]
    public void AddItemEntityRejectsNonStrictFishingFlag()
    {
        var packet = new BedrockAddItemEntityPacket(
            10L,
            3UL,
            new BedrockItemInstance(0, 0, 0U, null, 0U, []),
            new BedrockVector3(0f, 0f, 0f),
            new BedrockVector3(0f, 0f, 0f),
            [],
            false);
        byte[] encoded = BedrockActorCodecs.Encode(packet);
        encoded[^1] = 0x02;

        Assert.Throws<BedrockCodecException>(() => BedrockActorCodecs.DecodeAddItemEntity(encoded));
    }

    [Fact]
    public void AddItemEntityRejectsNonFiniteVectors()
    {
        var packet = new BedrockAddItemEntityPacket(
            10L,
            3UL,
            new BedrockItemInstance(0, 0, 0U, null, 0U, []),
            new BedrockVector3(0f, 0f, 0f),
            new BedrockVector3(0f, 0f, 0f),
            [],
            false);
        byte[] encoded = BedrockActorCodecs.Encode(packet);
        encoded[10] = 0x00;
        encoded[11] = 0x00;
        encoded[12] = 0xC0;
        encoded[13] = 0x7F;

        Assert.Throws<BedrockCodecException>(() => BedrockActorCodecs.DecodeAddItemEntity(encoded));
    }

    [Fact]
    public void AddItemEntityRejectsUnknownMetadataValueType()
    {
        var packet = new BedrockAddItemEntityPacket(
            10L,
            3UL,
            new BedrockItemInstance(0, 0, 0U, null, 0U, []),
            new BedrockVector3(0f, 0f, 0f),
            new BedrockVector3(0f, 0f, 0f),
            [],
            false);
        byte[] prefix = BedrockActorCodecs.Encode(packet);
        byte[] mutated = [.. prefix[..^2], 0x01, 0x09, 0x00];

        Assert.Throws<BedrockCodecException>(() => BedrockActorCodecs.DecodeAddItemEntity(mutated));
    }
}
