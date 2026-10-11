using Umpk.Protocol.Bedrock;
using Xunit;

namespace Umpk.Protocol.Bedrock.Tests.Codecs;

public sealed class BedrockItemStackCodecTests
{
    private static BedrockStackRequestSlot InventorySlot(byte slot, int stackId) =>
        new(new BedrockFullContainerName(12, null), slot, stackId);

    private static BedrockStackRequestSlot CursorSlot(byte slot, int stackId) =>
        new(new BedrockFullContainerName(59, null), slot, stackId);

    private static BedrockItemStackAction Take(byte count, BedrockStackRequestSlot source, BedrockStackRequestSlot destination) =>
        new(BedrockItemStackActionType.Take, 0, new BedrockTakePayload(count, source, destination));

    [Fact]
    public void TakeRequestUsesExactWireForm()
    {
        var packet = new BedrockItemStackRequestPacket(
            [new BedrockItemStackRequest(1, [Take(5, InventorySlot(0, 7), CursorSlot(0, 0))], [], 0)]);

        byte[] expected = Convert.FromHexString("0102010000050C0000070000003B0000000000000000000000");
        byte[] encoded = BedrockItemStackCodecs.Encode(packet);

        Assert.Equal(expected, encoded);
        Assert.Equal(packet, BedrockItemStackCodecs.DecodeRequest(expected));
    }

    [Fact]
    public void DropRequestUsesExactWireForm()
    {
        var source = new BedrockStackRequestSlot(new BedrockFullContainerName(28, null), 0, 7);
        var packet = new BedrockItemStackRequestPacket(
            [new BedrockItemStackRequest(-1, [new BedrockItemStackAction(BedrockItemStackActionType.Drop, 3, new BedrockDropPayload(1, source, false))], [], 0)]);

        byte[] expected = Convert.FromHexString("0101010303011C000007000000000000000000");
        byte[] encoded = BedrockItemStackCodecs.Encode(packet);

        Assert.Equal(expected, encoded);
        BedrockItemStackRequestPacket decoded = BedrockItemStackCodecs.DecodeRequest(expected);
        Assert.Equal(packet, decoded);
        var drop = (BedrockDropPayload)decoded.Requests[0].Actions[0].Payload;
        Assert.Equal(1, drop.Count);
        Assert.Equal(0, drop.Source.Slot);
        Assert.Equal(7, drop.Source.StackId);
        Assert.False(drop.Randomly);
    }

    [Fact]
    public void SignedRequestIdRoundTrips()
    {
        var packet = new BedrockItemStackRequestPacket(
            [new BedrockItemStackRequest(-1, [Take(1, InventorySlot(2, 3), CursorSlot(0, 0))], [], 0)]);

        BedrockItemStackRequestPacket decoded = BedrockItemStackCodecs.DecodeRequest(BedrockItemStackCodecs.Encode(packet));

        Assert.Equal(packet, decoded);
        Assert.Equal(-1, decoded.Requests[0].RequestId);
    }

    [Fact]
    public void Fixed32StackIdPreservesNegativeValueByteExactly()
    {
        var slot = new BedrockStackRequestSlot(new BedrockFullContainerName(29, null), 4, -1);
        var packet = new BedrockItemStackRequestPacket(
            [new BedrockItemStackRequest(2, [Take(1, slot, CursorSlot(0, 0))], [], 3)]);

        byte[] encoded = BedrockItemStackCodecs.Encode(packet);
        BedrockItemStackRequestPacket decoded = BedrockItemStackCodecs.DecodeRequest(encoded);

        Assert.Equal(packet, decoded);
        Assert.Equal(-1, ((BedrockTakePayload)decoded.Requests[0].Actions[0].Payload).Source.StackId);
        Assert.Equal("FFFFFFFF", Convert.ToHexString(encoded, 9, 4));
    }

    [Fact]
    public void MineBlockPreservesFixed32NetworkId()
    {
        var action = new BedrockItemStackAction(
            BedrockItemStackActionType.MineBlock, 9, new BedrockMineBlockPayload(3, 100, -5));
        var packet = new BedrockItemStackRequestPacket([new BedrockItemStackRequest(4, [action], [], 0)]);

        Assert.Equal(packet, BedrockItemStackCodecs.DecodeRequest(BedrockItemStackCodecs.Encode(packet)));
    }

    [Fact]
    public void LabTableCombineAndNonImplementedCarryNoPayload()
    {
        var packet = new BedrockItemStackRequestPacket(
            [new BedrockItemStackRequest(
                5,
                [
                    new BedrockItemStackAction(BedrockItemStackActionType.LabTableCombine, 7, BedrockLabTableCombinePayload.Instance),
                    new BedrockItemStackAction(BedrockItemStackActionType.NonImplemented, 16, BedrockNonImplementedPayload.Instance),
                ],
                [],
                0)]);

        Assert.Equal(packet, BedrockItemStackCodecs.DecodeRequest(BedrockItemStackCodecs.Encode(packet)));
    }

    [Fact]
    public void CraftingAndLoomActionsRoundTrip()
    {
        var packet = new BedrockItemStackRequestPacket(
            [new BedrockItemStackRequest(
                6,
                [
                    new BedrockItemStackAction(BedrockItemStackActionType.CraftRecipe, 10, new BedrockCraftRecipePayload(45, 2)),
                    new BedrockItemStackAction(
                        BedrockItemStackActionType.CraftRecipeAuto,
                        11,
                        new BedrockCraftRecipeAutoPayload(46, 1, [new BedrockRecipeIngredient(BedrockRecipeIngredientKind.ItemTag, 3, null, null, null, null, "minecraft:planks")])),
                    new BedrockItemStackAction(BedrockItemStackActionType.CraftCreative, 12, new BedrockCraftCreativePayload(7, 1)),
                    new BedrockItemStackAction(BedrockItemStackActionType.Optional, 13, new BedrockOptionalPayload(8, -1)),
                    new BedrockItemStackAction(BedrockItemStackActionType.CraftGrindstoneRequest, 14, new BedrockCraftGrindstoneRequestPayload(9, 1, 2)),
                    new BedrockItemStackAction(BedrockItemStackActionType.CraftLoomRequest, 15, new BedrockCraftLoomRequestPayload("banner_pattern", 1)),
                    new BedrockItemStackAction(
                        BedrockItemStackActionType.ResultsDeprecated,
                        17,
                        new BedrockResultsDeprecatedPayload(
                            [new BedrockItemStackResultDescriptor(1, 1, "Stick", 0, 5, 1u, [])],
                            1)),
                ],
                ["Renamed"],
                3)]);

        Assert.Equal(packet, BedrockItemStackCodecs.DecodeRequest(BedrockItemStackCodecs.Encode(packet)));
    }

    /// <summary>Checks the creative request captured from a real Bedrock 1.26.45 client, including all three actions and the exact bytes sent on the wire.</summary>
    /// <remarks>The original 77-byte frame starts with packet header 93 01 and has SHA-256 55eb6b10ab1345029ee6ab5c84d80c6aaa2e626c225befae63f0917825a2bcb8; the payload below omits that header.</remarks>
    [Fact]
    public void GenuineCreativeOakPlanksRequestRoundTripsByteIdentically()
    {
        byte[] payload = Convert.FromHexString("0105030C0E00011113010101146D696E6563726166743A6F616B5F706C616E6B73000100B6B5AC94070A00000000000000000000010000403C0032FDFFFFFF3B00000000000000FFFFFFFF");

        BedrockItemStackRequestPacket decoded = BedrockItemStackCodecs.DecodeRequest(payload);

        Assert.Single(decoded.Requests);
        BedrockItemStackRequest request = decoded.Requests[0];
        Assert.Equal(-3, request.RequestId);
        Assert.Equal(3, request.Actions.Length);
        Assert.Empty(request.CustomNames);
        Assert.Equal(-1, request.Cause);

        Assert.Equal(BedrockItemStackActionType.CraftCreative, request.Actions[0].Type);
        Assert.Equal(14, request.Actions[0].LegacyTypeId);
        var creative = Assert.IsType<BedrockCraftCreativePayload>(request.Actions[0].Payload);
        Assert.Equal(0, creative.ItemId);
        Assert.Equal(1, creative.TimesCrafted);

        Assert.Equal(BedrockItemStackActionType.ResultsDeprecated, request.Actions[1].Type);
        Assert.Equal(19, request.Actions[1].LegacyTypeId);
        var results = Assert.IsType<BedrockResultsDeprecatedPayload>(request.Actions[1].Payload);
        Assert.Equal(1, results.TimesCrafted);
        Assert.Single(results.ResultItems);
        BedrockItemStackResultDescriptor descriptor = results.ResultItems[0];
        Assert.Equal(1, descriptor.Type);
        Assert.Equal(1, descriptor.LegacyType);
        Assert.Equal("minecraft:oak_planks", descriptor.Name);
        Assert.Equal(0, descriptor.Metadata);
        Assert.Equal(1, descriptor.Count);
        Assert.Equal(1921718966u, descriptor.BlockRuntimeId);
        Assert.Equal(new byte[10], descriptor.ExtraData);

        Assert.Equal(BedrockItemStackActionType.Take, request.Actions[2].Type);
        Assert.Equal(0, request.Actions[2].LegacyTypeId);
        var take = Assert.IsType<BedrockTakePayload>(request.Actions[2].Payload);
        Assert.Equal(64, take.Count);
        Assert.Equal(60, take.Source.Container.ContainerId);
        Assert.Null(take.Source.Container.DynamicContainerId);
        Assert.Equal(50, take.Source.Slot);
        Assert.Equal(-3, take.Source.StackId);
        Assert.Equal(59, take.Destination.Container.ContainerId);
        Assert.Null(take.Destination.Container.DynamicContainerId);
        Assert.Equal(0, take.Destination.Slot);
        Assert.Equal(0, take.Destination.StackId);

        Assert.Equal(payload, BedrockItemStackCodecs.Encode(decoded));
    }

    [Fact]
    public void FullContainerNamePreservesDynamicIdentity()
    {
        var slot = new BedrockStackRequestSlot(new BedrockFullContainerName(63, 7u), 1, 11);
        var packet = new BedrockItemStackRequestPacket(
            [new BedrockItemStackRequest(7, [Take(1, slot, CursorSlot(0, 0))], [], 0)]);

        BedrockItemStackRequestPacket decoded = BedrockItemStackCodecs.DecodeRequest(BedrockItemStackCodecs.Encode(packet));

        Assert.Equal(packet, decoded);
        Assert.Equal(7u, ((BedrockTakePayload)decoded.Requests[0].Actions[0].Payload).Source.Container.DynamicContainerId);
    }

    [Fact]
    public void SwapPlaceDropDestroyConsumeCreateBeaconRoundTrip()
    {
        BedrockStackRequestSlot source = InventorySlot(1, 21);
        BedrockStackRequestSlot destination = CursorSlot(0, 0);
        var packet = new BedrockItemStackRequestPacket(
            [new BedrockItemStackRequest(
                8,
                [
                    new BedrockItemStackAction(BedrockItemStackActionType.Swap, 2, new BedrockSwapPayload(source, destination)),
                    new BedrockItemStackAction(BedrockItemStackActionType.Place, 1, new BedrockPlacePayload(2, source, destination)),
                    new BedrockItemStackAction(BedrockItemStackActionType.Drop, 3, new BedrockDropPayload(1, source, false)),
                    new BedrockItemStackAction(BedrockItemStackActionType.Destroy, 4, new BedrockDestroyPayload(1, source)),
                    new BedrockItemStackAction(BedrockItemStackActionType.Consume, 5, new BedrockConsumePayload(3, source)),
                    new BedrockItemStackAction(BedrockItemStackActionType.Create, 6, new BedrockCreatePayload(9)),
                    new BedrockItemStackAction(BedrockItemStackActionType.BeaconPayment, 8, new BedrockBeaconPaymentPayload(1, 2)),
                ],
                [],
                0)]);

        Assert.Equal(packet, BedrockItemStackCodecs.DecodeRequest(BedrockItemStackCodecs.Encode(packet)));
    }

    [Theory]
    [InlineData("01")]
    [InlineData("0102")]
    [InlineData("0102010000050C0000070000003B000000000000000000000000")]
    [InlineData("0102010000050C0000070000003B00000000000000000000")]
    public void RequestRejectsTruncatedOrTrailingPayloads(string payloadHex)
    {
        byte[] payload = Convert.FromHexString(payloadHex);

        Assert.Throws<BedrockCodecException>(() => BedrockItemStackCodecs.DecodeRequest(payload));
    }

    [Fact]
    public void RequestRejectsUnknownActionType()
    {
        byte[] payload = Convert.FromHexString("0102011200050C0000070000003B000000000000000000000000");

        Assert.Throws<BedrockCodecException>(() => BedrockItemStackCodecs.DecodeRequest(payload));
    }

    [Fact]
    public void RequestRejectsOversizedActionCount()
    {
        byte[] payload = BuildRequestWithCount(5000);

        Assert.Throws<BedrockCodecException>(() => BedrockItemStackCodecs.DecodeRequest(payload));
    }

    [Fact]
    public void OkResponseUsesExactWireForm()
    {
        var slot = new BedrockItemStackResponseSlot(5, 5, 3, 9, string.Empty, string.Empty, 0);
        var container = new BedrockItemStackResponseContainer(new BedrockFullContainerName(12, null), [slot]);
        var packet = new BedrockItemStackResponsePacket([new BedrockItemStackResponse(0, 1, true, [container])]);

        byte[] expected = Convert.FromHexString("0100020101010C0001050503010112000000");
        byte[] encoded = BedrockItemStackCodecs.Encode(packet);

        Assert.Equal(expected, encoded);
        BedrockItemStackResponsePacket decoded = BedrockItemStackCodecs.DecodeResponse(expected);
        Assert.Equal(packet, decoded);
        Assert.True(decoded.Responses[0].IsOk);
        Assert.Equal(9, decoded.Responses[0].Containers[0].Slots[0].StackId);
    }

    [Fact]
    public void ErrorResponseCarriesNoContainers()
    {
        var packet = new BedrockItemStackResponsePacket([new BedrockItemStackResponse(1, 2, false, [])]);

        byte[] expected = Convert.FromHexString("0101040100");
        byte[] encoded = BedrockItemStackCodecs.Encode(packet);

        Assert.Equal(expected, encoded);
        BedrockItemStackResponsePacket decoded = BedrockItemStackCodecs.DecodeResponse(expected);
        Assert.Equal(packet, decoded);
        Assert.False(decoded.Responses[0].IsOk);
    }

    [Fact]
    public void ErrorResponseMayCarryEmptyPresentContainers()
    {
        var packet = new BedrockItemStackResponsePacket([new BedrockItemStackResponse(1, -5, true, [])]);

        Assert.Equal(packet, BedrockItemStackCodecs.DecodeResponse(BedrockItemStackCodecs.Encode(packet)));
    }

    [Fact]
    public void LiveAcceptedResponseDecodesWithCorrelatedNegativeId()
    {
        byte[] payload = Convert.FromHexString("0100010101011C0002000004010104000000010101010106000000");

        BedrockItemStackResponsePacket decoded = BedrockItemStackCodecs.DecodeResponse(payload);

        Assert.Single(decoded.Responses);
        Assert.True(decoded.Responses[0].IsOk);
        Assert.Equal(-1, decoded.Responses[0].RequestId);
        Assert.Single(decoded.Responses[0].Containers);
        Assert.Equal(28, decoded.Responses[0].Containers[0].SlotType.ContainerId);
        Assert.Equal(2, decoded.Responses[0].Containers[0].Slots.Length);
        Assert.Equal(4, decoded.Responses[0].Containers[0].Slots[0].Count);
        Assert.Equal(2, decoded.Responses[0].Containers[0].Slots[0].StackId);
        Assert.Equal(1, decoded.Responses[0].Containers[0].Slots[1].Count);
        Assert.Equal(3, decoded.Responses[0].Containers[0].Slots[1].StackId);
        Assert.Equal(payload, BedrockItemStackCodecs.Encode(decoded));
    }

    [Fact]
    public void LiveAcceptedRequestRoundTripsByteIdentically()
    {
        byte[] payload = Convert.FromHexString("0101010101011C0000020000001C0001000000000000000000");

        BedrockItemStackRequestPacket decoded = BedrockItemStackCodecs.DecodeRequest(payload);

        Assert.Single(decoded.Requests);
        Assert.Equal(-1, decoded.Requests[0].RequestId);
        Assert.Equal(payload, BedrockItemStackCodecs.Encode(decoded));
    }

    [Fact]
    public void SentinelRequestIdCannotBeEmitted()
    {
        Assert.Throws<BedrockCodecException>(() => new BedrockItemStackRequest(0, [], [], 0));
    }

    [Fact]
    public void NonZeroStatusDecodesAsHandledRejection()
    {
        byte[] payload = Convert.FromHexString("0132010100");

        BedrockItemStackResponsePacket decoded = BedrockItemStackCodecs.DecodeResponse(payload);

        Assert.Single(decoded.Responses);
        Assert.False(decoded.Responses[0].IsOk);
        Assert.Equal(50, decoded.Responses[0].Status);
        Assert.Equal(-1, decoded.Responses[0].RequestId);
        Assert.Empty(decoded.Responses[0].Containers);
        Assert.Equal(payload, BedrockItemStackCodecs.Encode(decoded));
    }

    [Theory]
    [InlineData("01")]
    [InlineData("01000201010C000105050301120000")]
    [InlineData("01000201010C0001050503011200000000")]
    [InlineData("0101040000")]
    public void ResponseRejectsTruncatedOrTrailingPayloads(string payloadHex)
    {
        byte[] payload = Convert.FromHexString(payloadHex);

        Assert.Throws<BedrockCodecException>(() => BedrockItemStackCodecs.DecodeResponse(payload));
    }

    [Fact]
    public void ResponseRejectsOversizedResponseCount()
    {
        byte[] payload = Convert.FromHexString("FFFFFFFFFF0F");

        Assert.Throws<BedrockCodecException>(() => BedrockItemStackCodecs.DecodeResponse(payload));
    }

    private static byte[] BuildRequestWithCount(int count)
    {
        var output = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        writer.WriteUInt32VarInt(checked((uint)count));
        return output.WrittenSpan.ToArray();
    }
}
