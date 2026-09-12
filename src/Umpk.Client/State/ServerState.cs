using Umpk.Geometry;

namespace Umpk.Client.State;

/// <summary>The world spawn point a server announces: the block position plus the spawn yaw.</summary>
/// <param name="Position">The spawn block position.</param>
/// <param name="Angle">The spawn yaw in degrees; zero on versions before 1.16, which do not carry it.</param>
public readonly record struct WorldSpawnPoint(BlockPos Position, float Angle);

/// <summary>What the session has learned about the server itself: its brand, its measured tick rate, and the keep-alive exchange. Mutated only on the session loop by appliers; readable off-loop as an eventually-consistent snapshot.</summary>
public sealed class ServerState
{
    /// <summary>How long a tick-rate sample stays usable. A vanilla server broadcasts game time every 20 ticks (one second at full speed), so a gap several times that means the server stopped ticking, and the honest answer is "unknown" rather than a stale or zero rate.</summary>
    internal static readonly TimeSpan TickSampleLifetime = TimeSpan.FromSeconds(10);

    /// <summary>The number of recent samples averaged into <see cref="TicksPerSecond"/>.</summary>
    private const int TickSampleWindow = 5;

    /// <summary>A server cannot sustain more than 20 ticks per second, but one catching up after a stall can briefly report more. Clamp so a catch-up burst does not surface as an impossible rate.</summary>
    private const double MaxTicksPerSecond = 20.0;

    private readonly Queue<double> _tickSamples = new();
    private long _lastGameTime;
    private long _lastGameTimeStamp;
    private bool _hasTickAnchor;
    private long _lastSampleStamp;

    /// <summary>The server brand, for example <c>vanilla</c> or <c>Paper</c>. Null until the server announces it on the <c>minecraft:brand</c> plugin channel (<c>MC|Brand</c> before 1.13).</summary>
    public string? Brand { get; internal set; }

    /// <summary>The world spawn point the server last announced (<c>set_default_spawn_position</c>), or null before it announces one. This is the compass target and the respawn anchor, not the player's own position.</summary>
    /// <remarks>Held here rather than on the world because the packet can arrive before the world exists during the join sequence. The terrain applier records and publishes that early announcement before its world-availability gate.</remarks>
    public WorldSpawnPoint? WorldSpawn { get; internal set; }

    /// <summary>
    /// The measured server tick rate, or null when it is not known: before the second game-time update arrives, and again whenever the server stops ticking.
    /// <para>Derived from the game time the server broadcasts every 20 ticks: the reported tick delta over the measured wall-clock delta. A server that stops ticking stops broadcasting, so this returns to null rather than falling to zero. That covers the 1.21.2+ pause-when-empty behavior, <c>/tick freeze</c>, and a hung server alike: the client genuinely cannot tell a paused server from an unreachable one, and reporting "0 TPS" would claim knowledge it does not have.</para>
    /// </summary>
    public double? TicksPerSecond { get; private set; }

    /// <summary>The id of the most recent clientbound keep-alive, or null before the first one. On a vanilla server this is the server's own millisecond clock reading at send time, not a counter, so it is not comparable against any client clock.</summary>
    public long? LastKeepAliveId { get; internal set; }

    /// <summary>
    /// How long the client took to answer the most recent keep-alive: from decoding the request to handing the response to the transport.
    /// <para>This is the client's own turnaround, NOT the network round trip. A Java client cannot measure the keep-alive round trip: it is only ever the responder, and the round trip is measured on the server (<c>latency = (latency * 3 + elapsed) / 4</c>) and reaches the client only as the tab-list latency field. Use <see cref="ObservedLatency"/> for the network figure and this for detecting a stalled session loop, which is what causes keep-alive timeout disconnects.</para>
    /// </summary>
    public TimeSpan? KeepAliveTurnaround { get; private set; }

    /// <summary>The interval between the two most recent clientbound keep-alives, or null before the second one. A vanilla server aims for 15 seconds (<c>LATENCY_CHECK_INTERVAL</c>), so a materially longer gap means the server is behind on its own network tick.</summary>
    public TimeSpan? KeepAliveInterval { get; private set; }

    /// <summary>The server-measured round trip for this client, in milliseconds, as last reported in the tab list. This is the authoritative latency figure: the server times its own keep-alive round trip and publishes the smoothed result. Null until the server reports one.</summary>
    public int? ObservedLatency { get; internal set; }

    private long _lastKeepAliveStamp;
    private bool _hasKeepAliveAnchor;

    /// <summary>Records a game-time broadcast and updates the tick-rate estimate. <paramref name="timestamp"/> is a monotonic reading (<see cref="TimeProvider.GetTimestamp"/>). Session-loop only.</summary>
    internal void RecordGameTime(long gameTime, long timestamp, TimeProvider time)
    {
        if (!_hasTickAnchor)
        {
            Anchor(gameTime, timestamp);
            return;
        }

        long tickDelta = gameTime - _lastGameTime;
        TimeSpan elapsed = time.GetElapsedTime(_lastGameTimeStamp, timestamp);
        Anchor(gameTime, timestamp);

        // A non-positive tick delta is not a zero-TPS reading: it means the world clock did not advance between broadcasts (a frozen tick manager) or went backwards (a dimension change or an operator setting the time). Re-anchor and wait for a real interval rather than inventing a rate.
        if (tickDelta <= 0 || elapsed <= TimeSpan.Zero)
            return;

        double sample = Math.Min(tickDelta / elapsed.TotalSeconds, MaxTicksPerSecond);
        _tickSamples.Enqueue(sample);
        while (_tickSamples.Count > TickSampleWindow)
            _tickSamples.Dequeue();

        _lastSampleStamp = timestamp;
        TicksPerSecond = _tickSamples.Average();
    }

    /// <summary>Expires the tick-rate estimate once the server has gone quiet for longer than a broadcast gap can explain. Called on the session tick so a paused or hung server reads as unknown promptly instead of only at the next broadcast (which, for a paused server, never comes). Session-loop only.</summary>
    internal void ExpireStaleTickRate(long now, TimeProvider time)
    {
        if (TicksPerSecond is null)
            return;

        if (time.GetElapsedTime(_lastSampleStamp, now) > TickSampleLifetime)
        {
            TicksPerSecond = null;
            _tickSamples.Clear();
            _hasTickAnchor = false;
        }
    }

    /// <summary>Records a clientbound keep-alive arrival. Session-loop only.</summary>
    internal void RecordKeepAliveReceived(long id, long timestamp, TimeProvider time)
    {
        LastKeepAliveId = id;
        KeepAliveInterval = _hasKeepAliveAnchor
            ? time.GetElapsedTime(_lastKeepAliveStamp, timestamp)
            : null;
        _lastKeepAliveStamp = timestamp;
        _hasKeepAliveAnchor = true;
    }

    /// <summary>Records that the keep-alive response has been sent. Session-loop only.</summary>
    internal void RecordKeepAliveAnswered(long timestamp, TimeProvider time)
    {
        if (_hasKeepAliveAnchor)
            KeepAliveTurnaround = time.GetElapsedTime(_lastKeepAliveStamp, timestamp);

    }

    /// <summary>Drops everything that belongs to one connection. A reconnect must not present the previous server's brand or tick rate as current. Session-loop only.</summary>
    internal void ResetForSessionEnd()
    {
        Brand = null;
        WorldSpawn = null;
        TicksPerSecond = null;
        LastKeepAliveId = null;
        KeepAliveTurnaround = null;
        KeepAliveInterval = null;
        ObservedLatency = null;
        _tickSamples.Clear();
        _hasTickAnchor = false;
        _hasKeepAliveAnchor = false;
    }

    private void Anchor(long gameTime, long timestamp)
    {
        _lastGameTime = gameTime;
        _lastGameTimeStamp = timestamp;
        _hasTickAnchor = true;
    }
}
