using Umpk.Client.Commands;
using Umpk.Commands;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests.Commands;

/// <summary>Unit tests for <see cref="ServerCommandTree"/> reconstruction, local completion, ask-server detection, and signed-argument span extraction over synthetic trees.</summary>
public sealed class ServerCommandTreeTests
{
    private static CommandNodeData Root(params int[] children) =>
        new(CommandNodeKind.Root, 0x00, [.. children], -1, null, null);

    private static CommandNodeData Literal(string name, bool executable = false, int[]? children = null, int redirect = -1)
    {
        byte flags = (byte)(0x01 | (executable ? 0x04 : 0) | (redirect >= 0 ? 0x08 : 0));
        return new CommandNodeData(CommandNodeKind.Literal, flags, [.. (children ?? [])], redirect, name, null);
    }

    private static CommandNodeData Argument(string name, string parser, int[]? children = null, Identifier? suggestion = null, bool executable = false)
    {
        byte flags = (byte)(0x02 | (executable ? 0x04 : 0) | (suggestion is not null ? 0x10 : 0));
        var arg = new CommandArgumentData(0, parser, ArgumentParserProperties.Empty, suggestion);
        return new CommandNodeData(CommandNodeKind.Argument, flags, [.. (children ?? [])], -1, name, arg);
    }

    private static ServerCommandTree BuildMsgLikeTree()
    {
        // root -> "msg" -> targets(entity) -> message(message, executable) -> "help"(executable) -> "seed"(executable)
        var wire = new CommandTreeData(
        [
            Root(1, 4, 5),
            Literal("msg", children: [2]),
            Argument("targets", "minecraft:entity", children: [3]),
            Argument("message", "minecraft:message", executable: true),
            Literal("help", executable: true),
            Literal("seed", executable: true),
        ], 0);

        return ServerCommandTree.Build(wire, ArgumentTypeRegistry.V1_21_5);
    }

    [Fact]
    public void Build_WiresChildrenAndKinds()
    {
        ServerCommandTree tree = BuildMsgLikeTree();
        Assert.Equal(CommandNodeKind.Root, tree.Root.Kind);
        Assert.Equal(3, tree.Root.Children.Count);
        CommandNodeView msg = tree.Root.Children.First(c => c.Name == "msg");
        Assert.Equal("targets", msg.Children[0].Name);
        Assert.Equal("minecraft:entity", msg.Children[0].ParserName);
    }

    [Fact]
    public void Build_ResolvesRedirect_CycleSafe()
    {
        // root -> "execute" -> redirect back to root.
        var wire = new CommandTreeData([Root(1), Literal("execute", redirect: 0)], 0);
        ServerCommandTree tree = ServerCommandTree.Build(wire, ArgumentTypeRegistry.V1_21_5);
        CommandNodeView execute = tree.Root.Children[0];
        Assert.Same(tree.Root, execute.Redirect);
        // EffectiveChildren of the redirect node fall through to the target's children.
        Assert.Equal(tree.Root.Children.Count, execute.EffectiveChildren.Count);
    }

    [Fact]
    public void CompleteLocally_SuggestsMatchingLiteralPrefix()
    {
        ServerCommandTree tree = BuildMsgLikeTree();
        // Cursor after "se" should suggest "seed".
        CompletionResult result = tree.CompleteLocally("se", 2);
        Assert.Contains(result.Suggestions, s => s.Text == "seed");
        Assert.DoesNotContain(result.Suggestions, s => s.Text == "msg");
        Assert.Equal(0, result.RangeStart);
        Assert.Equal(2, result.RangeEnd);
    }

    [Fact]
    public void CompleteLocally_EmptyPrefix_ListsAllRootLiterals()
    {
        ServerCommandTree tree = BuildMsgLikeTree();
        CompletionResult result = tree.CompleteLocally(string.Empty, 0);
        Assert.Contains(result.Suggestions, s => s.Text == "msg");
        Assert.Contains(result.Suggestions, s => s.Text == "help");
        Assert.Contains(result.Suggestions, s => s.Text == "seed");
    }

    [Fact]
    public void NeedsServerCompletion_TrueForEntityArgument()
    {
        ServerCommandTree tree = BuildMsgLikeTree();
        // After "msg " the next token is the entity-target argument, which needs the server.
        Assert.True(tree.NeedsServerCompletion("msg ", 4));
    }

    [Fact]
    public void NeedsServerCompletion_FalseForLiteralFrontier()
    {
        ServerCommandTree tree = BuildMsgLikeTree();
        Assert.False(tree.NeedsServerCompletion("he", 2));
    }

    [Fact]
    public void NeedsServerCompletion_TrueForCustomSuggestionProvider()
    {
        var wire = new CommandTreeData(
        [
            Root(1),
            Literal("summon", children: [2]),
            Argument("entity", "minecraft:resource_location", suggestion: Identifier.Parse("minecraft:summonable_entities")),
        ], 0);
        ServerCommandTree tree = ServerCommandTree.Build(wire, ArgumentTypeRegistry.V1_21_5);
        Assert.True(tree.NeedsServerCompletion("summon ", 7));
    }

    [Fact]
    public void GetSignedArguments_ReturnsMessageSpan()
    {
        ServerCommandTree tree = BuildMsgLikeTree();
        IReadOnlyList<SignedArgumentSpan> spans = tree.GetSignedArguments("msg Steve hello world");
        Assert.Single(spans);
        SignedArgumentSpan span = spans[0];
        Assert.Equal("message", span.Name);
        Assert.Equal("hello world", span.Value);
        Assert.Equal("msg Steve ".Length, span.Start);
        Assert.Equal("hello world".Length, span.Length);
    }

    [Fact]
    public void GetSignedArguments_Empty_WhenNoMessageArgument()
    {
        var wire = new CommandTreeData(
        [
            Root(1),
            Literal("seed", executable: true),
        ], 0);
        ServerCommandTree tree = ServerCommandTree.Build(wire, ArgumentTypeRegistry.V1_21_5);
        Assert.Empty(tree.GetSignedArguments("seed"));
    }

    [Fact]
    public void GetSignedArguments_NonEntityTargetNotSigned()
    {
        // Only minecraft:message is a signed argument; the entity target is not.
        ServerCommandTree tree = BuildMsgLikeTree();
        IReadOnlyList<SignedArgumentSpan> spans = tree.GetSignedArguments("msg Steve hi");
        Assert.DoesNotContain(spans, s => s.Name == "targets");
    }

    [Fact]
    public async Task AdversarialRedirectCycle_WalksTerminate_NoHangOrOverflow()
    {
        // Adversarial payload: a mutual redirect cycle ("a" -> "b" -> "a") plus a deeply repeated input must not send any tree walk into an infinite loop or stack overflow. Build resolves redirects by index (one hop), and every walk advances its token position each iteration and is bounded by the input length, so all three walks must terminate.
        var wire = new CommandTreeData(
        [
            Root(1, 2),
            Literal("a", redirect: 2),   // a -> b
            Literal("b", redirect: 1),   // b -> a (cycle)
        ], 0);
        ServerCommandTree tree = ServerCommandTree.Build(wire, ArgumentTypeRegistry.V1_21_5);

        // A long "a b a b..." input that would loop forever if the walk followed redirects unbounded.
        string deep = string.Join(' ', Enumerable.Repeat("a b", 500));
        int cursor = deep.Length;

        // Run the walks under a bounded timeout so a hang fails the test instead of wedging the suite.
        Task walk = Task.Run(() =>
        {
            _ = tree.CompleteLocally(deep, cursor);
            _ = tree.NeedsServerCompletion(deep, cursor);
            _ = tree.GetSignedArguments(deep);
        });

        Task first = await Task.WhenAny(walk, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(walk, first); // the walk finished before the timeout
        await walk;               // observe any exception the walk threw
    }
}
