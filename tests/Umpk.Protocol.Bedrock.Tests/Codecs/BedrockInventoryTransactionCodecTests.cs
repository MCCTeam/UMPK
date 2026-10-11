using Umpk.Protocol.Bedrock;
using Xunit;

namespace Umpk.Protocol.Bedrock.Tests.Codecs;

public sealed class BedrockInventoryTransactionCodecTests
{
    private static BedrockItemInstance AirItem() => new(0, 0, 0u, null, 0u, []);

    [Fact]
    public void NormalTransactionUsesExactWireForm()
    {
        var packet = new BedrockInventoryTransactionPacket(
            new BedrockTransactionLegacy(0, []),
            BedrockInventoryTransactionType.Normal,
            [],
            null);
        byte[] expected = Convert.FromHexString("0000010000");

        Assert.Equal(expected, BedrockInventoryTransactionCodecs.Encode(packet));
        Assert.Equal(packet, BedrockInventoryTransactionCodecs.Decode(expected));
    }

    [Fact]
    public void InventoryMismatchCarriesNoPayload()
    {
        var packet = new BedrockInventoryTransactionPacket(
            new BedrockTransactionLegacy(0, []),
            BedrockInventoryTransactionType.InventoryMismatch,
            [],
            null);

        Assert.Equal(packet, BedrockInventoryTransactionCodecs.Decode(BedrockInventoryTransactionCodecs.Encode(packet)));
    }

    [Fact]
    public void ItemUseRoundTripsWithExactValues()
    {
        var held = AirItem();
        var data = new BedrockItemUseTransactionData(0, 1, 10, 64, -20, 1, 0, held, new BedrockVector3(1.0f, 2.0f, 3.0f), new BedrockVector3(0.5f, 0.5f, 0.5f), 1u, 1, 0);
        var packet = new BedrockInventoryTransactionPacket(new BedrockTransactionLegacy(0, []), BedrockInventoryTransactionType.ItemUse, [], data);

        BedrockInventoryTransactionPacket decoded = BedrockInventoryTransactionCodecs.Decode(BedrockInventoryTransactionCodecs.Encode(packet));

        Assert.Equal(packet, decoded);
        var use = Assert.IsType<BedrockItemUseTransactionData>(decoded.Data);
        Assert.Equal(10, use.BlockX);
        Assert.Equal(1u, use.BlockRuntimeId);
    }

    // Candidate shape only: the server ignored these bytes across the full live matrix, so this pins a reusable wire form without proving acceptance.
    [Fact]
    public void PlacementCandidateTransactionUsesExactWireForm()
    {
        var held = new BedrockItemInstance(3, 64, 0u, 2, 2186211206u, []);
        var data = new BedrockItemUseTransactionData(0, 1, 8, 110, 8, 1, 0, held, new BedrockVector3(8.5f, 97.62f, 8.5f), new BedrockVector3(0.5f, 1.0f, 0.5f), 1460042000u, 1, 0);
        var packet = new BedrockInventoryTransactionPacket(new BedrockTransactionLegacy(0, []), BedrockInventoryTransactionType.ItemUse, [], data);

        byte[] expected = Convert.FromHexString("0000010200000110DC011001000300400000010486DFBB92080000000841713DC342000008410000003F0000803F0000003F90F299B8050100");
        byte[] encoded = BedrockInventoryTransactionCodecs.Encode(packet);

        Assert.Equal(expected, encoded);
        BedrockInventoryTransactionPacket decoded = BedrockInventoryTransactionCodecs.Decode(expected);

        Assert.Equal(packet, decoded);
        var use = Assert.IsType<BedrockItemUseTransactionData>(decoded.Data);
        Assert.Equal(0, use.ActionType);
        Assert.Equal(1, use.TriggerType);
        Assert.Equal((8, 110, 8), (use.BlockX, use.BlockY, use.BlockZ));
        Assert.Equal(1, use.Face);
        Assert.Equal(0, use.HotbarSlot);
        Assert.Equal(held, use.HeldItem);
        Assert.Equal(new BedrockVector3(8.5f, 97.62f, 8.5f), use.PlayerPos);
        Assert.Equal(new BedrockVector3(0.5f, 1.0f, 0.5f), use.ClickPos);
        Assert.Equal(1460042000u, use.BlockRuntimeId);
        Assert.Equal(1, use.ClientPrediction);
        Assert.Equal(0, use.ClientCooldownState);
        Assert.Equal(expected, BedrockInventoryTransactionCodecs.Encode(decoded));
    }

    [Fact]
    public void UnknownSourceTypeIsPreserved()
    {
        var action = new BedrockTransactionAction(77u, true, null, true, null, 5u, AirItem(), AirItem());
        var packet = new BedrockInventoryTransactionPacket(new BedrockTransactionLegacy(0, []), BedrockInventoryTransactionType.Normal, [action], null);

        Assert.Equal(packet, BedrockInventoryTransactionCodecs.Decode(BedrockInventoryTransactionCodecs.Encode(packet)));
    }

    [Fact]
    public void UnknownTransactionTypeIsRejected()
    {
        byte[] payload = Convert.FromHexString("00010900");

        Assert.Throws<BedrockCodecException>(() => BedrockInventoryTransactionCodecs.Decode(payload));
    }

    [Fact]
    public void NonFinitePositionsAreRejected()
    {
        var held = AirItem();
        var data = new BedrockItemUseTransactionData(0, 1, 0, 0, 0, 0, 0, held, new BedrockVector3(float.NaN, 0.0f, 0.0f), new BedrockVector3(0.0f, 0.0f, 0.0f), 0u, 0, 0);
        var packet = new BedrockInventoryTransactionPacket(new BedrockTransactionLegacy(0, []), BedrockInventoryTransactionType.ItemUse, [], data);

        Assert.Throws<BedrockCodecException>(() => BedrockInventoryTransactionCodecs.Encode(packet));
    }

    [Theory]
    [InlineData("")]
    [InlineData("00")]
    [InlineData("00000100000000")]
    public void TransactionRejectsTruncatedOrTrailingPayloads(string payloadHex)
    {
        byte[] payload = Convert.FromHexString(payloadHex);

        Assert.Throws<BedrockCodecException>(() => BedrockInventoryTransactionCodecs.Decode(payload));
    }

    [Fact]
    public void LegacyRequestRoundTripsWithEntries()
    {
        var entries = new[] { new BedrockTransactionLegacyEntry(0, [1, 2]), new BedrockTransactionLegacyEntry(5, []) };
        var packet = new BedrockInventoryTransactionPacket(new BedrockTransactionLegacy(7, entries), BedrockInventoryTransactionType.Normal, [], null);

        BedrockInventoryTransactionPacket decoded = BedrockInventoryTransactionCodecs.Decode(BedrockInventoryTransactionCodecs.Encode(packet));

        Assert.Equal(packet, decoded);
        Assert.Equal(7, decoded.Legacy.RequestId);
        Assert.Equal(2, decoded.Legacy.Entries.Length);
        Assert.Equal([1, 2], decoded.Legacy.Entries[0].ChangedSlots.ToArray());
    }

    [Fact]
    public void ActionWithWindowAndFlagsRoundTrips()
    {
        var action = new BedrockTransactionAction(0u, true, (sbyte)0, true, 3u, 5u, AirItem(), AirItem());
        var packet = new BedrockInventoryTransactionPacket(new BedrockTransactionLegacy(0, []), BedrockInventoryTransactionType.Normal, [action], null);

        BedrockInventoryTransactionPacket decoded = BedrockInventoryTransactionCodecs.Decode(BedrockInventoryTransactionCodecs.Encode(packet));

        Assert.Equal(packet, decoded);
        Assert.Equal((sbyte)0, decoded.Actions[0].WindowId);
        Assert.Equal((uint)3, decoded.Actions[0].Flags);
    }

    [Fact]
    public void ItemUseOnEntityRoundTripsWithExactValues()
    {
        var data = new BedrockItemUseOnEntityTransactionData(123u, 1, 2, AirItem(), new BedrockVector3(1.0f, 64.0f, -1.0f), new BedrockVector3(0.0f, 1.0f, 0.0f));
        var packet = new BedrockInventoryTransactionPacket(new BedrockTransactionLegacy(0, []), BedrockInventoryTransactionType.ItemUseOnEntity, [], data);

        BedrockInventoryTransactionPacket decoded = BedrockInventoryTransactionCodecs.Decode(BedrockInventoryTransactionCodecs.Encode(packet));

        Assert.Equal(packet, decoded);
        var entity = Assert.IsType<BedrockItemUseOnEntityTransactionData>(decoded.Data);
        Assert.Equal(123u, entity.EntityRuntimeId);
    }

    [Fact]
    public void ItemReleaseRoundTripsWithExactValues()
    {
        var data = new BedrockItemReleaseTransactionData(0, 4, AirItem(), new BedrockVector3(0.0f, 66.0f, 0.0f));
        var packet = new BedrockInventoryTransactionPacket(new BedrockTransactionLegacy(0, []), BedrockInventoryTransactionType.ItemRelease, [], data);

        Assert.Equal(packet, BedrockInventoryTransactionCodecs.Decode(BedrockInventoryTransactionCodecs.Encode(packet)));
    }

    [Fact]
    public void UnknownActionBytesArePreserved()
    {
        var held = AirItem();
        var data = new BedrockItemUseTransactionData(9, 77, -5, -64, 300, 250, -3, held, new BedrockVector3(0.0f, 0.0f, 0.0f), new BedrockVector3(0.0f, 0.0f, 0.0f), uint.MaxValue, 9, 7);
        var packet = new BedrockInventoryTransactionPacket(new BedrockTransactionLegacy(0, []), BedrockInventoryTransactionType.ItemUse, [], data);

        BedrockInventoryTransactionPacket decoded = BedrockInventoryTransactionCodecs.Decode(BedrockInventoryTransactionCodecs.Encode(packet));

        Assert.Equal(packet, decoded);
        var use = Assert.IsType<BedrockItemUseTransactionData>(decoded.Data);
        Assert.Equal(9, use.ActionType);
        Assert.Equal(77, use.TriggerType);
        Assert.Equal(uint.MaxValue, use.BlockRuntimeId);
    }

    [Fact]
    public void MismatchedPayloadIsRejected()
    {
        Assert.Throws<BedrockCodecException>(() => new BedrockInventoryTransactionPacket(
            new BedrockTransactionLegacy(0, []),
            BedrockInventoryTransactionType.Normal,
            [],
            new BedrockItemUseTransactionData(0, 1, 0, 0, 0, 0, 0, AirItem(), new BedrockVector3(0, 0, 0), new BedrockVector3(0, 0, 0), 0u, 0, 0)));
    }

    [Fact]
    public void TransactionRejectsInvalidBoolean()
    {
        byte[] payload = Convert.FromHexString("0002010000");

        Assert.Throws<BedrockCodecException>(() => BedrockInventoryTransactionCodecs.Decode(payload));
    }

    [Fact]
    public void TransactionRejectsOversizedActionCount()
    {
        byte[] payload = Convert.FromHexString("00000101800100");

        Assert.Throws<BedrockCodecException>(() => BedrockInventoryTransactionCodecs.Decode(payload));
    }

    // Candidate shape only: the server ignored these bytes, so this pins a reusable wire form without proving acceptance.
    [Fact]
    public void HotbarDropCandidateTransactionUsesExactWireForm()
    {
        var authoritative = new BedrockItemInstance(306, 5, 0u, 2, 0u, new byte[10]);
        var remainder = new BedrockItemInstance(306, 4, 0u, 2, 0u, new byte[10]);
        var dropped = new BedrockItemInstance(306, 1, 0u, null, 0u, new byte[10]);
        var packet = new BedrockInventoryTransactionPacket(
            new BedrockTransactionLegacy(0, []),
            BedrockInventoryTransactionType.Normal,
            [
                new BedrockTransactionAction(0u, true, (sbyte)0, true, null, 0u, authoritative, remainder),
                new BedrockTransactionAction(2u, true, null, true, 0u, 0u, AirItem(), dropped),
            ],
            null);

        byte[] expected = Convert.FromHexString("0000010001020001010001000032010500000104000A0000000000000000000032010400000104000A00000000000000000000020100010100000000000000000000320101000000000A00000000000000000000");
        byte[] encoded = BedrockInventoryTransactionCodecs.Encode(packet);

        Assert.Equal(expected, encoded);
        BedrockInventoryTransactionPacket decoded = BedrockInventoryTransactionCodecs.Decode(expected);
        Assert.Equal(packet, decoded);
        Assert.Equal(BedrockInventoryTransactionType.Normal, decoded.TransactionType);
        Assert.Equal(2, decoded.Actions.Length);
        Assert.Equal(0u, decoded.Actions[0].SourceType);
        Assert.Equal((sbyte)0, decoded.Actions[0].WindowId);
        Assert.Equal(306, decoded.Actions[0].OldItem.NetworkId);
        Assert.Equal(5, decoded.Actions[0].OldItem.Count);
        Assert.Equal(4, decoded.Actions[0].NewItem.Count);
        Assert.Equal(2u, decoded.Actions[1].SourceType);
        Assert.Equal(0u, decoded.Actions[1].Flags);
        Assert.Equal(1, decoded.Actions[1].NewItem.Count);
        Assert.Equal(expected, BedrockInventoryTransactionCodecs.Encode(decoded));
    }

    [Fact]
    public void LiveDiamondMoveDecodesWithExactValues()
    {
        byte[] payload = Convert.FromHexString("00000100010200010100010000000000000000000032010500000104000A00000000000000000000030100010001320105000000000A000000000000000000000000000000000000");

        BedrockInventoryTransactionPacket packet = BedrockInventoryTransactionCodecs.Decode(payload);

        Assert.Equal(BedrockInventoryTransactionType.Normal, packet.TransactionType);
        Assert.Equal(2, packet.Actions.Length);
        Assert.Equal(0u, packet.Actions[0].SourceType);
        Assert.True(packet.Actions[0].ContainerPresence);
        Assert.Equal((sbyte)0, packet.Actions[0].WindowId);
        Assert.Equal(306, packet.Actions[0].NewItem.NetworkId);
        Assert.Equal(5, packet.Actions[0].NewItem.Count);
        Assert.Equal(3u, packet.Actions[1].SourceType);
        Assert.Equal(306, packet.Actions[1].OldItem.NetworkId);
        Assert.Equal(payload, BedrockInventoryTransactionCodecs.Encode(packet));
    }
}
