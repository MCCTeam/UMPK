using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Transport;

/// <summary>the connection drives a <see cref="BundleAccumulator"/> from its delivery path. Packets between two bundle_delimiter frames are buffered and delivered as one atomic <see cref="PacketBundle"/>. A terminal packet inside a bundle is rejected, and a bundle open when the connection closes is abandoned rather than delivered partially.</summary>
public class BundleDeliveryTests
{
    private static CancellationToken Ct() => new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;

    private const int Delimiter = 0x00;

    private static JavaConnectionOptions Options() => new()
    {
        UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
        ReadIdleTimeout = TimeSpan.Zero,
    };

    [Fact]
    public async Task PacketsBetweenDelimiters_AreDeliveredAsOneAtomicBundle()
    {
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, Options());
        conn.BindCodec(new FakeCodecBinding([Delimiter, 1, 2, 3], bundleDelimiterWireId: Delimiter), PacketFlow.Clientbound);
        conn.Start();

        // A single frame before the bundle passes through normally.
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [1, 0xA1]);
        // Open the bundle, two interior packets, close the bundle.
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [Delimiter]);
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [2, 0xB2]);
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [3, 0xC3]);
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [Delimiter]);
        // A single frame after the bundle passes through normally.
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [1, 0xA4]);

        // First item: the pre-bundle passthrough packet.
        InboundItem first = await conn.ReceiveAsync(Ct());
        Assert.Null(first.Bundle);
        Assert.Equal(1, first.Frame.WireId);

        // Second item: the whole bundle, atomically (the two delimiter frames are not delivered).
        InboundItem bundleItem = await conn.ReceiveAsync(Ct());
        Assert.NotNull(bundleItem.Bundle);
        Assert.Equal(2, bundleItem.Bundle!.Packets.Count);
        Assert.Equal(2, ((FakeCodecBinding.Decoded)bundleItem.Bundle.Packets[0]).WireId);
        Assert.Equal(3, ((FakeCodecBinding.Decoded)bundleItem.Bundle.Packets[1]).WireId);

        // Third item: the post-bundle passthrough packet.
        InboundItem last = await conn.ReceiveAsync(Ct());
        Assert.Null(last.Bundle);
        Assert.Equal(1, last.Frame.WireId);
    }

    /// <summary>A bundle item has no frame of its own (<see cref="InboundItem.Frame"/> is default), so without <see cref="PacketBundle.Frames"/> a consumer publishing frame identity for a bundled packet would have to invent a wire id. Each bundled packet keeps the id and byte count it actually arrived with.</summary>
    [Fact]
    public async Task BundledPackets_KeepTheirOwnWireIdAndByteCount()
    {
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, Options());
        conn.BindCodec(new FakeCodecBinding([Delimiter, 2, 3], bundleDelimiterWireId: Delimiter), PacketFlow.Clientbound);
        conn.Start();

        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [Delimiter]);
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [2, 0xB2, 0xB3, 0xB4]); // wire id 2, 3 body bytes
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [3]);                   // wire id 3, empty body
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [Delimiter]);

        InboundItem bundleItem = await conn.ReceiveAsync(Ct());
        Assert.NotNull(bundleItem.Bundle);
        PacketBundle bundle = bundleItem.Bundle!;

        // The item itself has no identity, which is exactly why the per-packet frames must be carried.
        Assert.Equal(0, bundleItem.Frame.WireId);
        Assert.Equal(0, bundleItem.Frame.Payload.Length);

        Assert.Equal(bundle.Packets.Count, bundle.Frames.Count);
        Assert.Equal(2, bundle.Frames[0].WireId);
        Assert.Equal(3, bundle.Frames[0].Payload.Length);
        Assert.Equal(new byte[] { 0xB2, 0xB3, 0xB4 }, bundle.Frames[0].Payload.ToArray());
        Assert.Equal(3, bundle.Frames[1].WireId);
        Assert.Equal(0, bundle.Frames[1].Payload.Length);
    }

    [Fact]
    public async Task EmptyBundle_DeliversAnEmptyPacketBundle()
    {
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, Options());
        conn.BindCodec(new FakeCodecBinding([Delimiter, 1], bundleDelimiterWireId: Delimiter), PacketFlow.Clientbound);
        conn.Start();

        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [Delimiter]);
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [Delimiter]);
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [1, 0x01]);

        InboundItem bundleItem = await conn.ReceiveAsync(Ct());
        Assert.NotNull(bundleItem.Bundle);
        Assert.Empty(bundleItem.Bundle!.Packets);

        InboundItem after = await conn.ReceiveAsync(Ct());
        Assert.Equal(1, after.Frame.WireId);
    }

    [Fact]
    public async Task TerminalPacketInsideBundle_IsRejected()
    {
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, Options());
        // Wire id 7 is terminal; a terminal packet must not appear inside a bundle.
        conn.BindCodec(
            new FakeCodecBinding([Delimiter, 7], terminalWireId: 7, bundleDelimiterWireId: Delimiter),
            PacketFlow.Clientbound);
        conn.SetPhase(ProtocolPhase.Play);
        conn.Start();

        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [Delimiter]); // open
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [7]);         // terminal inside the bundle

        await Assert.ThrowsAsync<ConnectionClosedException>(async () =>
        {
            while (true)
                await conn.ReceiveAsync(Ct());

        });
    }

    [Fact]
    public async Task ConnectionClosedMidBundle_AbandonsBufferedPackets()
    {
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, Options());
        conn.BindCodec(new FakeCodecBinding([Delimiter, 2], bundleDelimiterWireId: Delimiter), PacketFlow.Clientbound);
        conn.Start();

        // Open a bundle and buffer one packet, then complete the peer's stream without closing the bundle.
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [Delimiter]);
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [2, 0xB2]);
        await pair.Right.Output.CompleteAsync();

        // The buffered (never-closed) bundle is discarded, not surfaced: the consumer sees the stream end, never a partial bundle.
        await Assert.ThrowsAsync<ConnectionClosedException>(async () => await conn.ReceiveAsync(Ct()));
    }
}
