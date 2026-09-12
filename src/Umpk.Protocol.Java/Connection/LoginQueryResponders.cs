using System.Collections.Concurrent;

namespace Umpk.Protocol.Java;

/// <summary>
/// The login-phase plugin channels this client CLAIMS, keyed by channel identifier.
/// <para>A server may send <c>minecraft:custom_query</c> during login on any channel it likes; a proxy uses it for modern forwarding (<c>velocity:player_info</c>), and a mod loader uses it for its handshake (<c>fml:loginwrapper</c>). Vanilla's answer for a channel the client does not know is <c>understood = false</c>, which is also the answer for every unregistered channel.</para>
/// <para>A responder receives the query's payload (the bytes after the channel identifier) and returns the bytes to answer with, or <see langword="null"/> to fall back to "not understood". Returning an EMPTY (but non-null) payload is a real answer with no body, which is not the same thing on the wire.</para>
/// <para>THREADING AND TIMING. Responders run on the login driver's own read loop, in frame order, and the driver awaits each one before reading the next frame: the server is blocked on the answer, so a responder must be prompt. A responder that throws is logged and answered as "not understood", so a faulty claim is reported as not understood instead of terminating login.</para>
/// </summary>
public sealed class LoginQueryResponders
{
    private readonly ConcurrentDictionary<Identifier, Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<ReadOnlyMemory<byte>?>>> _responders = new();

    /// <summary>A snapshot of the channels currently claimed.</summary>
    public IReadOnlyCollection<Identifier> Channels => [.. _responders.Keys];

    /// <summary>How many channels are currently claimed.</summary>
    public int Count => _responders.Count;

    /// <summary>Claims <paramref name="channel"/>. Dispose the returned handle to release it; disposing releases only this registration, and a handle whose channel has already been re-claimed by someone else releases nothing.</summary>
    /// <exception cref="ArgumentException">The channel is already claimed. Two responders on one channel cannot both answer a query that carries a single transaction id, so the conflict is named here rather than resolved by an arbitrary rule; ask <see cref="IsClaimed"/> first when a caller can share the channel.</exception>
    public IDisposable Register(
        Identifier channel,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<ReadOnlyMemory<byte>?>> responder)
    {
        ArgumentNullException.ThrowIfNull(responder);
        if (!_responders.TryAdd(channel, responder))
            throw new ArgumentException(
                $"The login query channel '{channel}' is already claimed by another responder.", nameof(channel));

        return new Registration(this, channel, responder);
    }

    /// <summary>True when a responder is registered for <paramref name="channel"/>.</summary>
    public bool IsClaimed(Identifier channel) => _responders.ContainsKey(channel);

    /// <summary>Releases every claim. Called by the client at session end so a responder holding session state cannot answer for the next one; a caller that reconnects the same client re-registers.</summary>
    public void Clear() => _responders.Clear();

    /// <summary>The responder claiming <paramref name="channel"/>, if any.</summary>
    internal bool TryGetResponder(
        Identifier channel,
        out Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<ReadOnlyMemory<byte>?>>? responder)
        => _responders.TryGetValue(channel, out responder);

    private void Release(
        Identifier channel,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<ReadOnlyMemory<byte>?>> responder)
    {
        // The pair overload, not Remove(key): between this handle's registration and its disposal the channel may have been cleared and re-claimed by someone else, and disposing a stale handle must not silently unregister the live claim.
        ICollection<KeyValuePair<Identifier, Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<ReadOnlyMemory<byte>?>>>> collection = _responders;
        collection.Remove(new KeyValuePair<Identifier, Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<ReadOnlyMemory<byte>?>>>(channel, responder));
    }

    private sealed class Registration : IDisposable
    {
        private readonly LoginQueryResponders _owner;

        private readonly Identifier _channel;

        private readonly Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<ReadOnlyMemory<byte>?>> _responder;

        private int _disposed;

        public Registration(
            LoginQueryResponders owner,
            Identifier channel,
            Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<ReadOnlyMemory<byte>?>> responder)
        {
            _owner = owner;
            _channel = channel;
            _responder = responder;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _owner.Release(_channel, _responder);

        }
    }
}
