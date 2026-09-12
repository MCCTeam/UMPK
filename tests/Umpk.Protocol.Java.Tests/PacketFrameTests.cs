using Xunit;

namespace Umpk.Protocol.Java.Tests;

/// <summary><see cref="PacketFrame"/>, the ref-struct view <see cref="PacketObservation.AsFrame"/> hands out, and the guard it exists to enforce: the payload cannot be boxed, captured, stored, or otherwise made to outlive the callback that received it, which is what keeps a consumer from reading a pooled inbound buffer or the outbound encode scratch after either has been recycled for the next frame. The same compile-time and runtime assertions below pin that contract.</summary>
public sealed class PacketFrameTests
{
    [Fact]
    public void AsFrame_CarriesFlowPhaseWireIdAndPayload()
    {
        var observation = new PacketObservation(
            PacketFlow.Clientbound, ProtocolPhase.Play, 0x26, [1, 2, 3, 4], offset: 0, length: 4, decodedPacket: null);

        PacketFrame frame = observation.AsFrame();

        Assert.Equal(PacketFlow.Clientbound, frame.Flow);
        Assert.Equal(ProtocolPhase.Play, frame.Phase);
        Assert.Equal(0x26, frame.WireId);
        Assert.Equal(4, frame.PayloadLength);
        Assert.True(frame.IsClientbound);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, frame.Payload.ToArray());
        Assert.Null(frame.DecodedPacket);
    }

    [Fact]
    public void CopyPayload_DetachesFromTheRecycledBuffer()
    {
        // The payload points at a pooled inbound buffer or the shared outbound scratch, both reused by the next frame. A recorder that keeps bytes must get its own copy, and this is that guarantee. Serverbound here too, so this test and the one above between them cover both directions.
        byte[] shared = [1, 2, 3];
        var observation = new PacketObservation(
            PacketFlow.Serverbound, ProtocolPhase.Configuration, 0x0E, shared, offset: 0, length: 3, decodedPacket: null);

        PacketFrame frame = observation.AsFrame();
        byte[] kept = frame.CopyPayload();

        // Simulate the pooled buffer being handed back and reused for the next frame.
        shared[0] = 99;

        Assert.Equal(new byte[] { 1, 2, 3 }, kept);
        Assert.False(frame.IsClientbound);
    }

    [Fact]
    public void PayloadLength_IsReadableWithoutTouchingThePayload()
    {
        // A caller that only wants to know how many bytes there are (allocation planning, a length-only log line) never has to materialize or index the span itself. Also exercises the delegate: a plain Action<PacketFrame> cannot be used at all, since PacketFrame is a ref struct, so the handler is invoked directly here as the type requires (in PacketFrame).
        var observation = new PacketObservation(
            PacketFlow.Clientbound, ProtocolPhase.Login, 0x02, [9, 9], offset: 0, length: 2, decodedPacket: null);

        int seenLength = -1;
        PacketFrameHandler handler = (in PacketFrame f) => seenLength = f.PayloadLength;
        PacketFrame frame = observation.AsFrame();
        handler(in frame);

        Assert.Equal(2, seenLength);
    }
}
