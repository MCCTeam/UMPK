using System.Collections.Immutable;
using Umpk.Client;
using Umpk.Client.Commands;
using Umpk.Commands;
using Umpk.Data.Java;
using Umpk.Game.Players;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests.Commands;

/// <summary>Completion-merge tests: the client's <c>Actions.Chat.CompleteAsync</c> unions host-command suggestions (from the <see cref="CommandService{TSource}"/>) with the local server-command-tree walk (literals). Exercised against a built (unconnected) client with a host command registered and a synthetic server command tree installed.</summary>
public sealed class ChatCompletionMergeTests
{
    private static UmpkClient BuildClient() => new UmpkClientBuilder()
        .UseVersion(JavaVersions.V1_21_5)
        .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
        .Build();

    private static ServerCommandTree ServerTree()
    {
        // root -> "seed"(exec), "help"(exec)
        var wire = new CommandTreeData(
        [
            new CommandNodeData(CommandNodeKind.Root, 0x00, [1, 2], -1, null, null),
            new CommandNodeData(CommandNodeKind.Literal, 0x05, ImmutableArray<int>.Empty, -1, "seed", null),
            new CommandNodeData(CommandNodeKind.Literal, 0x05, ImmutableArray<int>.Empty, -1, "help", null),
        ], 0);
        return ServerCommandTree.Build(wire, ArgumentTypeRegistry.V1_21_5);
    }

    [Fact]
    public async Task CompleteAsync_MergesHostAndServerLiteralCompletions()
    {
        await using UmpkClient client = BuildClient();

        // Register a host command "sethome".
        using ICommandRegistrationScope<ClientCommandSource> scope = client.Commands.CreateScope("test");
        scope.Register(b => b.Literal("sethome", l => l.Executes(_ => ValueTask.FromResult(1))));

        // Install a synthetic server tree with "seed" and "help".
        client.State.ServerCommands.Update(ServerTree());

        // Complete "/se": host contributes nothing (sethome does not start with "se" after the slash), the server tree contributes "seed".
        CompletionResult result = await client.Actions.Chat.CompleteAsync("/se", 3);
        Assert.Contains(result.Suggestions, s => s.Text == "seed");
    }

    [Fact]
    public async Task CompleteAsync_HostCommandSuggested()
    {
        await using UmpkClient client = BuildClient();

        using ICommandRegistrationScope<ClientCommandSource> scope = client.Commands.CreateScope("test");
        scope.Register(b => b.Literal("sethome", l => l.Executes(_ => ValueTask.FromResult(1))));
        client.State.ServerCommands.Update(ServerTree());

        // Complete "sethom" (no slash) against host commands: the host command service matches "sethome".
        CompletionResult result = await client.Actions.Chat.CompleteAsync("sethom", 6);
        Assert.Contains(result.Suggestions, s => s.Text == "sethome");
    }

    [Fact]
    public async Task CompleteAsync_ServerDrivenArgument_FailsSoftToLocalWhenNotConnected()
    {
        await using UmpkClient client = BuildClient();

        // root -> "msg" -> "targets"(minecraft:entity, ask-server) -> "message"(minecraft:message).
        var wire = new CommandTreeData(
        [
            new CommandNodeData(CommandNodeKind.Root, 0x00, [1], -1, null, null),
            new CommandNodeData(CommandNodeKind.Literal, 0x01, [2], -1, "msg", null),
            new CommandNodeData(CommandNodeKind.Argument, 0x02, [3], -1, "targets",
                new CommandArgumentData(6, "minecraft:entity",
                    new EntityArgumentProperties(false, false), null)),
            new CommandNodeData(CommandNodeKind.Argument, 0x06, ImmutableArray<int>.Empty, -1, "message",
                new CommandArgumentData(0, "minecraft:message", ArgumentParserProperties.Empty, null)),
        ], 0);
        client.State.ServerCommands.Update(ServerCommandTree.Build(wire, ArgumentTypeRegistry.V1_21_5));

        // Completing the entity target ("/msg St") is server-driven: NeedsServerCompletion is true, so CompleteAsync attempts the tab-complete round trip. The client is not connected, so that round trip throws; the merge swallows it and returns whatever local completions exist (here: none for the entity token). The call must not propagate the connection failure.
        CompletionResult result = await client.Actions.Chat.CompleteAsync("/msg St", 7);
        Assert.Empty(result.Suggestions);
    }

    [Fact]
    public async Task GetSignedArguments_UsesInstalledServerTree()
    {
        await using UmpkClient client = BuildClient();

        var wire = new CommandTreeData(
        [
            new CommandNodeData(CommandNodeKind.Root, 0x00, [1], -1, null, null),
            new CommandNodeData(CommandNodeKind.Literal, 0x01, [2], -1, "say", null),
            new CommandNodeData(CommandNodeKind.Argument, 0x06,
                ImmutableArray<int>.Empty, -1, "message",
                new CommandArgumentData(0, "minecraft:message", ArgumentParserProperties.Empty, null)),
        ], 0);
        client.State.ServerCommands.Update(ServerCommandTree.Build(wire, ArgumentTypeRegistry.V1_21_5));

        IReadOnlyList<SignedArgumentSpan> spans = client.Actions.Chat.GetSignedArguments("say hello world");
        Assert.Single(spans);
        Assert.Equal("hello world", spans[0].Value);
    }
}
