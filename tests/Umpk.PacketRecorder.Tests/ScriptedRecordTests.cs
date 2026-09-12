using Umpk.Protocol.Java;
using Umpk.TestKit.Corpus;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.PacketRecorder.Tests;

/// <summary>Records a scripted <see cref="FakeJavaServer"/> exchange over a real <see cref="JavaConnection"/> and asserts the capture is lossless against the script (the recorder capture-fidelity suite). Framing and compression run for real, so a byte-identical capture proves the recorder captures the true post-transform pre-decode payload.</summary>
public sealed class ScriptedRecordTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task ScriptedClientboundExchange_RecordsLossless()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await using FakeJavaServer server = FakeJavaServer.Create();

        // Client side over the fake server's pipe, frame mode (no codec binding needed).
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

        // The scripted clientbound frames (wire id -> body). Includes a large body to cross the compression threshold once compression is enabled.
        byte[] small = [0x10, 0x20, 0x30];
        byte[] large = new byte[512];
        for (int i = 0; i < large.Length; i++)
            large[i] = (byte)(i * 7);

        var expected = new List<(int WireId, byte[] Body)>
        {
            (0x01, small),
            (0x02, large),
            (0x03, [0xFF]),
        };

        // Drain the client so the read loop keeps observing.
        var drained = new List<int>();
        Task drain = Task.Run(async () =>
        {
            await foreach (InboundFrame f in client.ReceiveFramesAsync(ct).ConfigureAwait(false))
                drained.Add(f.WireId);

        }, ct);

        // Enable compression on both ends before any frame moves (deterministic; no mid-stream race), so the large body crosses the threshold and the recorder still sees post-decompression bytes.
        server.EnableCompression(256);
        client.EnableCompression(256);
        await server.SendFrameAsync(expected[0].WireId, expected[0].Body, ct);
        await server.SendFrameAsync(expected[1].WireId, expected[1].Body, ct);
        await server.SendFrameAsync(expected[2].WireId, expected[2].Body, ct);

        await WaitForCountAsync(() => recorder.Count, expected.Count, ct);

        IReadOnlyList<RecordedFrame> recorded = recorder.Snapshot();
        Assert.Equal(expected.Count, recorded.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(CorpusDirection.Clientbound, recorded[i].Direction);
            Assert.Equal(expected[i].WireId, recorded[i].WireId);
            Assert.Equal(expected[i].Body, recorded[i].Body);
        }

        await client.CloseAsync(CloseReason.Local, ct);
        await SafeAwait(drain);
    }

    [Fact]
    public async Task ReRecordingRealCorpus_IsIdempotent()
    {
        // Exercise idempotency against an INDEPENDENT, server-recorded fixture (the committed 47/login-join corpus, which is clientbound-only) instead of frames this test synthesized itself from a formula. Loading it also runs the loader's integrity check, so the fixture is proven intact before we replay it.
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        string capturePath = Path.Combine(RepoFixtures.CorpusRoot, "47", "login-join.umpkcap");
        Assert.True(File.Exists(capturePath), $"expected committed corpus at {capturePath}");

        LoadedCorpus loaded = await CorpusLoader.LoadFileAsync(capturePath, ct);
        Assert.NotNull(loaded.Manifest);

        // The fixture is clientbound-only, so its full content hash equals its clientbound subset's; a faithful re-record must reproduce it byte-for-byte.
        Assert.Equal(0, loaded.Manifest!.ServerboundCount);

        IReadOnlyList<RecordedFrame> reRecorded = await CorpusReplayer.ReRecordClientboundAsync(loaded, ct: ct);

        (_, _, CorpusManifest reModel) = UmpkCapWriter.Serialize(
            reRecorded, loaded.Manifest.MinecraftVersion, loaded.Protocol, "idempotent", DateTimeOffset.UnixEpoch, 1);

        Assert.Equal(loaded.Manifest.ClientboundCount, reRecorded.Count);
        Assert.Equal(loaded.Manifest.ContentHash, reModel.ContentHash);
    }

    private static async Task WaitForCountAsync(Func<int> current, int target, CancellationToken ct)
    {
        while (current() < target)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(2, ct);
        }
    }

    private static async Task SafeAwait(Task t)
    {
        try
        {
            await t;
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
    }
}

/// <summary>Locates the committed corpus fixtures relative to the test assembly at runtime.</summary>
internal static class RepoFixtures
{
    /// <summary>The repo-root-relative corpus root: <c>fixtures/corpus</c>.</summary>
    public static string CorpusRoot => Path.Combine(RepoRoot(), "fixtures", "corpus");

    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Umpk.sln")) || Directory.Exists(Path.Combine(dir, "fixtures")))
                return dir;

            dir = Path.GetDirectoryName(dir);
        }

        return Directory.GetCurrentDirectory();
    }
}
