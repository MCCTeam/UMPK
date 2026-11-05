using System.Buffers;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.World;

public sealed class UpdateTagsCodecTests
{
    [Theory]
    [InlineData(393, 3)]
    [InlineData(401, 3)]
    [InlineData(404, 3)]
    [InlineData(477, 4)]
    [InlineData(498, 4)]
    [InlineData(573, 4)]
    [InlineData(735, 4)]
    [InlineData(754, 4)]
    [InlineData(755, 0)]
    [InlineData(763, 0)]
    [InlineData(764, 0)]
    [InlineData(777, 0)]
    public void NativeTagLayoutsDecodeAndRoundTripThroughThePlayBinding(int protocol, int fixedRegistries)
    {
        byte[] frame = Frame(fixedRegistries);
        BoundPacketCodec codec = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:update_tags");

        ClientboundUpdateTagsPacket packet = Assert.IsType<ClientboundUpdateTagsPacket>(codec.DecodeFrame(frame));

        Assert.Equal(ProtocolPhase.Play, packet.Type.Phase);
        Assert.Equal(fixedRegistries == 0 ? 1 : fixedRegistries, packet.Registries.Count);
        TagRegistry blocks = packet.Registries[0];
        Assert.Equal(Identifier.Minecraft("block"), blocks.Registry);
        TagEntry tag = Assert.Single(blocks.Tags);
        Assert.Equal(Identifier.Parse("example:mineable"), tag.TagId);
        Assert.Equal(new[] { 1, 5 }, tag.Ids);
        Assert.Equal(frame, codec.Encode(packet));
    }

    private static byte[] Frame(int fixedRegistries)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        if (fixedRegistries == 0)
        {
            writer.WriteVarInt(1);
            writer.WriteString("minecraft:block");
        }
        writer.WriteVarInt(1);
        writer.WriteString("example:mineable");
        writer.WriteVarInt(2);
        writer.WriteVarInt(1);
        writer.WriteVarInt(5);
        for (int registry = 1; registry < fixedRegistries; registry++)
            writer.WriteVarInt(0);
        return buffer.WrittenSpan.ToArray();
    }
}
