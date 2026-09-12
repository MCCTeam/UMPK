using System.Collections.Concurrent;
using Umpk.Auth.Persistence;

namespace Umpk.Auth;

/// <summary>A non-persistent <see cref="ITokenStore"/> backed by a concurrent dictionary. Round-trips values through the same closed-type serialization as the file store so tests exercise the real contract, but nothing touches disk. Intended for tests and ephemeral hosts.</summary>
public sealed class InMemoryTokenStore : ITokenStore
{
    private readonly ConcurrentDictionary<string, byte[]> _entries = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask<T?> GetAsync<T>(string key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        ct.ThrowIfCancellationRequested();
        if (!PersistedJson.IsSupported<T>())
            return ValueTask.FromException<T?>(new NotSupportedException(
                "InMemoryTokenStore persists only Umpk.Auth's own types."));

        if (_entries.TryGetValue(key, out byte[]? bytes))
            return ValueTask.FromResult(PersistedJson.Deserialize<T>(bytes));

        return ValueTask.FromResult<T?>(default);
    }

    /// <inheritdoc />
    public ValueTask SetAsync<T>(string key, T value, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        ct.ThrowIfCancellationRequested();
        _entries[key] = PersistedJson.Serialize(value);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask RemoveAsync(string key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        ct.ThrowIfCancellationRequested();
        _entries.TryRemove(key, out _);
        return ValueTask.CompletedTask;
    }
}
