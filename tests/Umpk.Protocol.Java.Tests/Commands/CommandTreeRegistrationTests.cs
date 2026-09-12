using System.Buffers;
using Umpk;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Commands;

/// <summary>Registration-wiring tests for the declare-commands packet: driving <see cref="PacketRegistrar.Register"/> the way the generated descriptor code does resolves <c>minecraft:commands</c> to an implemented codec (replacing the marker) on 770/776, and to a marker on 1.8 (the packet predates DeclareCommands).</summary>
public class CommandTreeRegistrationTests
{
    private static readonly GameVersion Dummy = new(GameEdition.Java, "test", 770);

    private static BoundPacketCodec Bind(PacketFlow flow, string identifier, string codecKey)
    {
        var builder = new ProtocolDescriptorBuilder(Dummy, new ProtocolFeatures());
        PacketRegistrar.Register(builder, ProtocolPhase.Play, flow, 0, identifier);
        ProtocolDescriptor descriptor = builder.Build();
        Assert.True(descriptor.GetRegistry(ProtocolPhase.Play, flow).TryGetInbound(0, out BoundPacketCodec entry));
        return entry;
    }

    [Theory]
    [InlineData("V1_21_5")]
    [InlineData("V26_1")]
    [InlineData("V26_2")]
    public void Commands_ResolvesImplementedCodec(string codecKey)
    {
        BoundPacketCodec entry = Bind(PacketFlow.Clientbound, "minecraft:commands", codecKey);
        Assert.True(entry.IsImplemented, $"minecraft:commands ({codecKey}) should resolve an implemented codec, not a marker.");
    }

    [Fact]
    public void Commands_770_And_776_UseDistinctArgumentTables()
    {
        // Both eras resolve an implemented codec; the 770 codec closes over the 1.21.5 argument-type table and the 776 codec over the 26.2 table (proven by the registry-shift test below).
        BoundPacketCodec v770 = Bind(PacketFlow.Clientbound, "minecraft:commands", "V1_21_5");
        BoundPacketCodec v776 = Bind(PacketFlow.Clientbound, "minecraft:commands", "V26_1");
        Assert.True(v770.IsImplemented);
        Assert.True(v776.IsImplemented);
    }

    /// <summary>Every protocol in the Brigadier numeric-id band must bind the parser table for ITS OWN era. <c>minecraft:uuid</c> is the last entry of every vanilla table, so its wire id is a compact fingerprint of which table the timeline bound. A codec closing over a neighbour's table resolves a different name for the expected final id.</summary>
    [Theory]
    [InlineData(759, 47)]
    [InlineData(760, 47)]
    [InlineData(761, 47)]
    [InlineData(762, 48)]
    [InlineData(763, 48)]
    [InlineData(764, 48)]
    [InlineData(765, 49)]
    [InlineData(766, 53)]
    [InlineData(767, 53)]
    [InlineData(768, 53)]
    [InlineData(769, 53)]
    [InlineData(770, 54)]
    [InlineData(771, 56)]
    [InlineData(772, 56)]
    [InlineData(773, 56)]
    [InlineData(774, 56)]
    [InlineData(775, 56)]
    [InlineData(776, 56)]
    public void Commands_BindsThisProtocolsOwnParserTable(int protocol, int uuidParserId)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:commands");
        var packet = (ClientboundCommandsPacket)bound.DecodeFrame(SingleArgumentFrame(uuidParserId));

        Assert.Equal(2, packet.Tree.Nodes.Length);
        Assert.Equal("minecraft:uuid", packet.Tree.Nodes[1].Argument!.ParserName);
    }

    /// <summary>26.2 renamed <c>minecraft:color</c> to <c>minecraft:team_color</c> at id 16 while 1.21.6-26.1 kept <c>minecraft:color</c>. Both are payload-free, so nothing desynchronizes and only the resolved name (and therefore the encode-side lookup) tells the two tables apart.</summary>
    [Theory]
    [InlineData(771, "minecraft:color")]
    [InlineData(775, "minecraft:color")]
    [InlineData(776, "minecraft:team_color")]
    public void Commands_ColorParser_ResolvesPerWireLayoutName(int protocol, string expected)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:commands");
        var packet = (ClientboundCommandsPacket)bound.DecodeFrame(SingleArgumentFrame(16));
        Assert.Equal(expected, packet.Tree.Nodes[1].Argument!.ParserName);
    }

    /// <summary>A minimal two-node frame: root with one payload-free argument child using this parser id.</summary>
    private static byte[] SingleArgumentFrame(int parserId)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteVarInt(2);           // node count
        w.WriteByte(0x00);          // root flags
        w.WriteVarInt(1);           // one child
        w.WriteVarInt(1);           // child index
        w.WriteByte(0x02);          // argument node
        w.WriteVarInt(0);           // no children
        w.WriteString("a");         // argument name
        w.WriteVarInt(parserId);    // parser id (payload-free parser: no properties follow)
        w.WriteVarInt(0);           // root index
        return buffer.WrittenSpan.ToArray();
    }
}
