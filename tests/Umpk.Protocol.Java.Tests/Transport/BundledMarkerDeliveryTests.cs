using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Transport;

/// <summary>A frame with no decoded packet inside an open bundle. Under <see cref="UnknownPacketPolicy.Preserve"/> that is either a registered-but-unbound marker or an unmapped wire id; both must still enter the accumulator.</summary>
/// <remarks>
/// <para>Every packet between the two delimiters belongs to the bundle, so a bundled packet is delivered with its bundle and never before it. UMPK's accumulator was entered only when a frame decoded, so a marker inside a bundle was delivered as a frame-only item AHEAD of the bundle that lexically contains it.</para>
/// <para>This is reachable today: <c>minecraft:projectile_power</c> is a declared intentional marker from protocol 766 and can appear inside the entity-pairing bundle.</para>
/// </remarks>
public class BundledMarkerDeliveryTests
{
    private const int Delimiter = 0x00;

    private static CancellationToken Ct() => new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;

    private static JavaConnectionOptions Options() => new()
    {
        UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
        ReadIdleTimeout = TimeSpan.Zero,
    };

    /// <summary>The ordering guarantee: the marker travels INSIDE the bundle, in its wire position, and no frame-only item is delivered ahead of the bundle.</summary>
    [Fact]
    public async Task AMarkerInsideABundle_TravelsWithTheBundleAndKeepsItsPosition()
    {
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, Options());

        // Wire id 9 has no codec, which is what makes it a marker.
        conn.BindCodec(new FakeCodecBinding([Delimiter, 2, 3], bundleDelimiterWireId: Delimiter), PacketFlow.Clientbound);
        conn.Start();

        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [Delimiter]);
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [2, 0xB2]);
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [9, 0xD1, 0xD2]);
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [3, 0xC3]);
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [Delimiter]);

        // The first item off the connection must be the complete bundle.
        InboundItem item = await conn.ReceiveAsync(Ct());
        Assert.NotNull(item.Bundle);
        PacketBundle bundle = item.Bundle!;

        Assert.Equal(3, bundle.Packets.Count);
        Assert.Equal(2, ((FakeCodecBinding.Decoded)bundle.Packets[0]).WireId);
        Assert.Equal(3, ((FakeCodecBinding.Decoded)bundle.Packets[2]).WireId);

        var marker = Assert.IsType<UnknownPacket>(bundle.Packets[1]);
        Assert.Equal(9, marker.WireId);
        Assert.Equal(new byte[] { 0xD1, 0xD2 }, marker.Payload.ToArray());

        // The frame identity is carried alongside, so a consumer can still name the wire id and length.
        Assert.Equal(9, bundle.Frames[1].WireId);
        Assert.Equal(2, bundle.Frames[1].Payload.Length);
    }

    /// <summary>A bundle whose only content is a marker is one item, not a stray frame followed by an empty bundle.</summary>
    [Fact]
    public async Task ABundleContainingOnlyAMarker_IsStillOneItem()
    {
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, Options());
        conn.BindCodec(new FakeCodecBinding([Delimiter, 1], bundleDelimiterWireId: Delimiter), PacketFlow.Clientbound);
        conn.Start();

        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [Delimiter]);
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [9, 0xEE]);
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [Delimiter]);
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [1, 0x01]);

        InboundItem item = await conn.ReceiveAsync(Ct());
        Assert.NotNull(item.Bundle);
        Assert.Single(item.Bundle!.Packets);
        Assert.Equal(9, Assert.IsType<UnknownPacket>(item.Bundle.Packets[0]).WireId);

        InboundItem after = await conn.ReceiveAsync(Ct());
        Assert.Null(after.Bundle);
        Assert.Equal(1, after.Frame.WireId);
    }

    /// <summary>Outside a bundle, a marker remains a frame-only item so raw-frame consumers are unaffected.</summary>
    [Fact]
    public async Task AMarkerOutsideABundle_IsStillDeliveredAsAFrameOnlyItem()
    {
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, Options());
        conn.BindCodec(new FakeCodecBinding([Delimiter, 1], bundleDelimiterWireId: Delimiter), PacketFlow.Clientbound);
        conn.Start();

        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [9, 0xEE]);

        InboundItem item = await conn.ReceiveAsync(Ct());
        Assert.Null(item.Bundle);
        Assert.Null(item.Packet);
        Assert.Equal(9, item.Frame.WireId);
    }

    /// <summary>Terminal validation covers markers too. No play-clientbound terminal packet is currently a marker, so this pins the invariant for any later terminal marker.</summary>
    [Fact]
    public async Task ATerminalMarkerInsideABundle_IsRejected()
    {
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, Options());

        // Wire id 7 is terminal AND has no codec, so it is a terminal marker.
        conn.BindCodec(
            new FakeCodecBinding([Delimiter, 1], terminalWireId: 7, bundleDelimiterWireId: Delimiter),
            PacketFlow.Clientbound);
        conn.SetPhase(ProtocolPhase.Play);
        conn.Start();

        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [Delimiter]);
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [7]);

        ConnectionClosedException closed = await Assert.ThrowsAsync<ConnectionClosedException>(async () =>
        {
            while (true)
                await conn.ReceiveAsync(Ct());

        });
        Assert.Equal(CloseReason.ProtocolViolation, closed.Reason);
    }
}
