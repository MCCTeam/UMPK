using Umpk.Protocol.Bedrock;
using Xunit;

namespace Umpk.Protocol.Bedrock.Tests.Codecs;

public sealed class BedrockInventoryCodecTests
{
    private static BedrockItemInstance AirItem() => new(0, 0, 0u, null, 0u, []);

    private static BedrockItemInstance StoneItem(ushort count) => new(1, count, 0u, null, 1u, []);

    [Fact]
    public void InventoryContentUsesExactWireForm()
    {
        var container = new BedrockContainerAddress(0, 12, null);
        var items = new[] { StoneItem(5), AirItem() };
        var packet = new BedrockInventoryContentPacket(0, items, container, AirItem());
        byte[] expected = Convert.FromHexString("0002010005000000010000000000000000000c000000000000000000");

        Assert.Equal(expected, BedrockInventoryCodecs.Encode(packet));
        Assert.Equal(packet, BedrockInventoryCodecs.DecodeContent(expected));
    }

    [Fact]
    public void InventorySlotUsesPrimarySchemaWireForm()
    {
        var packet = new BedrockInventorySlotPacket(0, 5, null, null, StoneItem(5));
        byte[] expected = Convert.FromHexString("000500000100050000000100");

        Assert.Equal(expected, BedrockInventoryCodecs.Encode(packet));
        Assert.Equal(packet, BedrockInventoryCodecs.DecodeSlot(expected));
    }

    [Fact]
    public void InventorySlotPreservesOptionalContainerAddress()
    {
        var container = new BedrockContainerAddress(0, 29, null);
        var packet = new BedrockInventorySlotPacket(0, 1, container, null, AirItem());
        byte[] expected = Convert.FromHexString("0001011d00000000000000000000");

        Assert.Equal(expected, BedrockInventoryCodecs.Encode(packet));
        Assert.Equal(packet, BedrockInventoryCodecs.DecodeSlot(expected));
    }

    [Fact]
    public void ContainerAddressPreservesDynamicIdentity()
    {
        var address = new BedrockContainerAddress(0, 63, 7u);
        var packet = new BedrockInventorySlotPacket(0, 0, address, null, AirItem());

        Assert.Equal(packet, BedrockInventoryCodecs.DecodeSlot(BedrockInventoryCodecs.Encode(packet)));
        Assert.Equal(63, packet.Container!.Value.ContainerId);
        Assert.Equal(7u, packet.Container!.Value.DynamicContainerId);
    }

    [Fact]
    public void NegativeWindowIdRoundTrips()
    {
        var container = new BedrockContainerAddress(-1, 12, null);
        var packet = new BedrockInventoryContentPacket(-1, [], container, AirItem());

        Assert.Equal(packet, BedrockInventoryCodecs.DecodeContent(BedrockInventoryCodecs.Encode(packet)));
    }

    [Theory]
    [InlineData("00020100050000000100")]
    [InlineData("0002010005000000010000000000000000000c00000000000000000000")]
    [InlineData("0005000001000500000001")]
    [InlineData("00050000010005000000010000")]
    [InlineData("")]
    public void InventoryPacketsRejectTruncatedOrTrailingPayloads(string payloadHex)
    {
        byte[] payload = Convert.FromHexString(payloadHex);
        Assert.Throws<BedrockCodecException>(() => BedrockInventoryCodecs.DecodeContent(payload));
        Assert.Throws<BedrockCodecException>(() => BedrockInventoryCodecs.DecodeSlot(payload));
    }

    [Fact]
    public void InventoryContentRejectsOversizedItemCount()
    {
        byte[] payload = new BedrockWriterForTest().WriteContentWithCount(5000);

        Assert.Throws<BedrockCodecException>(() => BedrockInventoryCodecs.DecodeContent(payload));
    }

    [Fact]
    public void ContainerOpenUsesExactWireForm()
    {
        var packet = new BedrockContainerOpenPacket(1, 0, 10, 64, -20, -1L);
        byte[] expected = Convert.FromHexString("01001480012701");

        Assert.Equal(expected, BedrockInventoryCodecs.Encode(packet));
        BedrockContainerOpenPacket decoded = BedrockInventoryCodecs.DecodeContainerOpen(expected);
        Assert.Equal(packet, decoded);
        Assert.Equal(1, decoded.WindowId);
        Assert.Equal(0, decoded.WindowType);
        Assert.Equal(10, decoded.X);
        Assert.Equal(64, decoded.Y);
        Assert.Equal(-20, decoded.Z);
        Assert.Equal(-1L, decoded.EntityUniqueId);
    }

    [Fact]
    public void ContainerOpenPreservesExtremeCoordinatesAndEntityId()
    {
        var packet = new BedrockContainerOpenPacket(-1, 12, int.MinValue, int.MaxValue, 0, long.MinValue);

        Assert.Equal(packet, BedrockInventoryCodecs.DecodeContainerOpen(BedrockInventoryCodecs.Encode(packet)));
    }

    [Fact]
    public void ContainerCloseUsesExactWireForm()
    {
        var packet = new BedrockContainerClosePacket(1, 3, false);
        byte[] expected = Convert.FromHexString("010300");

        Assert.Equal(expected, BedrockInventoryCodecs.Encode(packet));
        BedrockContainerClosePacket decoded = BedrockInventoryCodecs.DecodeContainerClose(expected);
        Assert.Equal(packet, decoded);
        Assert.Equal(1, decoded.WindowId);
        Assert.Equal(3, decoded.WindowType);
        Assert.False(decoded.ServerSide);
    }

    [Fact]
    public void ContainerClosePreservesServerSideClose()
    {
        var packet = new BedrockContainerClosePacket(5, 0, true);

        Assert.Equal(packet, BedrockInventoryCodecs.DecodeContainerClose(BedrockInventoryCodecs.Encode(packet)));
    }

    [Fact]
    public void PlayerHotbarUsesExactWireForm()
    {
        var packet = new BedrockPlayerHotbarPacket(2, 0, true);
        byte[] expected = Convert.FromHexString("020001");

        Assert.Equal(expected, BedrockInventoryCodecs.Encode(packet));
        BedrockPlayerHotbarPacket decoded = BedrockInventoryCodecs.DecodePlayerHotbar(expected);
        Assert.Equal(packet, decoded);
        Assert.Equal(2, decoded.SelectedSlot);
        Assert.Equal(0, decoded.WindowId);
        Assert.True(decoded.SelectSlot);
    }

    [Fact]
    public void PlayerHotbarPreservesNegativeWindowAndUnselectedSlot()
    {
        var packet = new BedrockPlayerHotbarPacket(0, -1, false);

        Assert.Equal(packet, BedrockInventoryCodecs.DecodePlayerHotbar(BedrockInventoryCodecs.Encode(packet)));
    }

    [Theory]
    [InlineData("010014800127")]
    [InlineData("0100148001270100")]
    [InlineData("01030000")]
    [InlineData("010302")]
    [InlineData("02000100")]
    [InlineData("020002")]
    [InlineData("")]
    public void ContainerLifecyclePacketsRejectTruncatedOrTrailingPayloads(string payloadHex)
    {
        byte[] payload = Convert.FromHexString(payloadHex);
        Assert.Throws<BedrockCodecException>(() => BedrockInventoryCodecs.DecodeContainerOpen(payload));
        Assert.Throws<BedrockCodecException>(() => BedrockInventoryCodecs.DecodeContainerClose(payload));
        Assert.Throws<BedrockCodecException>(() => BedrockInventoryCodecs.DecodePlayerHotbar(payload));
    }

    private sealed class BedrockWriterForTest
    {
        internal byte[] WriteContentWithCount(uint count)
        {
            var output = new System.Buffers.ArrayBufferWriter<byte>();
            var writer = new BedrockWriter(output);
            writer.WriteUInt32VarInt(0u);
            writer.WriteUInt32VarInt(count);
            return output.WrittenSpan.ToArray();
        }
    }
}
