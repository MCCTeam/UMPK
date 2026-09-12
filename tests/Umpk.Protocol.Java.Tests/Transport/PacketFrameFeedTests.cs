using System.IO.Pipelines;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Transport;

/// <summary>The raw frame feed in both directions. These checks assert the concrete wire id and payload bytes for inbound and outbound frames, plus the cost contract that the send path does no observation work when nobody is listening.</summary>
public class PacketFrameFeedTests
{
    private static CancellationToken Ct(int seconds = 10) =>
        new CancellationTokenSource(TimeSpan.FromSeconds(seconds)).Token;

    private static JavaConnectionOptions FrameMode() => new()
    {
        UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
        ReadIdleTimeout = TimeSpan.Zero,
        InboundChannelCapacity = 256,
    };

    [Fact]
    public async Task Outbound_Observation_Carries_The_Real_WireId_And_Bytes()
    {
        DuplexPipePair pipes = DuplexPipePair.Create();
        await using var client = new JavaConnection(pipes.Left, FrameMode());
        client.BindCodec(binding: null, PacketFlow.Clientbound);
        client.Start();

        int wireId = -1;
        byte[] captured = [];
        PacketFlow flow = default;
        ProtocolPhase phase = default;
        int reportedLength = -1;
        client.OutboundPacketObserved += obs =>
        {
            wireId = obs.WireId;
            captured = obs.CopyPayload();
            flow = obs.Flow;
            phase = obs.Phase;
            reportedLength = obs.PayloadLength;
        };

        byte[] payload = [0xDE, 0xAD, 0xBE, 0xEF, 0x01];
        await client.SendFrameAsync(0x2A, payload, Ct());

        Assert.Equal(0x2A, wireId);
        Assert.Equal(payload, captured);
        Assert.Equal(payload.Length, reportedLength);

        // A client's outbound flow is serverbound, and the connection starts in handshake.
        Assert.Equal(PacketFlow.Serverbound, flow);
        Assert.Equal(ProtocolPhase.Handshake, phase);
    }

    [Fact]
    public async Task Outbound_Observation_Reads_A_MultiByte_VarInt_WireId()
    {
        // A wire id above 0x7F encodes as two VarInt bytes. The observation must report the decoded id and a payload that starts after BOTH id bytes, not a payload shifted by one.
        DuplexPipePair pipes = DuplexPipePair.Create();
        await using var client = new JavaConnection(pipes.Left, FrameMode());
        client.BindCodec(binding: null, PacketFlow.Clientbound);
        client.Start();

        int wireId = -1;
        byte[] captured = [];
        client.OutboundPacketObserved += obs =>
        {
            wireId = obs.WireId;
            captured = obs.CopyPayload();
        };

        byte[] payload = [0x11, 0x22, 0x33];
        await client.SendFrameAsync(0x1234, payload, Ct());

        Assert.Equal(0x1234, wireId);
        Assert.Equal(payload, captured);
    }

    [Fact]
    public async Task Inbound_And_Outbound_Are_Both_Observed_On_One_Session()
    {
        // The direction that matters for a replay: a real exchange over one pipe pair, with both halves of the conversation recovered from the two feeds.
        DuplexPipePair pipes = DuplexPipePair.Create();
        await using var client = new JavaConnection(pipes.Left, FrameMode());
        await using var server = new JavaConnection(pipes.Right, FrameMode());
        client.BindCodec(binding: null, PacketFlow.Clientbound);
        server.BindCodec(binding: null, PacketFlow.Serverbound);
        client.Start();
        server.Start();

        List<(PacketFlow Flow, int WireId, byte[] Payload)> seen = [];
        void Record(PacketObservation obs)
        {
            lock (seen)
                seen.Add((obs.Flow, obs.WireId, obs.CopyPayload()));

        }

        client.PacketObserved += Record;
        client.OutboundPacketObserved += Record;

        byte[] request = [0x07, 0x07];
        byte[] response = [0x63, 0x64, 0x65];
        await client.SendFrameAsync(0x03, request, Ct());
        InboundItem serverGot = await server.ReceiveAsync(Ct());
        Assert.Equal(0x03, serverGot.Frame.WireId);

        await server.SendFrameAsync(0x40, response, Ct());
        InboundItem clientGot = await client.ReceiveAsync(Ct());
        Assert.Equal(0x40, clientGot.Frame.WireId);

        (PacketFlow Flow, int WireId, byte[] Payload)[] snapshot;
        lock (seen)
            snapshot = [.. seen];

        Assert.Contains(snapshot, e => e.Flow == PacketFlow.Serverbound && e.WireId == 0x03 && e.Payload.SequenceEqual(request));
        Assert.Contains(snapshot, e => e.Flow == PacketFlow.Clientbound && e.WireId == 0x40 && e.Payload.SequenceEqual(response));
    }

    [Fact]
    public async Task Outbound_With_No_Listener_Does_No_Observation_Work()
    {
        // The observation must not be built when nobody is listening. Proven by a listener attached part-way through: it sees exactly the frames sent after it attached, and none before.
        DuplexPipePair pipes = DuplexPipePair.Create();
        await using var client = new JavaConnection(pipes.Left, FrameMode());
        client.BindCodec(binding: null, PacketFlow.Clientbound);
        client.Start();

        for (int i = 0; i < 8; i++)
            await client.SendFrameAsync(i, new byte[] { (byte)i }, Ct());

        List<int> observedIds = [];
        client.OutboundPacketObserved += obs => observedIds.Add(obs.WireId);

        for (int i = 8; i < 12; i++)
            await client.SendFrameAsync(i, new byte[] { (byte)i }, Ct());

        Assert.Equal([8, 9, 10, 11], observedIds);
    }

    [Fact]
    public async Task Outbound_With_No_Listener_Allocates_Nothing_Extra()
    {
        // The cost contract, measured. Three identical batches of sends on one thread:
        //   (a) no listener,
        //   (b) a listener that reads the id only,
        //   (c) a listener that copies the payload.
        // (b) must cost what (a) costs: the observation is a struct over a buffer that already exists, and nothing is copied unless asked. (c) must cost the bytes it copied on top, which is what makes the measurement meaningful rather than a tautology: if the probe could not see a real per-frame allocation it could not claim (a) and (b) have none.
        //
        // The send itself allocates: the pipe grows pooled segments to hold frames nobody drains, and how much of that comes from the shared pool rather than fresh memory depends on what else is running. That noise is additive and identical in shape across the three batches, so the assertions are on DIFFERENCES with a bound expressed as a fraction of the copy, sized well above the noise (a few KB per batch) and well below one copy per frame.
        const int Frames = 200;
        byte[] payload = new byte[4096];
        Random.Shared.NextBytes(payload);

        // A pipe roomy enough that no write ever parks, and the connection is never started, so nothing drains it. Every send then completes synchronously (uncontended write lock, no flush stall), which is asserted below. That matters for the measurement, not just for speed: GC.GetAllocatedBytesForCurrentThread is per-thread, so a send that parked and resumed on a pool thread would take its allocations out of the count and the result would depend on how busy the machine is.
        var roomy = new PipeOptions(
            pauseWriterThreshold: 64L * 1024 * 1024,
            resumeWriterThreshold: 32L * 1024 * 1024,
            useSynchronizationContext: false);

        async Task<long> BatchAsync(Action<PacketObservation>? listener)
        {
            DuplexPipePair pipes = DuplexPipePair.Create(roomy);
            await using var conn = new JavaConnection(pipes.Left, FrameMode());
            conn.BindCodec(binding: null, PacketFlow.Clientbound);
            if (listener is not null)
                conn.OutboundPacketObserved += listener;

            // Warm the send path so JIT and buffer growth are not counted.
            SendSynchronously(conn, payload);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < Frames; i++)
                SendSynchronously(conn, payload);

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        static void SendSynchronously(JavaConnection conn, byte[] payload)
        {
            ValueTask send = conn.SendFrameAsync(0x01, payload, CancellationToken.None);
            Assert.True(send.IsCompleted, "The send did not complete synchronously; the measurement would be thread-dependent.");
            send.GetAwaiter().GetResult();
        }

        long none = await BatchAsync(listener: null);
        long idOnly = await BatchAsync(obs => _ = obs.WireId);
        long copying = await BatchAsync(obs => _ = obs.CopyPayload());

        long copyCost = Frames * (long)payload.Length;
        long tolerance = copyCost / 8; // ~512 B/frame of slack against a 4096 B/frame copy.

        // The copying listener pays for its copies, and pays them on top of what the id-only listener costs. This is the calibration: it proves the probe can see a per-frame allocation at all.
        Assert.True(
            copying - idOnly >= copyCost * 9 / 10,
            $"Expected the copying listener to allocate about {copyCost} bytes more; idOnly={idOnly}, copying={copying}.");

        // Having no listener costs no more than having one that does not ask for bytes: the send path neither buffers nor copies a frame on its own.
        Assert.True(
            Math.Abs(idOnly - none) < tolerance,
            $"Observation is not allocation-free: none={none}, idOnly={idOnly} over {Frames} frames of {payload.Length} bytes.");
    }

    [Fact]
    public async Task Inbound_Observation_Still_Carries_Its_WireId_And_Bytes()
    {
        // Guards the offset added to PacketObservation for the outbound case: the inbound payload must still start at byte 0 of its own buffer.
        DuplexPipePair pipes = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pipes.Left, FrameMode());
        conn.BindCodec(binding: null, PacketFlow.Clientbound);

        int wireId = -1;
        byte[] captured = [];
        conn.PacketObserved += obs =>
        {
            wireId = obs.WireId;
            captured = obs.CopyPayload();
        };
        conn.Start();

        await FramingTests.WriteRawFrameAsync(pipes.Right.Output, [0x55, 0x66, 0x77, 0x00, 0x12]);
        InboundItem item = await conn.ReceiveAsync(Ct());

        Assert.Equal(0x55, item.Frame.WireId);
        Assert.Equal(0x55, wireId);
        Assert.Equal(new byte[] { 0x66, 0x77, 0x00, 0x12 }, captured);
    }
}
