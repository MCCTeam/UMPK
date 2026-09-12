using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.World;

public class LightUpdateBindingTests
{
    public static TheoryData<int, LightWireShape> Shapes =>
        new() { { 477, LightWireShape.MaskWithoutFlag },   { 578, LightWireShape.MaskWithoutFlag },
                { 735, LightWireShape.MaskWithFlag },      { 736, LightWireShape.MaskWithFlag },
                { 751, LightWireShape.MaskWithFlag },      { 753, LightWireShape.MaskWithFlag },
                { 754, LightWireShape.MaskWithFlag },      { 755, LightWireShape.BitSetWithFlag },
                { 756, LightWireShape.BitSetWithFlag },    { 757, LightWireShape.BitSetWithFlag },
                { 758, LightWireShape.BitSetWithFlag },    { 759, LightWireShape.BitSetWithFlag },
                { 760, LightWireShape.BitSetWithFlag },    { 761, LightWireShape.BitSetWithFlag },
                { 762, LightWireShape.BitSetWithFlag },    { 763, LightWireShape.BitSetWithoutFlag },
                { 764, LightWireShape.BitSetWithoutFlag }, { 770, LightWireShape.BitSetWithoutFlag } };
    public enum LightWireShape
    {
        MaskWithoutFlag,
        MaskWithFlag,
        BitSetWithFlag,
        BitSetWithoutFlag
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void LightUpdate_UsesExpectedWireShape(int protocol, LightWireShape shape)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:light_update");
        byte[] wire = bound.Encode(Packet());
        Assert.Equal(Frame(shape), wire);
        var back = Assert.IsType<ClientboundLightUpdatePacket>(bound.DecodeFrame(wire));
        Assert.Equal(-13, back.ChunkX);
        Assert.Equal(7, back.ChunkZ);
        Assert.Single(back.Light.SkyUpdates);
        Assert.Equal(0xAB, back.Light.SkyUpdates[0][0]);
        Assert.Equal(0xCD, back.Light.SkyUpdates[0][2047]);
        Assert.Empty(back.Light.BlockUpdates);
        Assert.True(back.Light.TrustEdges);
    }

    [Theory]
    [InlineData(754, LightWireShape.MaskWithoutFlag)]
    [InlineData(754, LightWireShape.BitSetWithFlag)]
    [InlineData(754, LightWireShape.BitSetWithoutFlag)]
    [InlineData(758, LightWireShape.MaskWithoutFlag)]
    [InlineData(758, LightWireShape.MaskWithFlag)]
    [InlineData(758, LightWireShape.BitSetWithoutFlag)]
    [InlineData(763, LightWireShape.MaskWithoutFlag)]
    [InlineData(763, LightWireShape.MaskWithFlag)]
    [InlineData(763, LightWireShape.BitSetWithFlag)]
    [InlineData(578, LightWireShape.MaskWithFlag)]
    [InlineData(578, LightWireShape.BitSetWithFlag)]
    public void LightUpdate_RejectsNeighbourFrames(int protocol, LightWireShape foreign) =>
        WireFrameAssertions.DoesNotRoundTrip(BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:light_update"),
                                             Frame(foreign));
    private static ClientboundLightUpdatePacket Packet()
    {
        byte[] sky = new byte[2048];
        sky[0] = 0xAB;
        sky[2047] = 0xCD;
        return new(-13, 7, new LightUpdateData([0x01], [0x00], [0x02], [0x04], [sky], [], TrustEdges: true));
    }
    private static byte[] Frame(LightWireShape shape)
    {
        byte[] sky = new byte[2048];
        sky[0] = 0xAB;
        sky[2047] = 0xCD;
        var w = new WireFrameAssertions.FrameWriter();
        w.VarInt(-13);
        w.VarInt(7);
        if (shape is LightWireShape.MaskWithFlag or LightWireShape.BitSetWithFlag)
            w.U8(1);
        if (shape is LightWireShape.MaskWithoutFlag or LightWireShape.MaskWithFlag)
        {
            w.VarInt(1);
            w.VarInt(0);
            w.VarInt(2);
            w.VarInt(4);
            w.VarInt(sky.Length);
            w.Raw(sky);
            return w.ToArray();
        }
        w.LongArray(1);
        w.LongArray(0);
        w.LongArray(2);
        w.LongArray(4);
        w.VarInt(1);
        w.VarInt(sky.Length);
        w.Raw(sky);
        w.VarInt(0);
        return w.ToArray();
    }
}
