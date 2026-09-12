using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Transport;

public class CompressionTests
{
    private static JavaConnectionOptions FrameMode() => new()
    {
        UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
        ReadIdleTimeout = TimeSpan.Zero,
    };

    private static async Task<byte[]> RoundTripAsync(byte[] wireContent, int threshold)
    {
        var pair = DuplexPipePair.Create();
        await using var sender = new JavaConnection(pair.Left, FrameMode());
        await using var receiver = new JavaConnection(pair.Right, FrameMode());
        sender.EnableCompression(threshold);
        receiver.EnableCompression(threshold);
        receiver.Start();

        int wireId = wireContent[0];
        await sender.SendFrameAsync(wireId, wireContent[1..], Ct());
        InboundItem item = await receiver.ReceiveAsync(Ct());
        Assert.Equal(wireId, item.Frame.WireId);
        return item.Frame.Payload.ToArray();
    }

    [Fact]
    public async Task BelowThreshold_UsesRawPath_RoundTrips()
    {
        // 10-byte payload, threshold 256: sent uncompressed (dataLength = 0).
        byte[] content = new byte[11];
        content[0] = 0x05;
        Random.Shared.NextBytes(content.AsSpan(1));
        byte[] result = await RoundTripAsync(content, threshold: 256);
        Assert.Equal(content[1..], result);
    }

    [Fact]
    public async Task AboveThreshold_Compresses_RoundTrips()
    {
        byte[] content = new byte[2000];
        content[0] = 0x06;
        // Compressible data (repeating) so the compressed form differs.
        for (int i = 1; i < content.Length; i++)
            content[i] = (byte)(i % 4);

        byte[] result = await RoundTripAsync(content, threshold: 64);
        Assert.Equal(content[1..], result);
    }

    [Fact]
    public async Task ExactlyAtThreshold_Compresses()
    {
        // Vanilla compresses when length >= threshold. Content length == threshold triggers it.
        int threshold = 128;
        byte[] content = new byte[threshold];
        content[0] = 0x07;
        Random.Shared.NextBytes(content.AsSpan(1));
        byte[] result = await RoundTripAsync(content, threshold);
        Assert.Equal(content[1..], result);
    }

    [Fact]
    public async Task OneBelowThreshold_UsesRawPath()
    {
        int threshold = 128;
        byte[] content = new byte[threshold - 1];
        content[0] = 0x08;
        Random.Shared.NextBytes(content.AsSpan(1));
        byte[] result = await RoundTripAsync(content, threshold);
        Assert.Equal(content[1..], result);
    }

    [Fact]
    public async Task ZeroThreshold_CompressesEverything()
    {
        byte[] content = new byte[50];
        content[0] = 0x09;
        Random.Shared.NextBytes(content.AsSpan(1));
        byte[] result = await RoundTripAsync(content, threshold: 0);
        Assert.Equal(content[1..], result);
    }

    private static CancellationToken Ct() => new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;
}
