using System.Text.Json;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Commands;

/// <summary>Frame-exact tests for the declare-commands (<c>minecraft:commands</c>) argument-parser table. The fixtures include both default and full operator trees.</summary>
/// <remarks>
/// <para>Protocols 759-763 and 768-769 have parser ids whose property payload is a DIFFERENT length, so one such node desynchronizes every node after it. On the 760 operator frame the first one is node 268 (<c>/execute align &lt;axes&gt;</c>, wire parser id 30 = <c>minecraft:swizzle</c>, no payload) which 770 reads as <c>minecraft:score_holder</c> and one byte long; two nodes later the reader takes 0x63 for a flags byte and reports the impossible "Unknown command node type 3".</para>
/// <para>Default trees use parsers whose payload lengths agree across these tables, so successful decoding alone is insufficient. The tests also assert parser names.</para>
/// </remarks>
public class CommandTreeLiveFrameTests
{
    private const string Commands = "minecraft:commands";

    /// <summary>Operator trees: protocol, node count, root index, and frame length.</summary>
    public static TheoryData<int, int, int, int> OperatorTrees => new()
    {
        { 759, 1133, 0, 16884 },
        { 760, 1133, 0, 16884 },
        { 761, 1149, 0, 17328 },
        { 762, 1362, 0, 20674 },
        { 763, 1363, 0, 20679 },
        { 768, 1465, 0, 22383 },
        { 769, 1465, 0, 22427 },
    };

    /// <summary>Default trees: protocol, node count, root index.</summary>
    public static TheoryData<int, int, int> DefaultTrees => new()
    {
        { 759, 20, 0 },
        { 760, 20, 0 },
        { 761, 20, 0 },
        { 762, 20, 0 },
        { 763, 20, 0 },
        { 768, 25, 0 },
        { 769, 25, 0 },
    };

    [Theory]
    [MemberData(nameof(OperatorTrees))]
    public void OperatorTree_DecodesFrameExactly_AndReEncodesByteIdentical(
        int protocol, int expectedNodes, int expectedRoot, int expectedLength)
    {
        byte[] frame = LoadFrame(protocol, "op");
        Assert.Equal(expectedLength, frame.Length);

        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, Commands);
        var packet = (ClientboundCommandsPacket)bound.DecodeFrame(frame);

        Assert.Equal(expectedNodes, packet.Tree.Nodes.Length);
        Assert.Equal(expectedRoot, packet.Tree.RootIndex);

        // Frame-exactness: BoundPacketCodec.Decode faults on a trailing byte, so reaching here already proves the reader consumed the frame exactly. Re-encoding closes the loop the other way.
        Assert.Equal(frame, bound.Encode(packet));
    }

    [Theory]
    [MemberData(nameof(DefaultTrees))]
    public void DefaultTree_ResolvesTheRealParserNames(int protocol, int expectedNodes, int expectedRoot)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, Commands);
        var packet = (ClientboundCommandsPacket)bound.DecodeFrame(LoadFrame(protocol, "default"));

        Assert.Equal(expectedNodes, packet.Tree.Nodes.Length);
        Assert.Equal(expectedRoot, packet.Tree.RootIndex);

        // The wrong-table decode also "succeeded" on this tree; it named these parsers style, nbt_path and message respectively. Names are the only thing that separates the two decodes here.
        Assert.Equal("minecraft:message", ParserOf(packet, "action"));
        Assert.Equal("minecraft:message", ParserOf(packet, "message"));
        Assert.Equal("minecraft:objective", ParserOf(packet, "objective"));
        Assert.Equal("minecraft:entity", ParserOf(packet, "targets"));
        Assert.Equal("brigadier:string", ParserOf(packet, "command"));
    }

    [Fact]
    public void OperatorTree_760_ParsesTheNodeTheWrongTableDerailedOn()
    {
        BoundPacketCodec bound = BoundCodec.At(760, PacketFlow.Clientbound, Commands);
        var packet = (ClientboundCommandsPacket)bound.DecodeFrame(LoadFrame(760, "op"));

        // Node 268 is /execute align <axes>. Wire parser id 30 is minecraft:swizzle on 759/760 and carries no payload; the 770 table calls id 30 minecraft:score_holder and reads a flags byte.
        CommandNodeData axes = packet.Tree[268];
        Assert.Equal(CommandNodeKind.Argument, axes.Kind);
        Assert.Equal("axes", axes.Name);
        Assert.Equal(0x0A, axes.Flags);
        Assert.Equal(30, axes.Argument!.ParserId);
        Assert.Equal("minecraft:swizzle", axes.Argument.ParserName);
        Assert.Equal(ArgumentParserProperties.Empty, axes.Argument.Properties);
        Assert.True(axes.HasRedirect);
        Assert.Equal(3, axes.RedirectIndex);

        // The byte the wrong table swallowed as swizzle's non-existent payload is node 269's flags byte (0x0A). Read in frame, 269 and 270 are the sibling /execute anchored and /execute in nodes.
        CommandNodeData anchor = packet.Tree[269];
        Assert.Equal("anchor", anchor.Name);
        Assert.Equal("minecraft:entity_anchor", anchor.Argument!.ParserName);

        // Node index 270 is where the misframed reader reported "Unknown command node type 3".
        CommandNodeData dimension = packet.Tree[270];
        Assert.Equal(CommandNodeKind.Argument, dimension.Kind);
        Assert.Equal("dimension", dimension.Name);
        Assert.Equal(41, dimension.Argument!.ParserId);
        Assert.Equal("minecraft:dimension", dimension.Argument.ParserName);
    }

    [Fact]
    public void OperatorTree_769_ParsesTheTemplateMirrorNodeTheWrongTableDerailedOn()
    {
        BoundPacketCodec bound = BoundCodec.At(769, PacketFlow.Clientbound, Commands);
        var packet = (ClientboundCommandsPacket)bound.DecodeFrame(LoadFrame(769, "op"));

        // 766-769 share one table; 770 inserts minecraft:resource_selector at id 47, so on 768/769 the 770 table reads a length-prefixed registry string where the payload-free template_mirror sits.
        CommandNodeData mirror = packet.Tree[979];
        Assert.Equal("mirror", mirror.Name);
        Assert.Equal(47, mirror.Argument!.ParserId);
        Assert.Equal("minecraft:template_mirror", mirror.Argument.ParserName);
        Assert.Equal(ArgumentParserProperties.Empty, mirror.Argument.Properties);
    }

    [Theory]
    [InlineData(759, 420)]
    [InlineData(760, 420)]
    [InlineData(761, 429)]
    public void MinecraftTime_CarriesNoPayload_Before762(int protocol, int nodeIndex)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, Commands);
        var packet = (ClientboundCommandsPacket)bound.DecodeFrame(LoadFrame(protocol, "op"));

        CommandNodeData time = packet.Tree[nodeIndex];
        Assert.Equal("time", time.Name);
        Assert.Equal("minecraft:time", time.Argument!.ParserName);

        // The 1.19 time argument has no property payload. Reading the int here consumes four bytes the server never sent.
        Assert.Equal(ArgumentParserProperties.Empty, time.Argument.Properties);
        Assert.All(
            AllTimeNodes(packet),
            node => Assert.Equal(ArgumentParserProperties.Empty, node.Argument!.Properties));
    }

    [Theory]
    [InlineData(762, 457, 0)]
    [InlineData(763, 458, 0)]
    [InlineData(768, 495, 1)]
    [InlineData(769, 495, 1)]
    public void MinecraftTime_CarriesItsIntMin_From762(int protocol, int nodeIndex, int expectedMin)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, Commands);
        var packet = (ClientboundCommandsPacket)bound.DecodeFrame(LoadFrame(protocol, "op"));

        CommandNodeData time = packet.Tree[nodeIndex];
        Assert.Equal("minecraft:time", time.Argument!.ParserName);
        var props = Assert.IsType<TimeArgumentProperties>(time.Argument.Properties);
        Assert.Equal(expectedMin, props.Min);

        // From 1.20.4 every time node carries the minimum field.
        Assert.All(AllTimeNodes(packet), node => Assert.IsType<TimeArgumentProperties>(node.Argument!.Properties));
    }

    [Fact]
    public void OperatorTree_760_CarriesRedirectsAndSuggestionProviders()
    {
        BoundPacketCodec bound = BoundCodec.At(760, PacketFlow.Clientbound, Commands);
        var packet = (ClientboundCommandsPacket)bound.DecodeFrame(LoadFrame(760, "op"));

        // /tell and /w both redirect onto /msg's node, and 47 nodes redirect in total.
        CommandNodeData tell = packet.Tree[31];
        Assert.Equal("tell", tell.Name);
        Assert.True(tell.HasRedirect);
        Assert.Equal(30, tell.RedirectIndex);
        Assert.Equal(47, packet.Tree.Nodes.Count(n => n.HasRedirect));

        // Suggestion-provider ids only appear when flag bit 4 is set; 118 nodes carry one.
        CommandNodeData summonable = packet.Tree[201];
        Assert.True(summonable.HasCustomSuggestions);
        Assert.Equal("minecraft:summonable_entities", summonable.Argument!.SuggestionProvider!.Value.ToString());
        Assert.Equal(118, packet.Tree.Nodes.Count(n => n.HasCustomSuggestions));
    }

    /// <summary>Cross-era rejection: an operator frame decoded through a NEIGHBOURING era's codec must not be accepted. Each pair below straddles a parser-id table change, making the wrong table observable.</summary>
    [Theory]
    [InlineData(760, 770)]
    [InlineData(760, 764)]
    [InlineData(759, 761)]
    [InlineData(761, 760)]
    [InlineData(762, 761)]
    [InlineData(762, 765)]
    [InlineData(768, 770)]
    [InlineData(769, 770)]
    public void OperatorFrame_UnderANeighbouringWireLayoutCodec_IsRejected(int frameProtocol, int decodeProtocol)
    {
        byte[] frame = LoadFrame(frameProtocol, "op");
        BoundPacketCodec bound = BoundCodec.At(decodeProtocol, PacketFlow.Clientbound, Commands);
        Assert.ThrowsAny<Exception>(() => bound.DecodeFrame(frame));
    }

    /// <summary>The in-code parser tables must equal the committed dataset for every protocol the timeline binds them to. A binding must not span two different <c>minecraft:command_argument_type</c> registries.</summary>
    [Theory]
    [InlineData(759)]
    [InlineData(760)]
    [InlineData(761)]
    [InlineData(762)]
    [InlineData(763)]
    [InlineData(764)]
    [InlineData(765)]
    [InlineData(766)]
    [InlineData(767)]
    [InlineData(768)]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(771)]
    [InlineData(772)]
    [InlineData(773)]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    public void BoundArgumentTable_MatchesCommittedDataset(int protocol)
    {
        IReadOnlyList<string> expected = DatasetArgumentTypes(protocol);
        ArgumentTypeRegistry registry = RegistryFor(protocol);

        Assert.Equal(expected.Count, registry.Count);
        for (int id = 0; id < expected.Count; id++)
            Assert.Equal(expected[id], registry.NameFromId(id));

    }

    private static IEnumerable<CommandNodeData> AllTimeNodes(ClientboundCommandsPacket packet) =>
        packet.Tree.Nodes.Where(n => n.Argument?.ParserName == "minecraft:time");

    private static string? ParserOf(ClientboundCommandsPacket packet, string nodeName) =>
        packet.Tree.Nodes.First(n => n.Name == nodeName && n.Argument is not null).Argument!.ParserName;

    private static ArgumentTypeRegistry RegistryFor(int protocol) => protocol switch
    {
        759 or 760 => ArgumentTypeRegistry.V759,
        761 => ArgumentTypeRegistry.V761,
        762 or 763 or 764 => ArgumentTypeRegistry.V764,
        765 => ArgumentTypeRegistry.V765,
        766 or 767 or 768 or 769 => ArgumentTypeRegistry.V766,
        770 => ArgumentTypeRegistry.V1_21_5,
        776 => ArgumentTypeRegistry.V26_2,
        _ => ArgumentTypeRegistry.V1_21_6,
    };

    private static byte[] LoadFrame(int protocol, string kind) =>
        File.ReadAllBytes(
            Path.Combine(RepoRoot(), "fixtures", "commands",
                protocol.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"declare-commands-{kind}.bin"));

    private static IReadOnlyList<string> DatasetArgumentTypes(int protocol)
    {
        string path = Path.Combine(RepoRoot(), "data", "java",
            protocol.ToString(System.Globalization.CultureInfo.InvariantCulture), "argument_types.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        var byId = new SortedDictionary<int, string>();
        foreach (JsonElement entry in document.RootElement.GetProperty("entries").EnumerateArray())
            byId.Add(entry.GetProperty("id").GetInt32(), entry.GetProperty("name").GetString()!);

        Assert.Equal(Enumerable.Range(0, byId.Count), byId.Keys);
        return [.. byId.Values];
    }

    private static string RepoRoot()
    {
        DirectoryInfo? cursor = new(AppContext.BaseDirectory);
        while (cursor is not null && !Directory.Exists(Path.Combine(cursor.FullName, "fixtures", "commands")))
            cursor = cursor.Parent;

        return cursor?.FullName ?? throw new DirectoryNotFoundException(
            "could not locate fixtures/commands from the test output directory");
    }
}
