using System.Buffers;
using System.Diagnostics.Metrics;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Transport;

public class ConnectionBehaviorTests
{
    private static CancellationToken Ct(int seconds = 10) =>
        new CancellationTokenSource(TimeSpan.FromSeconds(seconds)).Token;

    [Fact]
    public async Task Backpressure_FullChannel_StallsReader()
    {
        // Bound of 2: the sender can enqueue at most channelBound + a couple frames in flight, then the read loop stalls until the consumer drains. We assert the reader does not run arbitrarily far ahead.
        var pair = DuplexPipePair.Create();
        var options = new JavaConnectionOptions
        {
            InboundChannelCapacity = 2,
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
            ReadIdleTimeout = TimeSpan.Zero,
        };
        await using var conn = new JavaConnection(pair.Left, options);

        long observed = 0;
        conn.PacketObserved += _ => Interlocked.Increment(ref observed);
        conn.Start();

        // Push 50 frames without consuming any.
        for (int i = 0; i < 50; i++)
            await FramingTests.WriteRawFrameAsync(pair.Right.Output, [(byte)i]);

        await Task.Delay(200);

        // With a bound of 2, the read loop can only have observed a small number of frames (channel capacity + the one it is blocked writing). It must not have drained all 50.
        long seen = Interlocked.Read(ref observed);
        Assert.True(seen < 10, $"Reader ran ahead unexpectedly: observed {seen} frames with a bound of 2.");

        // Draining lets the reader make progress.
        for (int i = 0; i < 50; i++)
        {
            InboundItem item = await conn.ReceiveAsync(Ct());
            Assert.Equal(i, item.Frame.WireId);
        }
    }

    [Fact]
    public async Task FrameMode_DecodeFilter_DecodesOnlySelected()
    {
        var pair = DuplexPipePair.Create();
        var options = new JavaConnectionOptions
        {
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
            ReadIdleTimeout = TimeSpan.Zero,
        };
        await using var conn = new JavaConnection(pair.Left, options);
        conn.BindCodec(new FakeCodecBinding([1, 2, 3]), PacketFlow.Clientbound);
        // Only decode wire id 2.
        conn.SetDecodeFilter(new PacketDecodeFilter((_, _, wireId) => wireId == 2));
        conn.Start();

        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [1, 0xAA]);
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [2, 0xBB]);
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [3, 0xCC]);

        InboundItem a = await conn.ReceiveAsync(Ct());
        Assert.Null(a.Packet); // wire id 1 not selected for decode
        Assert.Equal(1, a.Frame.WireId);

        InboundItem b = await conn.ReceiveAsync(Ct());
        Assert.NotNull(b.Packet); // wire id 2 decoded
        Assert.Equal(2, b.Frame.WireId);

        InboundItem c = await conn.ReceiveAsync(Ct());
        Assert.Null(c.Packet);
        Assert.Equal(3, c.Frame.WireId);
    }

    [Fact]
    public async Task ItemMode_DecodesEveryKnownFrame()
    {
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, new JavaConnectionOptions
        {
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
            ReadIdleTimeout = TimeSpan.Zero,
        });
        conn.BindCodec(new FakeCodecBinding([5]), PacketFlow.Clientbound);
        conn.Start();

        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [5, 1, 2, 3]);
        InboundItem item = await conn.ReceiveAsync(Ct());
        var decoded = Assert.IsType<FakeCodecBinding.Decoded>(item.Packet);
        Assert.Equal(5, decoded.WireId);
        Assert.Equal(new byte[] { 1, 2, 3 }, decoded.Body);
    }

    [Fact]
    public async Task UnknownPacketPolicy_Throw_ClosesConnection()
    {
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, new JavaConnectionOptions
        {
            UnknownPacketPolicy = UnknownPacketPolicy.Throw,
            ReadIdleTimeout = TimeSpan.Zero,
        });
        conn.BindCodec(new FakeCodecBinding([1]), PacketFlow.Clientbound);
        conn.Start();

        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [99, 0]); // unmapped
        await Assert.ThrowsAsync<ConnectionClosedException>(async () =>
        {
            while (true)
                await conn.ReceiveAsync(Ct());

        });
    }

    [Fact]
    public async Task TerminalPacket_PausesReader_UntilSetPhase()
    {
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, new JavaConnectionOptions
        {
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
            ReadIdleTimeout = TimeSpan.Zero,
        });
        // Wire id 7 is terminal, transitioning Login -> Play.
        conn.BindCodec(new FakeCodecBinding([7, 8], terminalWireId: 7, nextPhase: ProtocolPhase.Play), PacketFlow.Clientbound);
        conn.SetPhase(ProtocolPhase.Login);
        conn.Start();

        long observed = 0;
        conn.PacketObserved += _ => Interlocked.Increment(ref observed);

        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [7]); // terminal
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [8, 1]); // after transition

        // Consume the terminal packet.
        InboundItem terminal = await conn.ReceiveAsync(Ct());
        Assert.Equal(7, terminal.Frame.WireId);

        // Reader is parked: the second frame must not be observed until we acknowledge the phase.
        await Task.Delay(150);
        Assert.Equal(1, Interlocked.Read(ref observed));
        Assert.Equal(ProtocolPhase.Login, conn.Phase);

        conn.SetPhase(ProtocolPhase.Play);
        InboundItem next = await conn.ReceiveAsync(Ct());
        Assert.Equal(8, next.Frame.WireId);
        Assert.Equal(ProtocolPhase.Play, conn.Phase);
    }

    [Fact]
    public async Task CompressionEnablePoint_PausesReader_UntilEnableCompression()
    {
        // Over a zero-latency in-memory pipe, the set-compression frame and the first compressed frame both arrive before the consumer can enable compression. Without the gate the read loop reads the compressed frame with the uncompressed reader and mis-frames it. With the gate the read loop parks after the set-compression frame.
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, new JavaConnectionOptions
        {
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
            ReadIdleTimeout = TimeSpan.Zero,
        });
        // Wire id 3 is the set-compression frame; wire id 9 is the first compressed frame.
        conn.BindCodec(new FakeCodecBinding([3, 9], compressionWireId: 3), PacketFlow.Clientbound);
        conn.SetPhase(ProtocolPhase.Login);
        conn.Start();

        long observed = 0;
        conn.PacketObserved += _ => Interlocked.Increment(ref observed);

        // The set-compression frame is sent uncompressed (compression not yet on).
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [3]);

        // The very next frame is already in the compressed wire format. Produce those exact bytes with a sender that has compression enabled, and push them straight onto the pipe (zero latency).
        byte[] compressedFrameBytes = await BuildCompressedFrameBytesAsync(wireId: 9, payloadLength: 2000, threshold: 64);
        await pair.Right.Output.WriteAsync(compressedFrameBytes, Ct());
        await pair.Right.Output.FlushAsync(Ct());

        // Consume the set-compression frame.
        InboundItem setCompression = await conn.ReceiveAsync(Ct());
        Assert.Equal(3, setCompression.Frame.WireId);

        // The read loop is parked at the compression boundary: the compressed frame must not be read yet.
        await Task.Delay(150);
        Assert.Equal(1, Interlocked.Read(ref observed));
        Assert.False(conn.CompressionEnabled);

        // Enabling compression releases the gate; the compressed frame now decodes with the right reader.
        conn.EnableCompression(64);
        InboundItem next = await conn.ReceiveAsync(Ct());
        Assert.Equal(9, next.Frame.WireId);
        Assert.Equal(2000 - 1, next.Frame.Payload.Length); // 2000-byte wire content minus the 1-byte wire id
        Assert.True(conn.CompressionEnabled);
    }

    // Produces the on-wire bytes of a single compressed-format frame (as a compression-enabled sender would emit), by driving a sender connection over a throwaway pipe and reading what it wrote.
    private static async Task<byte[]> BuildCompressedFrameBytesAsync(int wireId, int payloadLength, int threshold)
    {
        var pair = DuplexPipePair.Create();
        await using var sender = new JavaConnection(pair.Left, new JavaConnectionOptions
        {
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
            ReadIdleTimeout = TimeSpan.Zero,
        });
        sender.EnableCompression(threshold);

        byte[] payload = new byte[payloadLength - 1];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 4); // compressible so the compressed form is distinct
        }

        await sender.SendFrameAsync(wireId, payload, Ct());
        await pair.Left.Output.CompleteAsync();

        System.IO.Pipelines.ReadResult read = await pair.Right.Input.ReadAsync(Ct());
        byte[] bytes = read.Buffer.ToArray();
        pair.Right.Input.AdvanceTo(read.Buffer.End);
        return bytes;
    }

    [Fact]
    public async Task Stats_CountBytesAndPackets()
    {
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, new JavaConnectionOptions
        {
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
            ReadIdleTimeout = TimeSpan.Zero,
        });
        conn.Start();

        await conn.SendFrameAsync(1, new byte[] { 1, 2, 3 }, Ct());
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [1, 9, 9]);
        await conn.ReceiveAsync(Ct());

        Assert.Equal(1, conn.Stats.PacketsOut);
        Assert.Equal(1, conn.Stats.PacketsIn);
        Assert.True(conn.Stats.BytesOut > 0);
        Assert.True(conn.Stats.BytesIn > 0);
    }

    [Fact]
    public async Task IdleTimeout_ClosesConnection()
    {
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, new JavaConnectionOptions
        {
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
            ReadIdleTimeout = TimeSpan.FromMilliseconds(100),
        });
        conn.Start();

        var ex = await Assert.ThrowsAsync<ConnectionClosedException>(async () =>
        {
            await conn.ReceiveAsync(Ct());
        });
        Assert.Equal(CloseReason.IdleTimeout, ex.Reason);
    }

    [Fact]
    public async Task Metrics_AreEmitted()
    {
        using var listener = new MeterListener();
        long packetsIn = 0;
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == ConnectionDiagnostics.MeterName)
                l.EnableMeasurementEvents(instrument);

        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == "umpk.connection.packets_in")
                Interlocked.Add(ref packetsIn, value);

        });
        listener.Start();

        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, new JavaConnectionOptions
        {
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
            ReadIdleTimeout = TimeSpan.Zero,
        });
        conn.Start();
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [1, 2]);
        await conn.ReceiveAsync(Ct());
        listener.RecordObservableInstruments();

        Assert.True(Interlocked.Read(ref packetsIn) >= 1);
    }
}
