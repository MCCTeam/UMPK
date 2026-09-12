using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary>Binding-level pins for the container-id (and button-id) wire WIDTH across eras. Vanilla writes the container id as a byte on every protocol through 1.21.1 (767) and as a VarInt from 1.21.2 (768). The one exception is <c>container_button_click</c>, which moved BOTH of its fields to VarInts at 1.20.5 (766).</summary>
/// <remarks>
/// The covered packet families share these boundaries from 1.14.4 through 1.21.2.
/// <para>Every id these packets actually carry is under 128, where a byte and a VarInt are the SAME single byte - which is exactly why these four sat mis-bound (a VarInt codec on the 477-763 byte band, and a byte codec on the 766/767 VarInt band for the button click) without any test or capture noticing. The values below are therefore deliberately above 127: that is the only region where the wire forms are distinguishable at all.</para>
/// </remarks>
public class ContainerIdEncodingTests
{
    private const int WideId = 200;      // 0xC8: one byte as a byte, two bytes as a VarInt.
    private static readonly byte[] AsByte = [0xC8];
    private static readonly byte[] AsVarInt = [0xC8, 0x01];

    private static void AssertHeader(BoundPacketCodec bound, object packet, byte[] expectedHeader, byte[] tail)
    {
        byte[] expected = [.. expectedHeader, .. tail];
        Assert.Equal(expected, bound.Encode(packet));

        // ...and the same bytes must decode back through the live entry point, frame-exactly.
        Assert.Equal(packet, bound.DecodeFrame(expected));
    }

    // container_close in both flows.

    [Theory]
    [InlineData(477, false)]   // 1.14
    [InlineData(754, false)]   // 1.16.5
    [InlineData(763, false)]   // 1.20.1
    [InlineData(765, false)]   // 1.20.3
    [InlineData(767, false)]   // 1.21.1, the last byte release
    [InlineData(768, true)]    // 1.21.2, readContainerId
    [InlineData(776, true)]    // 26.2
    public void ClientboundContainerClose_ContainerIdWidth(int protocol, bool varInt)
    {
        AssertHeader(
            BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:container_close"),
            new ClientboundContainerClosePacket(WideId),
            varInt ? AsVarInt : AsByte,
            []);
    }

    [Theory]
    [InlineData(47, false)]    // 1.8
    [InlineData(477, false)]
    [InlineData(763, false)]
    [InlineData(767, false)]
    [InlineData(768, true)]
    [InlineData(776, true)]
    public void ServerboundContainerClose_ContainerIdWidth(int protocol, bool varInt)
    {
        AssertHeader(
            BoundCodec.At(protocol, PacketFlow.Serverbound, "minecraft:container_close"),
            new ServerboundContainerClosePacket(WideId),
            varInt ? AsVarInt : AsByte,
            []);
    }

    // container_set_data.

    [Theory]
    [InlineData(477, false)]
    [InlineData(754, false)]
    [InlineData(763, false)]
    [InlineData(767, false)]
    [InlineData(768, true)]
    [InlineData(776, true)]
    public void ContainerSetData_ContainerIdWidth(int protocol, bool varInt)
    {
        // Tail: property-id short and value short, unchanged across every era.
        AssertHeader(
            BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:container_set_data"),
            new ClientboundContainerSetDataPacket(WideId, 2, 300),
            varInt ? AsVarInt : AsByte,
            [0x00, 0x02, 0x01, 0x2C]);
    }

    // container_button_click.

    // This one flips at 1.20.5, not 1.21.2: both fields became VarInts there, and 1.21.2 only renamed the first codec to CONTAINER_ID (still a VarInt).
    [Theory]
    [InlineData(47, false)]    // 1.8
    [InlineData(477, false)]   // 1.14
    [InlineData(754, false)]   // 1.16.5
    [InlineData(763, false)]   // 1.20.1
    [InlineData(765, false)]   // 1.20.3, the last byte release
    [InlineData(766, true)]    // 1.20.5/1.20.6, VAR_INT + VAR_INT
    [InlineData(767, true)]    // 1.21.1
    [InlineData(768, true)]    // 1.21.2, CONTAINER_ID + VAR_INT
    [InlineData(776, true)]    // 26.2
    public void ContainerButtonClick_BothFieldsChangeWidthTogether(int protocol, bool varInt)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Serverbound, "minecraft:container_button_click");
        var packet = new ServerboundContainerButtonClickPacket(WideId, ButtonId: 200);

        byte[] expected = varInt ? [.. AsVarInt, .. AsVarInt] : [.. AsByte, .. AsByte];
        Assert.Equal(expected, bound.Encode(packet));
        Assert.Equal(packet, bound.DecodeFrame(expected));
    }
}
