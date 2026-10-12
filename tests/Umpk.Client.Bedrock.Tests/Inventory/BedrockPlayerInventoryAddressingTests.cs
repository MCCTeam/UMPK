using Umpk.Protocol.Bedrock;
using Xunit;

namespace Umpk.Client.Bedrock.State.Inventory;

public sealed class BedrockPlayerInventoryAddressingTests
{
    [Theory]
    [InlineData(0, 8, 28, 28)]
    [InlineData(8, 9, 28, 29)]
    [InlineData(9, 8, 29, 28)]
    [InlineData(9, 35, 29, 29)]
    public void AllBuildersPreservePhysicalCoordinates(byte source, byte destination, byte sourceContainer, byte destinationContainer)
    {
        BedrockItemStackRequestPacket[] packets =
        [
            BedrockItemStackEgress.BuildTake(-3, source, destination, 3, 71, 82),
            BedrockItemStackEgress.BuildPlace(-3, source, destination, 3, 71, 82),
            BedrockItemStackEgress.BuildSwap(-3, source, destination, 71, 82),
        ];
        for (int index = 0; index < packets.Length; index++)
        {
            BedrockItemStackRequest request = Assert.Single(packets[index].Requests);
            Assert.Equal(-3, request.RequestId);
            BedrockItemStackAction action = Assert.Single(request.Actions);
            Assert.Equal((BedrockItemStackActionType)index, action.Type);
            Assert.Equal((byte)index, action.LegacyTypeId);
            (BedrockStackRequestSlot first, BedrockStackRequestSlot second) = action.Payload switch
            {
                BedrockTakePayload take => (take.Source, take.Destination),
                BedrockPlacePayload place => (place.Source, place.Destination),
                BedrockSwapPayload swap => (swap.Source, swap.Destination),
                _ => throw new InvalidOperationException("Unexpected payload."),
            };
            Assert.Equal(new BedrockStackRequestSlot(new(sourceContainer, null), source, 71), first);
            Assert.Equal(new BedrockStackRequestSlot(new(destinationContainer, null), destination, 82), second);
            if (action.Payload is BedrockTakePayload takePayload)
                Assert.Equal(3, takePayload.Count);
            if (action.Payload is BedrockPlacePayload placePayload)
                Assert.Equal(3, placePayload.Count);
        }
    }

    [Theory]
    [InlineData(36)]
    [InlineData(255)]
    public void OutOfPlayerSlotsCannotAcquireAnInventoryNamespace(byte slot)
    {
        Assert.Throws<InvalidOperationException>(() => BedrockItemStackEgress.ContainerForSlot(slot));
        Assert.Throws<InvalidOperationException>(() => BedrockItemStackEgress.BuildTake(-3, slot, 0, 1, 71, 0));
        Assert.Throws<InvalidOperationException>(() => BedrockItemStackEgress.BuildPlace(-3, 0, slot, 1, 71, 0));
        Assert.Throws<InvalidOperationException>(() => BedrockItemStackEgress.BuildSwap(-3, slot, 0, 71, 82));
    }

    [Theory]
    [InlineData(-1, 36, false)]
    [InlineData(8, 9, true)]
    [InlineData(9, 9, false)]
    [InlineData(35, 35, false)]
    [InlineData(35, 36, true)]
    [InlineData(36, 64, false)]
    public void PhysicalSlotRequiresActualCapacityAsWellAsPlayerBounds(int slot, int capacity, bool supported) =>
        Assert.Equal(supported, BedrockPlayerInventoryAddressing.IsSupportedPhysicalSlot(slot, capacity));
}
