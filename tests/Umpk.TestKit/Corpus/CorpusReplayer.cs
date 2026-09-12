using Microsoft.Extensions.Logging;
using Umpk.Protocol.Java;
using Umpk.TestKit.Server;

namespace Umpk.TestKit.Corpus;

/// <summary>Replays a loaded corpus's clientbound frames to a client over a <see cref="FakeJavaServer"/> and re-records what the client observes, so a test can prove the record path is idempotent: the re-recorded frames' content hash equals the original's. This closes the "re-recording a played-back corpus is idempotent" requirement without a live server.</summary>
public static class CorpusReplayer
{
    /// <summary>Streams the corpus's clientbound frames through a real <see cref="JavaConnection"/> on the client side and returns the frames a fresh <see cref="FrameRecorder"/> captured. Only clientbound frames are replayed (a client-side capture observes only clientbound traffic), so the comparison is made against the original's clientbound subset.</summary>
    public static async Task<IReadOnlyList<RecordedFrame>> ReRecordClientboundAsync(
        LoadedCorpus corpus, ILogger? logger = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(corpus);

        await using FakeJavaServer server = FakeJavaServer.Create(logger);
        var clientOptions = new JavaConnectionOptions
        {
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
            ReadIdleTimeout = TimeSpan.Zero,
        };
        await using var client = new JavaConnection(server.ClientPipe, clientOptions);
        client.BindCodec(null, PacketFlow.Clientbound);
        client.SetDecodeFilter(PacketDecodeFilter.None);

        using var recorder = new FrameRecorder();
        recorder.Attach(client);
        client.Start();

        // Drain client frames as the server sends, so the read loop keeps observing.
        var received = new List<int>();
        Task drain = Task.Run(async () =>
        {
            try
            {
                await foreach (InboundFrame f in client.ReceiveFramesAsync(ct).ConfigureAwait(false))
                    received.Add(f.WireId);

            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
        }, ct);

        int expectedClientbound = 0;
        foreach (RecordedFrame frame in corpus.Frames)
        {
            if (frame.Direction != CorpusDirection.Clientbound)
                continue;

            expectedClientbound++;
            await server.SendFrameAsync(frame.WireId, frame.Body, ct).ConfigureAwait(false);
        }

        // Wait until every clientbound frame has been observed by the recorder.
        await WaitForCountAsync(() => recorder.Count, expectedClientbound, ct).ConfigureAwait(false);

        await client.CloseAsync(CloseReason.Local, ct).ConfigureAwait(false);
        try
        {
            await drain.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // ignore
        }

        return recorder.Snapshot();
    }

    private static async Task WaitForCountAsync(Func<int> current, int target, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (current() < target)
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(2, timeout.Token).ConfigureAwait(false);
        }
    }
}
