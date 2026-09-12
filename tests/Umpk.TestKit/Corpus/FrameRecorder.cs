using System.Diagnostics;
using Umpk.Protocol.Java;

namespace Umpk.TestKit.Corpus;

/// <summary>Collects pre-decode frames from one or more <see cref="JavaConnection"/>s into a list of <see cref="RecordedFrame"/>s by subscribing to <see cref="JavaConnection.PacketObserved"/>. The recorded unit is the decrypted, decompressed frame payload (wire id + body) with direction/phase/timing metadata. Bytes are copied inside the observer callback because the observation's backing buffer is pooled and valid only for the callback's duration.</summary>
/// <remarks>
/// <para>A client connection's <c>PacketObserved</c> only fires for inbound (clientbound) frames: the transport raises observations from its read loop, not from the send path. To record serverbound frames as well, either attach the recorder to a proxy that reads both directions, or (in self-tests) attach one recorder per side of a <see cref="DuplexPipePair"/>. The recorder itself is direction-agnostic and records whatever a connection observes.</para>
/// <para>Thread-safe: observations may arrive on the connection's read-loop thread.</para>
/// </remarks>
public sealed class FrameRecorder : IDisposable
{
    private readonly object _gate = new();
    private readonly List<RecordedFrame> _frames = [];
    private readonly List<(JavaConnection Connection, Action<PacketObservation> Handler)> _subscriptions = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _sequence;

    /// <summary>The number of frames recorded so far.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
                return _frames.Count;

        }
    }

    /// <summary>Subscribes to a connection's frame observations.</summary>
    public void Attach(JavaConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        void Handler(PacketObservation obs) => Record(obs);
        connection.PacketObserved += Handler;
        lock (_gate)
            _subscriptions.Add((connection, Handler));

    }

    /// <summary>Records a single observation (copies the pooled payload immediately).</summary>
    public void Record(PacketObservation obs)
    {
        byte[] body = obs.RawPayload.ToArray();
        long timestamp = _clock.ElapsedTicks;
        lock (_gate)
        {
            long seq = _sequence++;
            _frames.Add(new RecordedFrame(
                seq,
                CorpusEnumMapping.ToDirection(obs.Flow),
                CorpusEnumMapping.ToPhase(obs.Phase),
                obs.WireId,
                body,
                timestamp));
        }
    }

    /// <summary>Returns a snapshot of the recorded frames in order.</summary>
    public IReadOnlyList<RecordedFrame> Snapshot()
    {
        lock (_gate)
            return _frames.ToArray();

    }

    /// <summary>Unsubscribes from all connections.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            foreach ((JavaConnection connection, Action<PacketObservation> handler) in _subscriptions)
                connection.PacketObserved -= handler;

            _subscriptions.Clear();
        }
    }
}
