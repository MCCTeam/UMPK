namespace Umpk.Game.Registries;

/// <summary>Accumulates entries and produces one immutable <see cref="Registry{T}"/>. The builder is the only way to construct a registry with a fluent flow; once <see cref="Build"/> is called the resulting registry never changes. A builder is single-use: reuse after building throws.</summary>
/// <typeparam name="T">The definition type.</typeparam>
public sealed class RegistryBuilder<T>
    where T : class
{
    private readonly Identifier _registryId;
    private readonly List<int> _networkIds;
    private readonly List<Identifier> _keys;
    private readonly List<T> _values;
    private readonly HashSet<int> _seenNetworkIds;
    private readonly HashSet<Identifier> _seenKeys;
    private bool _built;

    /// <summary>Creates a builder for the given registry id with an optional capacity hint.</summary>
    public RegistryBuilder(Identifier registryId, int capacity = 0)
    {
        _registryId = registryId;
        _networkIds = new List<int>(capacity);
        _keys = new List<Identifier>(capacity);
        _values = new List<T>(capacity);
        _seenNetworkIds = new HashSet<int>(capacity);
        _seenKeys = new HashSet<Identifier>(capacity);
    }

    /// <summary>The number of entries added so far.</summary>
    public int Count => _values.Count;

    /// <summary>Adds one entry.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException">The network id or key was already added.</exception>
    /// <exception cref="InvalidOperationException"><see cref="Build"/> has already run.</exception>
    public RegistryBuilder<T> Add(int networkId, Identifier key, T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (_built)
            throw new InvalidOperationException("This registry builder has already produced a registry.");

        if (!_seenNetworkIds.Add(networkId))
            throw new ArgumentException($"Duplicate network id {networkId} in registry '{_registryId}'.", nameof(networkId));

        if (!_seenKeys.Add(key))
            throw new ArgumentException($"Duplicate key '{key}' in registry '{_registryId}'.", nameof(key));

        _networkIds.Add(networkId);
        _keys.Add(key);
        _values.Add(value);
        return this;
    }

    /// <summary>Produces the immutable registry and seals the builder.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Build"/> has already run.</exception>
    public Registry<T> Build()
    {
        if (_built)
            throw new InvalidOperationException("This registry builder has already produced a registry.");

        _built = true;
        return new Registry<T>(_registryId, [.. _networkIds], [.. _keys], [.. _values]);
    }
}
