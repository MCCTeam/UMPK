using System.Buffers;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

public sealed class DefaultToolStackTests
{
    [Theory]
    [InlineData(766, 22, false)]
    [InlineData(766, 22, true)]
    [InlineData(770, 25, false)]
    [InlineData(770, 25, true)]
    [InlineData(777, 28, false)]
    [InlineData(777, 28, true)]
    public void WirePatchUsesItsItemPrototypeAndPreservesToolRemoval(int protocol, int componentId, bool removed)
    {
        var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
        int itemId = context.Registries.Items[Identifier.Minecraft("diamond_pickaxe")].NetworkId;
        var frame = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(frame);
        if (protocol == 766)
            writer.WriteByte(0);
        else
            writer.WriteVarInt(0);
        writer.WriteVarInt(1);
        writer.WriteShort(36);
        writer.WriteVarInt(1);
        writer.WriteVarInt(itemId);
        writer.WriteVarInt(0);
        writer.WriteVarInt(removed ? 1 : 0);
        if (removed)
            writer.WriteVarInt(componentId);
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:container_set_slot");

        var packet = Assert.IsType<ClientboundContainerSetSlotPacket>(bound.Decode(frame.WrittenSpan, context));

        Assert.Equal(!removed, packet.Item.Components.Has(DataComponents.Tool));
        if (removed)
            Assert.True(Assert.Single(packet.Item.Components.Patch).IsRemoval);
        else
            Assert.Empty(packet.Item.Components.Patch);
        var encoded = new ArrayBufferWriter<byte>();
        writer = new PacketWriter(encoded);
        bound.Encode(ref writer, packet, context);
        Assert.Equal(frame.WrittenSpan.ToArray(), encoded.WrittenSpan.ToArray());
    }
}
