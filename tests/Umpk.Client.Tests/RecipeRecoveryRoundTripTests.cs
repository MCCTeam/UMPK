using System.Buffers;
using Umpk.Client.Tests.Support;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit.Server;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class RecipeRecoveryRoundTripTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task UnmodeledCompactComponent_DropsWholeReplacement_AndNextFrameApplies()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = ScriptedServer.BuildClient(server);

        Task drive = ScriptedServer.DriveToPlayAsync(server, ct);
        await Task.WhenAll(
            client.ConnectAsync(new ServerEndpoint("test", 25565), ct),
            drive).WaitAsync(Budget, ct);

        var seed = new RecipeBookEntry(
            DisplayId: 7,
            RecipeDisplayKind.Stonecutter,
            ResultItemId: -1,
            ResultId: Identifier.Minecraft("stone"),
            ResultCount: 1,
            Group: null,
            Category: 0,
            Notification: false,
            Highlight: false,
            Display: new RecipeDisplay.Stonecutter(
                SlotDisplay.Empty.Instance,
                SlotDisplay.Empty.Instance,
                SlotDisplay.Empty.Instance),
            CraftingRequirements: null);
        await client.InvokeAsync(c =>
        {
            c.State.Recipes.ApplyBookAdd([seed], 0, replace: true);
            return true;
        }, ct);
        int revision = await client.InvokeAsync(c => c.State.Recipes.BookRevision, ct);

        PhaseRegistry play = ScriptedServer.Version.Protocol.GetRegistry(
            ProtocolPhase.Play, PacketFlow.Clientbound);
        int recipeId = play.Packets.Single(pair =>
            pair.Type.Id == Identifier.Minecraft("recipe_book_add")).WireId;
        await server.SendFrameAsync(recipeId, FailingReplacement(), ct);

        const long keepAliveId = 0x1122_3344_5566_7788;
        await ScriptedServer.SendAsync(
            server,
            ScriptedServer.Version.Protocol,
            ProtocolPhase.Play,
            new ClientboundPlayKeepAlivePacket(keepAliveId),
            ct);

        Assert.True(await WaitUntilAsync(
            () => client.State.Server.LastKeepAliveId == keepAliveId,
            Budget,
            ct));
        Assert.Equal(ClientStatus.Playing, client.Status);
        Assert.Equal(revision, client.State.Recipes.BookRevision);
        Assert.Equal([7], client.State.Recipes.Displays.Keys);
        Assert.Equal(0, client.State.Recipes.OpaqueAdditions);
    }

    private static byte[] FailingReplacement()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        writer.WriteVarInt(1);   // entry count
        writer.WriteVarInt(41);  // display id
        writer.WriteVarInt(3);   // stonecutter recipe display
        writer.WriteVarInt(0);   // empty input slot
        writer.WriteVarInt(3);   // item_stack result slot
        writer.WriteVarInt(1);   // stack count
        writer.WriteVarInt(936); // diamond_sword in protocol 774
        writer.WriteVarInt(1);   // one added component
        writer.WriteVarInt(0);   // no removed components
        writer.WriteVarInt(29);  // minecraft:weapon in protocol 774; recognized but unmodeled
        writer.WriteVarInt(123); // unread compact payload plus deliberately valid-looking trailing bytes
        writer.WriteVarInt(0);   // empty station
        writer.WriteVarInt(0);   // no group
        writer.WriteVarInt(0);   // category
        writer.WriteBool(false); // no crafting requirements
        writer.WriteByte(0);     // flags
        writer.WriteBool(true);  // replacement must never reach RecipeState
        return buffer.WrittenSpan.ToArray();
    }

    private static async Task<bool> WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout,
        CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(10, ct);
        }

        return condition();
    }
}
