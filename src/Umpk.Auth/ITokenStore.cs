namespace Umpk.Auth;

/// <summary>The single cache abstraction for persisted auth artifacts (sessions, certificates, refresh records), keyed by an opaque string. The default file store serializes exactly the library's own persisted types through a source-generated JSON context and throws for unknown value types; hosts that persist custom types implement their own store.</summary>
public interface ITokenStore
{
    /// <summary>Reads the value stored under <paramref name="key"/>, or null when absent or unreadable.</summary>
    ValueTask<T?> GetAsync<T>(string key, CancellationToken ct);

    /// <summary>Writes <paramref name="value"/> under <paramref name="key"/>, replacing any existing entry.</summary>
    ValueTask SetAsync<T>(string key, T value, CancellationToken ct);

    /// <summary>Removes the entry under <paramref name="key"/> if present.</summary>
    ValueTask RemoveAsync(string key, CancellationToken ct);
}
