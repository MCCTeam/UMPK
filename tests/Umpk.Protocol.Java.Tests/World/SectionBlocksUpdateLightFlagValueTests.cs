using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.World;

public sealed class SectionBlocksUpdateLightFlagValueTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bound755_ExposesAndEncodesTheNativeFlag(bool suppressLightUpdates)
    {
        BoundPacketCodec codec = BoundCodec.At(755, PacketFlow.Clientbound, "minecraft:section_blocks_update");
        var packet = new ClientboundSectionBlocksUpdatePacket(6, 0, 0, [new SectionBlockChange(0x123, 1)])
        {
            SuppressLightUpdates = suppressLightUpdates,
        };
        byte[] payload = codec.Encode(packet);
        Assert.Equal(suppressLightUpdates ? (byte)1 : (byte)0, payload[8]);
        var decoded = Assert.IsType<ClientboundSectionBlocksUpdatePacket>(codec.DecodeFrame(payload));
        Assert.Equal(suppressLightUpdates, decoded.SuppressLightUpdates);
        Assert.False(new ClientboundSectionBlocksUpdatePacket(0, 0, 0, []).SuppressLightUpdates);
        Assert.Equal(payload, codec.Encode(decoded));
    }
}
