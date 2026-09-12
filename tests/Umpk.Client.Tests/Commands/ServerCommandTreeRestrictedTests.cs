using System.Collections.Immutable;
using Umpk.Client.Commands;
using Umpk.Commands;
using Umpk.Data.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests.Commands;

/// <summary>The RESTRICTED command node (<c>FLAG_RESTRICTED = 32</c>, 1.21.6+) and completion.</summary>
/// <remarks>The flag belongs to the node after permission filtering, so an operator can receive a command such as <c>gamemode</c> with the flag set. Completion must traverse restricted nodes for users whose filtered tree contains them.</remarks>
public sealed class ServerCommandTreeRestrictedTests
{
    private const byte Root = 0x00;
    private const byte Literal = 0x01;
    private const byte Argument = 0x02;
    private const byte Executable = 0x04;
    private const byte Restricted = 0x20;

    /// <summary>root -> "gamemode"(restricted) -> "gamemode"(argument), plus "help"(unrestricted).</summary>
    private static ServerCommandTree OppedTree()
    {
        var wire = new CommandTreeData(
        [
            new CommandNodeData(CommandNodeKind.Root, Root, [1, 3], -1, null, null),
            new CommandNodeData(CommandNodeKind.Literal, Literal | Restricted, [2], -1, "gamemode", null),
            new CommandNodeData(
                CommandNodeKind.Argument, Argument | Executable | Restricted, ImmutableArray<int>.Empty, -1, "target",
                new CommandArgumentData(6, "minecraft:entity", null, null)),
            new CommandNodeData(CommandNodeKind.Literal, Literal | Executable, ImmutableArray<int>.Empty, -1, "help", null),
        ], 0);
        return ServerCommandTree.Build(wire, ArgumentTypeRegistry.V1_21_5);
    }

    [Fact]
    public void RestrictedFlag_IsDecodedFromTheWire()
    {
        ServerCommandTree tree = OppedTree();
        CommandNodeView gamemode = Assert.Single(tree.Root.EffectiveChildren, c => c.Name == "gamemode");
        CommandNodeView help = Assert.Single(tree.Root.EffectiveChildren, c => c.Name == "help");

        Assert.True(gamemode.IsRestricted);
        Assert.False(help.IsRestricted);
    }

    [Fact]
    public void CompleteLocally_OffersARestrictedLiteral()
    {
        CompletionResult result = OppedTree().CompleteLocally("gamemo", 6);

        Assert.Contains(result.Suggestions, s => s.Text == "gamemode");
        Assert.Equal(0, result.RangeStart);
        Assert.Equal(6, result.RangeEnd);
    }

    [Fact]
    public void NeedsServerCompletion_TrueUnderARestrictedArgument()
    {
        // "gamemode " with the cursor past the space: the frontier is the restricted literal and its only child is a restricted, server-driven argument. Skipping restricted nodes here made the round trip never happen, which reads exactly like a server that answered nothing.
        Assert.True(OppedTree().NeedsServerCompletion("gamemode ", 9));
    }

    /// <summary>The merged completion entry point offers a restricted command when the server permits it.</summary>
    [Fact]
    public async Task CompleteAsync_OffersARestrictedCommand()
    {
        await using UmpkClient client = new UmpkClientBuilder()
            .UseVersion(JavaVersions.V26_1)
            .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
            .Build();

        client.State.ServerCommands.Update(OppedTree());

        CompletionResult result = await client.Actions.Chat.CompleteAsync("/gamemo", 7);

        Assert.Contains(result.Suggestions, s => s.Text == "gamemode");
    }
}
