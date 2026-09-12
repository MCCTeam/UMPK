using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.World;

public class ChunkBlockUpdateBindingTests
{
    [Theory]
    [InlineData(477)]
    [InlineData(480)]
    [InlineData(485)]
    [InlineData(490)]
    [InlineData(498)]
    [InlineData(573)]
    [InlineData(575)]
    [InlineData(578)]
    [InlineData(735)]
    [InlineData(736)]
    public void ChunkBlocksUpdate_ResolvesMultiBlockTimeline(int protocol)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:chunk_blocks_update");
        var packet = new ClientboundSectionBlocksUpdatePacket(
            0, -3, 11, [new SectionBlockChange(0x0A93, 1234), new SectionBlockChange(0x0FFF, 9)], IsLegacy: true);
        byte[] wire = bound.Encode(packet);
        Assert.Equal([0xFF, 0xFF, 0xFF, 0xFD, 0x00, 0x00, 0x00, 0x0B, 0x02, 0x0A, 0x93, 0xD2, 0x09, 0x0F, 0xFF, 0x09],
                     wire);
        var back = Assert.IsType<ClientboundSectionBlocksUpdatePacket>(bound.DecodeFrame(wire));
        Assert.Equal(-3, back.LegacyChunkX);
        Assert.Equal(11, back.LegacyChunkZ);
        Assert.Equal(2, back.Changes.Count);
        Assert.Equal(1234, back.Changes[0].BlockStateId);
        Assert.Equal(0x0FFF, back.Changes[1].PackedPosition);
    }
}
