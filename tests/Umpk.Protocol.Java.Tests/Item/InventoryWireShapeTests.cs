using System.Buffers;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Xunit;
using static Umpk.Protocol.Java.Tests.Support.LiteralFrame;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary>Pins literal frames and incompatible layouts for inventory packets.</summary>
public sealed class InventoryWireShapeTests
{
    [Theory]
    [InlineData(107)]
    [InlineData(404)]
    public void ContainerClose_And_SetData_LiteralFrames(int protocol)
    {
        var close = (ClientboundContainerClosePacket)Clientbound(protocol, "container_close").DecodeFrame([0x7B]);
        Assert.Equal(0x7B, close.ContainerId);

        byte[] frame = Cat([0x7B], I16(2), I16(-9));
        var data = (ClientboundContainerSetDataPacket)Clientbound(protocol, "container_set_data").DecodeFrame(frame);
        Assert.Equal(0x7B, data.ContainerId);
        Assert.Equal(2, data.PropertyId);
        Assert.Equal(-9, data.Value);
    }

    [Theory]
    [InlineData(107)]
    [InlineData(404)]
    public void Cooldown_LiteralFrame_CarriesAnItemId(int protocol)
    {
        byte[] frame = Cat(VarInt(368), VarInt(200));
        var p = (ClientboundCooldownPacket)Clientbound(protocol, "cooldown").DecodeFrame(frame);
        Assert.Equal(368, p.ItemId);
        Assert.Equal(200, p.Ticks);
        Assert.Equal(frame, Clientbound(protocol, "cooldown").Encode(p));
    }

    [Theory]
    [InlineData(107)]
    [InlineData(404)]
    public void ContainerButtonClick_LiteralFrame(int protocol)
    {
        var p = (ServerboundContainerButtonClickPacket)Serverbound(protocol, "container_button_click").DecodeFrame([5, 2]);
        Assert.Equal(5, p.ContainerId);
        Assert.Equal(2, p.ButtonId);
    }

    [Fact]
    public void Cooldown_RejectsGroupIdentifierFrame()
    {
        byte[] legacy = Cat(VarInt(368), VarInt(200));
        Rejects(Clientbound(770, "cooldown"), legacy, "1.21.5 reads a cooldown-group identifier");
    }
}
