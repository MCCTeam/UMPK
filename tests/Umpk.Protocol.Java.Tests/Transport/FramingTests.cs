using System.Buffers;
using System.IO.Pipelines;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Transport;

public class FramingTests
{
    private static readonly JavaConnectionOptions FrameMode = new()
    {
        UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
        ReadIdleTimeout = TimeSpan.Zero,
    };

    private static byte[] EncodeVarInt(int value)
    {
        Span<byte> buf = stackalloc byte[5];
        int n = VarInt.Write(value, buf);
        return buf[..n].ToArray();
    }

    [Fact]
    public async Task SingleFrame_RoundTrips()
    {
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, FrameMode);
        conn.Start();

        byte[] payload = [0x2A, 1, 2, 3, 4]; // wire id 0x2A + body
        await WriteRawFrameAsync(pair.Right.Output, payload);

        InboundItem item = await conn.ReceiveAsync(TestTimeout());
        Assert.Equal(0x2A, item.Frame.WireId);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, item.Frame.Payload.ToArray());
    }

    [Fact]
    public async Task MultipleFrames_DeliverInOrder()
    {
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, FrameMode);
        conn.Start();

        for (int i = 0; i < 20; i++)
            await WriteRawFrameAsync(pair.Right.Output, [(byte)i, (byte)(i * 2)]);

        for (int i = 0; i < 20; i++)
        {
            InboundItem item = await conn.ReceiveAsync(TestTimeout());
            Assert.Equal(i, item.Frame.WireId);
            Assert.Equal(i * 2, item.Frame.Payload[0]);
        }
    }

    [Fact]
    public async Task MultiSegmentFrame_IsLinearized()
    {
        // Feed a frame one byte at a time across many pipe flushes; framing must reassemble it.
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, FrameMode);
        conn.Start();

        var body = new byte[500];
        for (int i = 0; i < body.Length; i++)
            body[i] = (byte)i;

        byte[] wireId = EncodeVarInt(7);
        byte[] frameContent = [.. wireId, .. body];
        byte[] lenPrefix = EncodeVarInt(frameContent.Length);
        byte[] wire = [.. lenPrefix, .. frameContent];

        foreach (byte b in wire)
        {
            Memory<byte> mem = pair.Right.Output.GetMemory(1);
            mem.Span[0] = b;
            pair.Right.Output.Advance(1);
            await pair.Right.Output.FlushAsync();
        }

        InboundItem item = await conn.ReceiveAsync(TestTimeout());
        Assert.Equal(7, item.Frame.WireId);
        Assert.Equal(body, item.Frame.Payload.ToArray());
    }

    [Fact]
    public async Task SplitLengthPrefix_IsReassembled()
    {
        // A 2-byte length VarInt split across two flushes.
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, FrameMode);
        conn.Start();

        var body = new byte[200];
        Random.Shared.NextBytes(body);
        byte[] content = [9, .. body];
        byte[] len = EncodeVarInt(content.Length); // 2 bytes since 201 > 127

        Assert.Equal(2, len.Length);
        await FlushBytesAsync(pair.Right.Output, len[..1]);
        await Task.Delay(10);
        await FlushBytesAsync(pair.Right.Output, [.. len[1..], .. content]);

        InboundItem item = await conn.ReceiveAsync(TestTimeout());
        Assert.Equal(9, item.Frame.WireId);
        Assert.Equal(body, item.Frame.Payload.ToArray());
    }

    [Fact]
    public async Task SendFrame_EncodesReadableWire()
    {
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, FrameMode);

        await conn.SendFrameAsync(0x1F, new byte[] { 10, 20, 30 }, TestTimeout());

        // Read the raw bytes off the other end and decode manually.
        ReadResult rr = await pair.Right.Input.ReadAsync(TestTimeout());
        byte[] wire = rr.Buffer.ToArray();
        // len(4) = 04, wireId=1F, body 0A 14 1E
        Assert.Equal(new byte[] { 0x04, 0x1F, 0x0A, 0x14, 0x1E }, wire);
    }

    internal static async Task WriteRawFrameAsync(PipeWriter output, byte[] frameContent)
    {
        byte[] len = EncodeVarInt(frameContent.Length);
        await FlushBytesAsync(output, [.. len, .. frameContent]);
    }

    private static async Task FlushBytesAsync(PipeWriter output, byte[] bytes)
    {
        Memory<byte> mem = output.GetMemory(bytes.Length);
        bytes.CopyTo(mem);
        output.Advance(bytes.Length);
        await output.FlushAsync();
    }

    private static CancellationToken TestTimeout() =>
        new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;
}
