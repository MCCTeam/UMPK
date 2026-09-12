using System.Collections.Concurrent;
using System.Collections.Immutable;
using Umpk.Client.Internal;
using Umpk.Commands;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Commands;

/// <summary>The server tab-complete round trip: correlates a <c>command_suggestion</c> request with its <c>command_suggestions</c> response by transaction id, as the vanilla client does. A completion request allocates a transaction id, sends the request, and awaits the matching response; the applier for the response packet resolves the pending request. Owned by the session; there is one per <see cref="UmpkClient"/>, so no statics.</summary>
internal sealed class CommandCompletionService
{
    private readonly IPacketSink _sink;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<CompletionResult>> _pending = new();
    private int _nextTransactionId;

    public CommandCompletionService(IPacketSink sink) => _sink = sink;

    /// <summary>Sends a server tab-complete request for the input at the cursor and awaits the response. The vanilla client sends the whole command (with the leading slash) as the request text. Returns the server's suggestions mapped to <see cref="CompletionResult"/>.</summary>
    public async Task<CompletionResult> RequestAsync(string requestText, CancellationToken ct)
    {
        int transactionId = Interlocked.Increment(ref _nextTransactionId) & 0x7FFFFFFF;
        var tcs = new TaskCompletionSource<CompletionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[transactionId] = tcs;

        try
        {
            await _sink.SendAsync(new ServerboundCommandSuggestionPacket(transactionId, requestText), ct)
                .ConfigureAwait(false);

            await using CancellationTokenRegistration reg = ct.Register(
                static state => ((TaskCompletionSource<CompletionResult>)state!).TrySetCanceled(), tcs);
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(transactionId, out _);
        }
    }

    /// <summary>Resolves the pending request that matches a server response, if any.</summary>
    public void Complete(ClientboundCommandSuggestionsPacket response)
    {
        if (!_pending.TryRemove(response.TransactionId, out TaskCompletionSource<CompletionResult>? tcs))
            return;

        var builder = ImmutableArray.CreateBuilder<CompletionSuggestion>(response.Suggestions.Count);
        int end = response.RangeStart + response.RangeLength;
        foreach (CommandSuggestion s in response.Suggestions)
        {
            // ToPlainText, not ToString: Component is a record, so ToString renders the C# object graph ("Component { Content = TextContent { Text = Nearest player, ... } }") and THAT string was what a host put in its suggestion popup. ToPlainText is the flattening that mirrors vanilla plain-text rendering, which is what a tooltip actually is.
            builder.Add(new CompletionSuggestion(s.Text, response.RangeStart, end, s.Tooltip?.ToPlainText()));
        }

        tcs.TrySetResult(new CompletionResult(response.RangeStart, end, builder.ToImmutable()));
    }
}
