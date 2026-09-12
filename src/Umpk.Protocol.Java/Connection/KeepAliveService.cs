using System.Security.Cryptography;

namespace Umpk.Protocol.Java;

/// <summary>The role-symmetric keep-alive bookkeeping, packaged so both roles reuse it: the responder half (client role) echoes a request id verbatim, and the initiator half (server role) issues a fresh nonce on a cadence and expects it echoed before a deadline, disconnecting otherwise. The cadence and response deadline are both 15 s. The 30 s read-idle backstop is modeled separately as <c>JavaConnectionOptions.ReadIdleTimeout</c>. This carries only the timing/nonce state; the caller owns the actual send (play and configuration phases use different packet ids, so the service stays packet-agnostic).</summary>
public sealed class KeepAliveService
{
    private readonly TimeSpan _interval;

    private readonly TimeSpan _timeout;

    private long _pendingId;

    private bool _hasPending;

    private DateTimeOffset _lastSentAt;

    private DateTimeOffset _lastResponseAt;

    /// <summary>Creates a keep-alive service with a single 15 s window: cadence and response deadline are both 15 s. The 30 s read-idle backstop lives in <c>JavaConnectionOptions.ReadIdleTimeout</c>, not here.</summary>
    public KeepAliveService()
        : this(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15))
    {
    }

    /// <summary>Creates a keep-alive service with an explicit cadence and response window.</summary>
    public KeepAliveService(TimeSpan interval, TimeSpan timeout)
    {
        _interval = interval;
        _timeout = timeout;
        _lastResponseAt = DateTimeOffset.MinValue;
    }

    /// <summary>The nonce currently awaiting an echo, when <see cref="HasPending"/> is set.</summary>
    public long PendingId => _pendingId;

    /// <summary>True when an issued keep-alive has not yet been echoed.</summary>
    public bool HasPending => _hasPending;

    // Responder half

    /// <summary>Responder (client role): returns the id to echo back for a received keep-alive request. Kept as a method so the responder path reads symmetrically with the initiator path.</summary>
    public static long BuildResponse(long requestId) => requestId;

    // Initiator half

    /// <summary>Initiator (server role): if it is time to send a new keep-alive (cadence elapsed and none pending), issues a fresh nonce and marks it pending. Returns <see langword="true"/> and sets <paramref name="id"/> when the caller should send a keep-alive now.</summary>
    public bool TryIssue(DateTimeOffset now, out long id)
    {
        if (_hasPending || (now - _lastSentAt) < _interval)
        {
            id = 0;
            return false;
        }

        id = NextNonce();
        _pendingId = id;
        _hasPending = true;
        _lastSentAt = now;
        return true;
    }

    /// <summary>Initiator (server role): records an echoed response. Returns <see langword="true"/> when the id matches the outstanding nonce (a valid, expected echo); <see langword="false"/> for a stale or unexpected id (vanilla treats a mismatch as a protocol error).</summary>
    public bool AcceptResponse(long id, DateTimeOffset now)
    {
        if (_hasPending && id == _pendingId)
        {
            _hasPending = false;
            _lastResponseAt = now;
            return true;
        }

        return false;
    }

    /// <summary>Initiator (server role): true when an outstanding keep-alive has gone unanswered past the timeout window, meaning the connection should be closed as unresponsive.</summary>
    public bool IsTimedOut(DateTimeOffset now) => _hasPending && (now - _lastSentAt) >= _timeout;

    private static long NextNonce()
    {
        Span<byte> buffer = stackalloc byte[8];
        RandomNumberGenerator.Fill(buffer);
        return BitConverter.ToInt64(buffer);
    }
}
