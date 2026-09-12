using Umpk.Client.Commands;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit.Corpus;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Client.Tests.Commands;

/// <summary>Corpus reconstruction tests: decode the real <c>minecraft:commands</c> frame from the vanilla-server login-config captures, reconstruct the <see cref="ServerCommandTree"/>, and assert node counts plus that known vanilla commands resolve with their expected argument chains. Runs only over the protocols with a matching argument-type registry: 770 (V1_21_5), 771 (V1_21_6), and 776 (V26_2). Older login-config corpora exist but have no matching registry, so resolving their command trees against the V1_21_5 table would be meaningless and they are excluded here.</summary>
public sealed class ServerCommandTreeCorpusTests
{
    private readonly ITestOutputHelper _output;

    public ServerCommandTreeCorpusTests(ITestOutputHelper output) => _output = output;

    // Protocols whose command-argument identifiers map to a real ArgumentTypeRegistry (see the registry
    // switch in the tests below). Restricting MemberData to these avoids resolving a 764-769 command tree
    // against the wrong (V1_21_5) table.
    private static readonly HashSet<int> RegistryBackedProtocols = [770, 771, 776];

    public static IEnumerable<object[]> LoginConfigCaptures()
    {
        string root = FindCorpusRoot();
        if (root is null)
            yield break;

        foreach (string path in CorpusLoader.DiscoverCaptures(root))
        {
            if (!Path.GetFileName(path).StartsWith("login-config", StringComparison.Ordinal))
                continue;

            // Gate the committed login/configuration capture on its protocol.
            string? protocolDir = Path.GetFileName(Path.GetDirectoryName(path));
            if (int.TryParse(protocolDir, out int protocol) && RegistryBackedProtocols.Contains(protocol))
                yield return [path];

        }
    }

    [Theory]
    [MemberData(nameof(LoginConfigCaptures))]
    public async Task Corpus_DeclareCommands_Reconstructs_And_ResolvesKnownCommands(string capturePath)
    {
        LoadedCorpus corpus = await CorpusLoader.LoadFileAsync(capturePath);
        Assert.True(JavaVersions.TryGetByProtocol(corpus.Protocol, out JavaVersion? version));
        ProtocolDescriptor descriptor = version!.Protocol;

        ClientboundCommandsPacket? commands = DecodeCommandsFrame(corpus, descriptor);
        Assert.NotNull(commands);

        ArgumentTypeRegistry registry = corpus.Protocol >= 776 ? ArgumentTypeRegistry.V26_2
            : corpus.Protocol >= 771 ? ArgumentTypeRegistry.V1_21_6 // 1.21.6 inserted hex_color/dialog
            : ArgumentTypeRegistry.V1_21_5;
        ServerCommandTree tree = ServerCommandTree.Build(commands!.Tree, registry);

        // Reconstruction preserved the wire node count and produced a root node with the server's command set. This capture is from a minimal offline dev server whose vanilla command set is {me, help, list, msg, tell, w, random, teammsg, tm, trigger}.
        Assert.Equal(commands.Tree.Nodes.Length, tree.Nodes.Count);
        Assert.Equal(CommandNodeKind.Root, tree.Root.Kind);
        Assert.True(tree.Root.Children.Count >= 10,
            $"expected the server's root command set, got {tree.Root.Children.Count}");

        int argumentNodes = tree.Nodes.Count(n => n.Kind == CommandNodeKind.Argument);
        int literalNodes = tree.Nodes.Count(n => n.Kind == CommandNodeKind.Literal);
        int redirects = tree.Nodes.Count(n => n.Redirect is not null);
        _output.WriteLine(
            $"{Path.GetFileName(capturePath)} (protocol {corpus.Protocol}): {tree.Nodes.Count} nodes " +
            $"({literalNodes} literal, {argumentNodes} argument, {tree.Root.Children.Count} root commands, {redirects} redirects).");

        // /msg resolves with a targets (entity) argument then a signed message argument.
        CommandNodeView? msg = FindRootLiteral(tree, "msg");
        Assert.NotNull(msg);
        CommandNodeView? msgTargets = msg!.EffectiveChildren.FirstOrDefault(c => c.Kind == CommandNodeKind.Argument);
        Assert.NotNull(msgTargets);
        Assert.Equal("targets", msgTargets!.Name);
        Assert.Equal("minecraft:entity", msgTargets.ParserName);
        Assert.Contains(msgTargets.EffectiveChildren, c => c.Name == "message" && c.ParserName == "minecraft:message");

        // /trigger resolves with an objective argument whose children are the literals add/set.
        CommandNodeView? trigger = FindRootLiteral(tree, "trigger");
        Assert.NotNull(trigger);
        CommandNodeView? objective = trigger!.EffectiveChildren.FirstOrDefault(c => c.Kind == CommandNodeKind.Argument);
        Assert.NotNull(objective);
        Assert.Equal("minecraft:objective", objective!.ParserName);
        Assert.Contains(objective.EffectiveChildren, c => c.Kind == CommandNodeKind.Literal && c.Name == "add");
        Assert.Contains(objective.EffectiveChildren, c => c.Kind == CommandNodeKind.Literal && c.Name == "set");

        // /random resolves with the literal subcommands value/roll.
        CommandNodeView? random = FindRootLiteral(tree, "random");
        Assert.NotNull(random);
        Assert.Contains(random!.EffectiveChildren, c => c.Kind == CommandNodeKind.Literal && c.Name == "value");
        Assert.Contains(random.EffectiveChildren, c => c.Kind == CommandNodeKind.Literal && c.Name == "roll");

        // /list is directly executable and has a "uuids" literal child.
        CommandNodeView? list = FindRootLiteral(tree, "list");
        Assert.NotNull(list);
        Assert.True(list!.IsExecutable);
        Assert.Contains(list.EffectiveChildren, c => c.Kind == CommandNodeKind.Literal && c.Name == "uuids");
    }

    [Theory]
    [MemberData(nameof(LoginConfigCaptures))]
    public async Task Corpus_DeclareCommands_MsgMessageArgument_IsSigned(string capturePath)
    {
        LoadedCorpus corpus = await CorpusLoader.LoadFileAsync(capturePath);
        Assert.True(JavaVersions.TryGetByProtocol(corpus.Protocol, out JavaVersion? version));
        ClientboundCommandsPacket? commands = DecodeCommandsFrame(corpus, version!.Protocol);
        Assert.NotNull(commands);

        ArgumentTypeRegistry registry = corpus.Protocol >= 776 ? ArgumentTypeRegistry.V26_2
            : corpus.Protocol >= 771 ? ArgumentTypeRegistry.V1_21_6 // 1.21.6 inserted hex_color/dialog
            : ArgumentTypeRegistry.V1_21_5;
        ServerCommandTree tree = ServerCommandTree.Build(commands!.Tree, registry);

        // /msg <target> <message>: the message argument is signable; the target is not.
        IReadOnlyList<SignedArgumentSpan> spans = tree.GetSignedArguments("msg Steve hello there");
        Assert.Single(spans);
        Assert.Equal("hello there", spans[0].Value);
        Assert.Equal("message", spans[0].Name);
    }

    private static ClientboundCommandsPacket? DecodeCommandsFrame(LoadedCorpus corpus, ProtocolDescriptor descriptor)
    {
        Assert.True(descriptor.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry registry));
        foreach (RecordedFrame frame in corpus.Frames)
        {
            if (frame.Direction != CorpusDirection.Clientbound || frame.Phase != CorpusPhase.Play)
                continue;

            if (!registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec) || !codec.IsImplemented)
                continue;

            if (codec.Type.Id != CommandsPackets.Clientbound.Commands.Id)
                continue;

            return (ClientboundCommandsPacket)codec.Decode(frame.Body, PacketCodecContext.Registryless);
        }

        return null;
    }

    private static CommandNodeView? FindRootLiteral(ServerCommandTree tree, string name) =>
        tree.Root.EffectiveChildren.FirstOrDefault(
            c => c.Kind == CommandNodeKind.Literal && string.Equals(c.Name, name, StringComparison.Ordinal));

    private static string FindCorpusRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "fixtures", "corpus");
            if (Directory.Exists(candidate))
                return candidate;

            dir = Path.GetDirectoryName(dir);
        }

        return null!;
    }
}
