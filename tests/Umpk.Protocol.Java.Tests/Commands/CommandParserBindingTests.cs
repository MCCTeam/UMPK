using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Commands;

public class CommandParserBindingTests
{
    public static TheoryData<int> StringParserProtocols => [735, 736, 751, 753, 754, 755, 756, 757, 758];
    [Theory]
    [MemberData(nameof(StringParserProtocols))]
    public void Commands_UsesStringKeyedParser(int protocol)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:commands");
        byte[] wire = bound.Encode(StringKeyedTree());
        Assert.Contains("minecraft:entity", System.Text.Encoding.UTF8.GetString(wire), StringComparison.Ordinal);
        var back = Assert.IsType<ClientboundCommandsPacket>(bound.DecodeFrame(wire));
        Assert.Equal(3, back.Tree.Nodes.Length);
        Assert.Equal(0, back.Tree.RootIndex);
        Assert.Equal("teleport", back.Tree.Nodes[1].Name);
        Assert.Equal("targets", back.Tree.Nodes[2].Name);
        Assert.Equal("minecraft:entity", back.Tree.Nodes[2].Argument!.ParserName);
        var props = Assert.IsType<EntityArgumentProperties>(back.Tree.Nodes[2].Argument!.Properties);
        Assert.True(props.SingleTarget);
        Assert.True(props.PlayersOnly);
    }

    [Fact]
    public void Commands_StringAndNumericParserFramesRejectEachOther()
    {
        BoundPacketCodec text = BoundCodec.At(758, PacketFlow.Clientbound, "minecraft:commands");
        BoundPacketCodec numeric = BoundCodec.At(759, PacketFlow.Clientbound, "minecraft:commands");
        byte[] asText = text.Encode(StringKeyedTree());
        byte[] asNumeric = numeric.Encode(StringKeyedTree());
        Assert.NotEqual(asText, asNumeric);
        WireFrameAssertions.DoesNotRoundTrip(numeric, asText);
        WireFrameAssertions.DoesNotRoundTrip(text, asNumeric);
    }
    private static ClientboundCommandsPacket StringKeyedTree()
    {
        var root = new CommandNodeData(CommandNodeKind.Root, 0x00, [1], -1, null, null);
        var literal = new CommandNodeData(CommandNodeKind.Literal, 0x01, [2], -1, "teleport", null);
        var argument = new CommandNodeData(
            CommandNodeKind.Argument, 0x02, [], -1, "targets",
            new CommandArgumentData(-1, "minecraft:entity", new EntityArgumentProperties(true, true), null));
        return new ClientboundCommandsPacket(new CommandTreeData([root, literal, argument], 0));
    }
}
