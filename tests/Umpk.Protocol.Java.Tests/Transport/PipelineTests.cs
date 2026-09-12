using System.Diagnostics;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Protocol.Java.Tests.Transport;

public class PipelineTests(ITestOutputHelper output)
{
    private static byte[] Key() => Enumerable.Range(0, 16).Select(i => (byte)(i * 11 + 3)).ToArray();

    private static JavaConnectionOptions FrameMode(int capacity = 1024) => new()
    {
        UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
        ReadIdleTimeout = TimeSpan.Zero,
        InboundChannelCapacity = capacity,
    };

    [Fact]
    public async Task Encryption_RoundTrips()
    {
        var pair = DuplexPipePair.Create();
        await using var sender = new JavaConnection(pair.Left, FrameMode());
        await using var receiver = new JavaConnection(pair.Right, FrameMode());
        sender.EnableEncryption(Key());
        receiver.EnableEncryption(Key());
        receiver.Start();

        for (int i = 0; i < 50; i++)
        {
            var body = new byte[100];
            Random.Shared.NextBytes(body);
            await sender.SendFrameAsync(i & 0x7F, body, Ct());
            InboundItem item = await receiver.ReceiveAsync(Ct());
            Assert.Equal(i & 0x7F, item.Frame.WireId);
            Assert.Equal(body, item.Frame.Payload.ToArray());
        }
    }

    [Fact]
    public async Task CompressionThenEncryption_RoundTrips()
    {
        var pair = DuplexPipePair.Create();
        await using var sender = new JavaConnection(pair.Left, FrameMode());
        await using var receiver = new JavaConnection(pair.Right, FrameMode());
        sender.EnableCompression(64);
        receiver.EnableCompression(64);
        sender.EnableEncryption(Key());
        receiver.EnableEncryption(Key());
        receiver.Start();

        var big = new byte[5000];
        for (int i = 0; i < big.Length; i++)
            big[i] = (byte)(i % 7);

        await sender.SendFrameAsync(0x42, big, Ct());
        InboundItem item = await receiver.ReceiveAsync(Ct());
        Assert.Equal(0x42, item.Frame.WireId);
        Assert.Equal(big, item.Frame.Payload.ToArray());
    }

    [Fact]
    public async Task LoopbackSmoke_100kFrames_CompressionAndEncryption()
    {
        const int frames = 100_000;
        var pair = DuplexPipePair.Create();
        await using var sender = new JavaConnection(pair.Left, FrameMode(capacity: 4096));
        await using var receiver = new JavaConnection(pair.Right, FrameMode(capacity: 4096));
        sender.EnableCompression(64);
        receiver.EnableCompression(64);
        sender.EnableEncryption(Key());
        receiver.EnableEncryption(Key());
        receiver.Start();

        // A payload above the compression threshold so both transforms run every frame.
        var body = new byte[128];
        for (int i = 0; i < body.Length; i++)
            body[i] = (byte)(i % 13);

        var sw = Stopwatch.StartNew();

        Task consumer = Task.Run(async () =>
        {
            long received = 0;
            await foreach (InboundItem item in receiver.ReceiveAllAsync(Ct()))
            {
                if (item.Frame.Payload.Length != body.Length)
                    throw new Xunit.Sdk.XunitException("payload length mismatch");

                if (++received == frames)
                    return;

            }
        });

        for (int i = 0; i < frames; i++)
            await sender.SendFrameAsync(i & 0x7F, body, Ct());

        await consumer;
        sw.Stop();

        double perSec = frames / sw.Elapsed.TotalSeconds;
        output.WriteLine($"Loopback: {frames} frames in {sw.ElapsedMilliseconds} ms = {perSec:N0} frames/s");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(60), "Loopback smoke took too long.");
    }

    private static CancellationToken Ct() => new CancellationTokenSource(TimeSpan.FromSeconds(90)).Token;
}
