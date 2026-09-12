using System.Collections.Concurrent;

namespace Umpk.Protocol.Java;

/// <summary>A per-connection cookie store: the small key/value bag the transfer/cookie bookkeeping uses. The client role answers <c>cookie_request</c> from this store and persists <c>store_cookie</c> into it; the server role reads cookies out of <c>cookie_response</c>. Cookies survive a <c>transfer</c> to another server, which is the whole point of the mechanism. Thread-safe because a connection's read loop and its session code may touch it concurrently.</summary>
public sealed class CookieStore
{
    private readonly ConcurrentDictionary<Identifier, byte[]> _cookies = new();

    /// <summary>Stores (or replaces) a cookie payload for a key.</summary>
    public void Set(Identifier key, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        _cookies[key] = payload;
    }

    /// <summary>Removes a cookie; a null payload from a <c>store_cookie</c>/response clears it.</summary>
    public void Remove(Identifier key) => _cookies.TryRemove(key, out _);

    /// <summary>Returns the stored payload for a key, or <see langword="null"/> when absent.</summary>
    public byte[]? Get(Identifier key) => _cookies.TryGetValue(key, out byte[]? value) ? value : null;

    /// <summary>True when a cookie is stored for the key.</summary>
    public bool Contains(Identifier key) => _cookies.ContainsKey(key);

    /// <summary>The number of stored cookies.</summary>
    public int Count => _cookies.Count;

    /// <summary>Returns an owned snapshot for a supervised transfer.</summary>
    public IReadOnlyDictionary<Identifier, byte[]> SnapshotOwned() =>
        _cookies.ToDictionary(static pair => pair.Key, static pair => pair.Value.ToArray());

    /// <summary>Installs owned cookie values into a fresh destination client.</summary>
    public void ImportOwned(IReadOnlyDictionary<Identifier, byte[]> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        foreach ((Identifier key, byte[] value) in snapshot)
            Set(key, value.ToArray());
    }

    /// <summary>
    /// An optional producer consulted BEFORE the stored value whenever a <c>cookie_request</c> is answered, in the configuration phase and the play phase alike. It exists because a cookie a proxy asks for on its first request cannot have been stored yet: the client can produce one from an external source at the moment it is requested.
    /// <para>Returning a payload within the protocol's 5120-byte limit STORES it under the key and answers with it, so a later request for the same key is already covered without asking again. An oversized result is unusable and falls back to the stored value without replacing it. Returning <see langword="null"/> also falls back to the stored value. Leaving this property null always uses the stored value.</para>
    /// <para>The resolver runs on the session loop while the server is blocked on the reply, so it must be prompt. A resolver that throws is reported by the caller and the stored value answers instead: an unanswered cookie request stalls a forwarding proxy forever, so no failure here may leave the reply unsent.</para>
    /// </summary>
    public Func<Identifier, CancellationToken, ValueTask<byte[]?>>? Resolver { get; set; }

    /// <summary>The payload to answer a <c>cookie_request</c> for <paramref name="key"/> with: the <see cref="Resolver"/>'s value when it produces one (stored on the way past), otherwise the stored value, otherwise <see langword="null"/> for a cookie this client does not hold.</summary>
    public async ValueTask<byte[]?> ResolveAsync(Identifier key, CancellationToken ct = default)
    {
        if (Resolver is { } resolver)
        {
            byte[]? produced = await resolver(key, ct).ConfigureAwait(false);
            if (produced is { Length: <= Codecs.LoginConfigWire.CookieMaxPayloadLength })
            {
                Set(key, produced);
                return produced;
            }
        }

        return Get(key);
    }
}
