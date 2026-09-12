using System.Buffers;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Commands;

/// <summary>Round-trip tests for the declare-commands (<c>minecraft:commands</c>) codec: synthetic trees with redirects and forks, every implemented parser property payload, and unknown-parser raw preservation. Byte-identity is asserted by re-encoding a decoded tree and comparing bytes.</summary>
public class CommandTreeCodecTests
{
    private static CommandNodeData Root(params int[] children) =>
        new(CommandNodeKind.Root, 0x00, [.. children], -1, null, null);

    private static CommandNodeData Literal(string name, byte extraFlags = 0, int[]? children = null, int redirect = -1)
    {
        byte flags = (byte)(0x01 | extraFlags | (redirect >= 0 ? 0x08 : 0));
        return new CommandNodeData(CommandNodeKind.Literal, flags, [.. (children ?? [])], redirect, name, null);
    }

    private static CommandNodeData Argument(
        string name, string parser, ArgumentParserProperties props, byte extraFlags = 0,
        int[]? children = null, Identifier? suggestion = null)
    {
        byte flags = (byte)(0x02 | extraFlags | (suggestion is not null ? 0x10 : 0));
        var arg = new CommandArgumentData(0, parser, props, suggestion);
        return new CommandNodeData(CommandNodeKind.Argument, flags, [.. (children ?? [])], -1, name, arg);
    }

    private static void AssertByteIdentical(PacketCodec<ClientboundCommandsPacket> codec, ClientboundCommandsPacket packet)
    {
        byte[] first = CodecRoundTrip.Encode(codec, packet);
        var reader = new PacketReader(first);
        ClientboundCommandsPacket decoded = codec.Decode(ref reader, PacketCodecContext.Registryless);
        Assert.Equal(0, reader.Remaining);
        byte[] second = CodecRoundTrip.Encode(codec, decoded);
        Assert.Equal(first, second);
    }

    [Fact]
    public void SimpleLiteralTree_RoundTrips_ByteIdentical()
    {
        // root -> literal "tp" (executable) -> argument "target" (entity)
        var tree = new CommandTreeData(
        [
            Root(1),
            Literal("tp", extraFlags: 0x04, children: [2]),
            Argument("target", "minecraft:entity", new EntityArgumentProperties(true, false), extraFlags: 0x04),
        ], 0);

        var packet = new ClientboundCommandsPacket(tree);
        AssertByteIdentical(CommandTreeCodecs.V1_21_5, packet);

        ClientboundCommandsPacket back = CodecRoundTrip.Cycle(CommandTreeCodecs.V1_21_5, packet);
        Assert.Equal(3, back.Tree.Nodes.Length);
        Assert.Equal("tp", back.Tree.Nodes[1].Name);
        Assert.True(back.Tree.Nodes[1].IsExecutable);
        Assert.Equal("minecraft:entity", back.Tree.Nodes[2].Argument!.ParserName);
    }

    [Fact]
    public void Redirect_RoundTrips()
    {
        // root -> literal "execute" -> redirect back to the root (a self-referential redirect, cycle-safe).
        var tree = new CommandTreeData(
        [
            Root(1),
            Literal("execute", children: [], redirect: 0),
        ], 0);

        var packet = new ClientboundCommandsPacket(tree);
        AssertByteIdentical(CommandTreeCodecs.V1_21_5, packet);

        ClientboundCommandsPacket back = CodecRoundTrip.Cycle(CommandTreeCodecs.V1_21_5, packet);
        Assert.True(back.Tree.Nodes[1].HasRedirect);
        Assert.Equal(0, back.Tree.Nodes[1].RedirectIndex);
    }

    [Fact]
    public void Fork_MultipleChildren_RoundTrips()
    {
        // root -> literal "give" -> two argument children (a fork of alternatives).
        var tree = new CommandTreeData(
        [
            Root(1),
            Literal("give", children: [2, 3]),
            Argument("targets", "minecraft:entity", new EntityArgumentProperties(false, true)),
            Argument("item", "minecraft:item_stack", ArgumentParserProperties.Empty),
        ], 0);

        AssertByteIdentical(CommandTreeCodecs.V1_21_5, new ClientboundCommandsPacket(tree));
        ClientboundCommandsPacket back = CodecRoundTrip.Cycle(CommandTreeCodecs.V1_21_5, new ClientboundCommandsPacket(tree));
        Assert.Equal(2, back.Tree.Nodes[1].Children.Length);
    }

    [Fact]
    public void CustomSuggestions_RoundTrips()
    {
        var tree = new CommandTreeData(
        [
            Root(1),
            Literal("summon", children: [2]),
            Argument("entity", "minecraft:resource_location", ArgumentParserProperties.Empty,
                suggestion: Identifier.Parse("minecraft:summonable_entities")),
        ], 0);

        AssertByteIdentical(CommandTreeCodecs.V1_21_5, new ClientboundCommandsPacket(tree));
        ClientboundCommandsPacket back = CodecRoundTrip.Cycle(CommandTreeCodecs.V1_21_5, new ClientboundCommandsPacket(tree));
        Assert.Equal("minecraft:summonable_entities", back.Tree.Nodes[2].Argument!.SuggestionProvider!.Value.ToString());
    }

    public static IEnumerable<object[]> ParserPayloads()
    {
        yield return [new StringArgumentProperties(BrigadierStringKind.SingleWord), "brigadier:string"];
        yield return [new StringArgumentProperties(BrigadierStringKind.GreedyPhrase), "brigadier:string"];
        yield return [new IntegerArgumentProperties(int.MinValue, int.MaxValue), "brigadier:integer"];
        yield return [new IntegerArgumentProperties(-5, 100), "brigadier:integer"];
        yield return [new IntegerArgumentProperties(0, int.MaxValue), "brigadier:integer"];
        yield return [new LongArgumentProperties(long.MinValue, long.MaxValue), "brigadier:long"];
        yield return [new LongArgumentProperties(-9, 9), "brigadier:long"];
        yield return [new FloatArgumentProperties(-float.MaxValue, float.MaxValue), "brigadier:float"];
        yield return [new FloatArgumentProperties(0.5f, 10.0f), "brigadier:float"];
        yield return [new DoubleArgumentProperties(-double.MaxValue, double.MaxValue), "brigadier:double"];
        yield return [new DoubleArgumentProperties(-1.5, 3.5), "brigadier:double"];
        yield return [new EntityArgumentProperties(true, false), "minecraft:entity"];
        yield return [new EntityArgumentProperties(false, true), "minecraft:entity"];
        yield return [new ScoreHolderArgumentProperties(true), "minecraft:score_holder"];
        yield return [new ScoreHolderArgumentProperties(false), "minecraft:score_holder"];
        yield return [new TimeArgumentProperties(0), "minecraft:time"];
        yield return [new TimeArgumentProperties(20), "minecraft:time"];
        yield return [new RegistryArgumentProperties(Identifier.Parse("minecraft:item")), "minecraft:resource"];
        yield return [new RegistryArgumentProperties(Identifier.Parse("minecraft:entity_type")), "minecraft:resource_key"];
        yield return [new RegistryArgumentProperties(Identifier.Parse("minecraft:worldgen/biome")), "minecraft:resource_or_tag"];
        yield return [ArgumentParserProperties.Empty, "brigadier:bool"];
        yield return [ArgumentParserProperties.Empty, "minecraft:block_pos"];
        yield return [ArgumentParserProperties.Empty, "minecraft:message"];
    }

    [Theory]
    [MemberData(nameof(ParserPayloads))]
    public void ParserProperty_RoundTrips_ByteIdentical(ArgumentParserProperties props, string parser)
    {
        var tree = new CommandTreeData(
        [
            Root(1),
            Argument("a", parser, props),
        ], 0);

        var packet = new ClientboundCommandsPacket(tree);
        AssertByteIdentical(CommandTreeCodecs.V1_21_5, packet);

        ClientboundCommandsPacket back = CodecRoundTrip.Cycle(CommandTreeCodecs.V1_21_5, packet);
        Assert.Equal(props, back.Tree.Nodes[1].Argument!.Properties);
    }

    [Fact]
    public void UnknownParser_OnEncode_Throws()
    {
        // There is no opaque round-trip for an unknown parser. The wire property payload has no length prefix, so the decoder rejects an unknown parser id (see UnknownParserId_OnDecode_Throws) and never builds an argument node with a null parser name. An argument node whose parser could not be resolved is therefore a non-serializable stub, and the encoder rejects it symmetrically rather than silently emitting a truncated node.
        var unknownArg = new CommandArgumentData(9999, ParserName: null, Properties: null, SuggestionProvider: null);
        var node = new CommandNodeData(CommandNodeKind.Argument, 0x02, [], -1, "x", unknownArg);
        var tree = new CommandTreeData([Root(1), node], 0);

        Assert.Throws<ProtocolViolationException>(() =>
            CodecRoundTrip.Encode(CommandTreeCodecs.V1_21_5, new ClientboundCommandsPacket(tree)));
    }

    [Fact]
    public void UnknownParserId_OnDecode_Throws()
    {
        // Hand-build a frame that references a parser id past the table (no length prefix means the codec cannot skip it), and confirm the decoder raises a frame-exact violation.
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteVarInt(2);            // node count
        w.WriteByte(0x00);          // root flags
        w.WriteVarInt(1);           // 1 child
        w.WriteVarInt(1);           // child index 1
        w.WriteByte(0x02);          // argument node
        w.WriteVarInt(0);           // no children
        w.WriteString("a");         // name
        w.WriteVarInt(9999);        // unknown parser id
        w.WriteVarInt(0);           // root index

        Assert.Throws<ProtocolViolationException>(() =>
        {
            var r = new PacketReader(buffer.WrittenSpan);
            CommandTreeCodecs.V1_21_5.Decode(ref r, PacketCodecContext.Registryless);
        });
    }

    [Fact]
    public void RestrictedFlag_PreservedOnEncode_V26_2()
    {
        // 26.2 adds FLAG_RESTRICTED (0x20). It is a read-through flag; the codec preserves the raw byte.
        var node = Literal("op", extraFlags: 0x20);
        var tree = new CommandTreeData([Root(1), node], 0);
        AssertByteIdentical(CommandTreeCodecs.V26_2, new ClientboundCommandsPacket(tree));

        ClientboundCommandsPacket back = CodecRoundTrip.Cycle(CommandTreeCodecs.V26_2, new ClientboundCommandsPacket(tree));
        Assert.True(back.Tree.Nodes[1].IsRestricted);
    }

    /// <summary>The era codecs a non-trivial synthetic tree must survive, one per distinct parser table.</summary>
    public static TheoryData<string> EraCodecKeys => new()
    {
        "V759", "V761", "V764", "V765", "V766", "V1_21_5", "V1_21_6", "V26_2",
    };

    private static PacketCodec<ClientboundCommandsPacket> EraCodec(string key) => key switch
    {
        "V759" => CommandTreeCodecs.V1_19,
        "V761" => CommandTreeCodecs.V1_19_3,
        "V764" => CommandTreeCodecs.V1_19_4,
        "V765" => CommandTreeCodecs.V1_20_3,
        "V766" => CommandTreeCodecs.V1_20_5,
        "V1_21_5" => CommandTreeCodecs.V1_21_5,
        "V1_21_6" => CommandTreeCodecs.V1_21_6,
        _ => CommandTreeCodecs.V26_2,
    };

    /// <summary>A tree with one literal cannot distinguish parser tables. This one has several literals, an argument node for every payload SHAPE the tables carry (empty, varint, flags-plus-bounds, one-byte flags, registry string), an executable leaf, a redirect and a suggestion provider, and it must round-trip byte-identically under every era codec.</summary>
    [Theory]
    [MemberData(nameof(EraCodecKeys))]
    public void NonTrivialTree_RoundTrips_ByteIdentical_OnEveryWireLayout(string codecKey)
    {
        PacketCodec<ClientboundCommandsPacket> codec = EraCodec(codecKey);

        // 0 root -> 1 "give", 2 "teleport", 3 "tp" (redirects to 2), 4 "scoreboard"
        //   1 "give"       -> 5 targets(entity, one byte), 6 item(item_stack, empty)
        //   2 "teleport"   -> 7 dest(vec3, empty, executable), 8 name(game_profile, suggestions)
        //   4 "scoreboard" -> 9 holder(score_holder, one byte), 10 count(integer, bounds),
        //                     11 phrase(string, varint), 12 registry(resource_or_tag, registry string)
        var tree = new CommandTreeData(
        [
            Root(1, 2, 3, 4),
            Literal("give", children: [5, 6]),
            Literal("teleport", children: [7, 8]),
            Literal("tp", redirect: 2),
            Literal("scoreboard", children: [9, 10, 11, 12]),
            Argument("targets", "minecraft:entity", new EntityArgumentProperties(false, true)),
            Argument("item", "minecraft:item_stack", ArgumentParserProperties.Empty, extraFlags: 0x04),
            Argument("dest", "minecraft:vec3", ArgumentParserProperties.Empty, extraFlags: 0x04),
            Argument("name", "minecraft:game_profile", ArgumentParserProperties.Empty,
                suggestion: Identifier.Parse("minecraft:ask_server")),
            Argument("holder", "minecraft:score_holder", new ScoreHolderArgumentProperties(true)),
            Argument("count", "brigadier:integer", new IntegerArgumentProperties(0, 64)),
            Argument("phrase", "brigadier:string", new StringArgumentProperties(BrigadierStringKind.GreedyPhrase)),
            Argument("registry", "minecraft:resource_or_tag",
                new RegistryArgumentProperties(Identifier.Parse("minecraft:worldgen/biome"))),
        ], 0);

        var packet = new ClientboundCommandsPacket(tree);
        AssertByteIdentical(codec, packet);

        ClientboundCommandsPacket back = CodecRoundTrip.Cycle(codec, packet);
        Assert.Equal(13, back.Tree.Nodes.Length);
        Assert.Equal("tp", back.Tree.Nodes[3].Name);
        Assert.Equal(2, back.Tree.Nodes[3].RedirectIndex);
        Assert.True(back.Tree.Nodes[7].IsExecutable);
        Assert.Equal("minecraft:ask_server", back.Tree.Nodes[8].Argument!.SuggestionProvider!.Value.ToString());
        Assert.Equal(new ScoreHolderArgumentProperties(true), back.Tree.Nodes[9].Argument!.Properties);
        Assert.Equal(new IntegerArgumentProperties(0, 64), back.Tree.Nodes[10].Argument!.Properties);
        Assert.Equal(new StringArgumentProperties(BrigadierStringKind.GreedyPhrase), back.Tree.Nodes[11].Argument!.Properties);
        Assert.Equal(
            new RegistryArgumentProperties(Identifier.Parse("minecraft:worldgen/biome")),
            back.Tree.Nodes[12].Argument!.Properties);
    }

    [Theory]
    [InlineData("V759")]
    [InlineData("V761")]
    public void TimeMinimum_OnAnWireLayoutWithoutIt_IsRejectedOnEncode(string codecKey)
    {
        // minecraft:time gained its int min at protocol 762. A hand-built tree that carries one on an earlier era must not silently emit four bytes the peer will not read.
        var tree = new CommandTreeData(
        [
            Root(1),
            Argument("time", "minecraft:time", new TimeArgumentProperties(20)),
        ], 0);

        Assert.Throws<ProtocolViolationException>(() =>
            CodecRoundTrip.Encode(EraCodec(codecKey), new ClientboundCommandsPacket(tree)));
    }

    [Theory]
    [InlineData("V764")]
    [InlineData("V1_21_5")]
    public void TimeMinimum_OnAnWireLayoutWithIt_RoundTrips(string codecKey)
    {
        var tree = new CommandTreeData(
        [
            Root(1),
            Argument("time", "minecraft:time", new TimeArgumentProperties(20)),
        ], 0);

        PacketCodec<ClientboundCommandsPacket> codec = EraCodec(codecKey);
        AssertByteIdentical(codec, new ClientboundCommandsPacket(tree));
        ClientboundCommandsPacket back = CodecRoundTrip.Cycle(codec, new ClientboundCommandsPacket(tree));
        Assert.Equal(new TimeArgumentProperties(20), back.Tree.Nodes[1].Argument!.Properties);
    }
}
