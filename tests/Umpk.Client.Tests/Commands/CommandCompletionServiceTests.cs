using System.Collections.Immutable;
using Umpk.Client.Commands;
using Umpk.Client.Tests.Support;
using Umpk.Commands;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests.Commands;

/// <summary>Tests the server tab-complete round trip correlator: a request allocates a transaction id and sends a <c>command_suggestion</c> packet; the matching <c>command_suggestions</c> response resolves the pending request. A response with a mismatched transaction id is ignored.</summary>
public sealed class CommandCompletionServiceTests
{
    [Fact]
    public async Task RequestAsync_CorrelatesResponseByTransactionId()
    {
        var sink = new RecordingSink();
        var service = new CommandCompletionService(sink);

        Task<CompletionResult> pending = service.RequestAsync("/msg Ste", CancellationToken.None);

        // The request was sent; extract its transaction id.
        var request = Assert.IsType<ServerboundCommandSuggestionPacket>(Assert.Single(sink.Packets));
        Assert.Equal("/msg Ste", request.Command);

        // Feed the matching response.
        var response = new ClientboundCommandSuggestionsPacket(
            request.TransactionId, 5, 3,
            [new CommandSuggestion("Steve", Component.Text("player")), new CommandSuggestion("Stella", null)]);
        service.Complete(response);

        CompletionResult result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(5, result.RangeStart);
        Assert.Equal(8, result.RangeEnd);
        Assert.Collection(result.Suggestions,
            s => Assert.Equal("Steve", s.Text),
            s => Assert.Equal("Stella", s.Text));
    }

    [Fact]
    public async Task Complete_WithUnknownTransaction_IsIgnored()
    {
        var sink = new RecordingSink();
        var service = new CommandCompletionService(sink);

        Task<CompletionResult> pending = service.RequestAsync("/x", CancellationToken.None);
        var request = Assert.IsType<ServerboundCommandSuggestionPacket>(Assert.Single(sink.Packets));

        // A response for a different transaction must not resolve the pending request.
        service.Complete(new ClientboundCommandSuggestionsPacket(
            request.TransactionId + 999, 0, 0, ImmutableArray<CommandSuggestion>.Empty.ToArray()));
        Assert.False(pending.IsCompleted);

        // The correct one resolves it.
        service.Complete(new ClientboundCommandSuggestionsPacket(
            request.TransactionId, 0, 0, ImmutableArray<CommandSuggestion>.Empty.ToArray()));
        CompletionResult result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(result.Suggestions);
    }
}
