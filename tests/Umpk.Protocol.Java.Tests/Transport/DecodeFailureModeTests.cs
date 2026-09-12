using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Transport;

/// <summary><see cref="DecodeFailureMode"/> is consumed in the decode path. A mapped-but-buggy codec (one whose wire id is known but whose decode throws) is handled per the connection's <see cref="JavaConnectionOptions.DecodeFailurePolicy"/>: fail the connection (default), skip-and-report, or forward the raw frame verbatim for byte-identical re-emission.</summary>
public class DecodeFailureModeTests
{
    private static CancellationToken Ct() => new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;

    private static JavaConnectionOptions Options(DecodeFailureMode mode) => new()
    {
        UnknownPacketPolicy = UnknownPacketPolicy.Throw,
        DecodeFailurePolicy = mode,
        ReadIdleTimeout = TimeSpan.Zero,
    };

    [Fact]
    public void FatalDecode_HasAStructuredFailureEventSurface()
    {
        var eventInfo = typeof(JavaConnection).GetEvent("PacketDecodeFailed");

        Assert.NotNull(eventInfo);
        Assert.Equal("System.Action`1", eventInfo!.EventHandlerType!.GetGenericTypeDefinition().FullName);
        Assert.Equal("PacketDecodeFailure", eventInfo.EventHandlerType.GenericTypeArguments[0].Name);
    }

    [Fact]
    public async Task FailConnection_IsDefault_AndClosesOnDecodeFailure()
    {
        Assert.Equal(DecodeFailureMode.FailConnection, new JavaConnectionOptions().DecodeFailurePolicy);

        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, Options(DecodeFailureMode.FailConnection));
        // Wire id 5 is "known" but its decode throws.
        conn.BindCodec(new FakeCodecBinding([5], throwOnDecodeWireIds: [5]), PacketFlow.Clientbound);
        conn.Start();

        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [5, 1, 2, 3]);

        await Assert.ThrowsAsync<ConnectionClosedException>(async () =>
        {
            while (true)
                await conn.ReceiveAsync(Ct());

        });
    }

    [Fact]
    public async Task FailConnection_ReportsBoundedFailureExactlyOnce_AndContainsSubscriberErrors()
    {
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, Options(DecodeFailureMode.FailConnection));
        conn.BindCodec(new FakeCodecBinding([5], throwOnDecodeWireIds: [5]), PacketFlow.Clientbound);
        conn.SetPhase(ProtocolPhase.Play);

        int throwingCalls = 0;
        int receivingCalls = 0;
        PacketDecodeFailure? reported = null;
        conn.PacketDecodeFailed += _ =>
        {
            Interlocked.Increment(ref throwingCalls);
            throw new InvalidOperationException("diagnostic subscriber failed");
        };
        conn.PacketDecodeFailed += failure =>
        {
            Interlocked.Increment(ref receivingCalls);
            reported = failure;
        };
        conn.Start();

        byte[] body = Enumerable.Range(0, 300).Select(static value => (byte)value).ToArray();
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [5, .. body]);

        ConnectionClosedException closed = await Assert.ThrowsAsync<ConnectionClosedException>(async () =>
        {
            while (true)
                await conn.ReceiveAsync(Ct());
        });

        Assert.Equal(CloseReason.ProtocolViolation, closed.Reason);
        Assert.Equal(1, Volatile.Read(ref throwingCalls));
        Assert.Equal(1, Volatile.Read(ref receivingCalls));
        Assert.NotNull(reported);
        Assert.Null(reported.Protocol);
        Assert.Equal(ProtocolPhase.Play, reported.Phase);
        Assert.Equal(PacketFlow.Clientbound, reported.Flow);
        Assert.Equal(5, reported.WireId);
        Assert.Null(reported.PacketId);
        Assert.Null(reported.CodecIdentity);
        Assert.Equal(body.Length, reported.PayloadLength);
        Assert.Equal(body[..256], reported.Evidence.ToArray());
        Assert.True(reported.EvidenceTruncated);
        Assert.IsType<ProtocolViolationException>(reported.Exception);
    }

    [Fact]
    public async Task FailConnection_ExcludesLoginPayloadFromFailureEvidence()
    {
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, Options(DecodeFailureMode.FailConnection));
        conn.BindCodec(new FakeCodecBinding([5], throwOnDecodeWireIds: [5]), PacketFlow.Clientbound);
        conn.SetPhase(ProtocolPhase.Login);

        PacketDecodeFailure? reported = null;
        conn.PacketDecodeFailed += failure => reported = failure;
        conn.Start();

        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [5, 0xAA, 0xBB, 0xCC]);
        await Assert.ThrowsAsync<ConnectionClosedException>(async () =>
        {
            while (true)
                await conn.ReceiveAsync(Ct());
        });

        Assert.NotNull(reported);
        Assert.Equal(3, reported.PayloadLength);
        Assert.Empty(reported.Evidence.ToArray());
        Assert.True(reported.EvidenceTruncated);
    }

    [Fact]
    public async Task SuccessfulDecode_ObservesPacketOnce_WithoutFailureReport()
    {
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, Options(DecodeFailureMode.FailConnection));
        conn.BindCodec(new FakeCodecBinding([5]), PacketFlow.Clientbound);

        int observations = 0;
        int failures = 0;
        conn.PacketObserved += _ => Interlocked.Increment(ref observations);
        conn.PacketDecodeFailed += _ => Interlocked.Increment(ref failures);
        conn.Start();

        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [5, 1, 2, 3]);
        InboundItem item = await conn.ReceiveAsync(Ct());

        Assert.IsType<FakeCodecBinding.Decoded>(item.Packet);
        Assert.Equal(1, Volatile.Read(ref observations));
        Assert.Equal(0, Volatile.Read(ref failures));
    }

    [Fact]
    public async Task SkipFrameAndReport_DropsFrame_AndContinues()
    {
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, Options(DecodeFailureMode.SkipFrameAndReport));
        conn.BindCodec(new FakeCodecBinding([5, 6], throwOnDecodeWireIds: [5]), PacketFlow.Clientbound);
        conn.Start();

        // The undecodable frame (wire id 5) is dropped; the next frame (wire id 6) still arrives.
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [5, 0xAA]);
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [6, 0xBB]);

        InboundItem item = await conn.ReceiveAsync(Ct());
        Assert.Equal(6, item.Frame.WireId);
        var decoded = Assert.IsType<FakeCodecBinding.Decoded>(item.Packet);
        Assert.Equal(6, decoded.WireId);
    }

    [Fact]
    public async Task ForwardVerbatim_SurfacesUnknownPacket_WithByteIdenticalPayload()
    {
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, Options(DecodeFailureMode.ForwardVerbatim));
        conn.BindCodec(new FakeCodecBinding([5], throwOnDecodeWireIds: [5]), PacketFlow.Clientbound);
        conn.Start();

        byte[] body = [0xDE, 0xAD, 0xBE, 0xEF];
        byte[] frameContent = [5, .. body]; // wire id 5 + body
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, frameContent);

        InboundItem item = await conn.ReceiveAsync(Ct());
        var unknown = Assert.IsType<UnknownPacket>(item.Packet);
        Assert.Equal(5, unknown.WireId);
        // The raw payload following the wire id is preserved byte-for-byte.
        Assert.Equal(body, unknown.Payload.ToArray());
        // And the delivered raw frame matches too.
        Assert.Equal(5, item.Frame.WireId);
        Assert.Equal(body, item.Frame.Payload.ToArray());
    }

    [Fact]
    public async Task ForwardVerbatim_ReEmitsTheOriginalFrameContentByteForByte()
    {
        // A forwarded undecodable frame must re-emit byte-identically. Verbatim re-emission of an UnknownPacket is, by definition, its wire-id VarInt followed by its untouched payload (DescriptorFrameCodecBinding.TryEncode's UnknownPacket branch does exactly that). Reconstruct that here and compare to the original frame content.
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, Options(DecodeFailureMode.ForwardVerbatim));
        conn.BindCodec(new FakeCodecBinding([300], throwOnDecodeWireIds: [300]), PacketFlow.Clientbound);
        conn.Start();

        // Use a multi-byte wire id (300 -> 2-byte VarInt) so the reconstruction exercises the id encoding.
        byte[] wireId = new byte[VarInt.MaxBytes];
        int idLen = VarInt.Write(300, wireId);
        byte[] body = [0x10, 0x20, 0x30, 0x40, 0x50];
        byte[] frameContent = [.. wireId[..idLen], .. body];
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, frameContent);

        InboundItem item = await conn.ReceiveAsync(Ct());
        var unknown = Assert.IsType<UnknownPacket>(item.Packet);
        Assert.Equal(300, unknown.WireId);

        byte[] reEmitted = new byte[VarInt.MaxBytes + unknown.Payload.Length];
        int n = VarInt.Write(unknown.WireId, reEmitted);
        unknown.Payload.Span.CopyTo(reEmitted.AsSpan(n));
        Assert.Equal(frameContent, reEmitted[..(n + unknown.Payload.Length)]);
    }
}
