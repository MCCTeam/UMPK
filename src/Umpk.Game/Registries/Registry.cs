using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Umpk.Game.Registries;

/// <summary>An immutable, network-id-keyed registry of <typeparamref name="T"/> definitions, mirroring the game's own registry model. Entries are stored in contiguous arrays and addressed by slot, so a generated, blob-backed data source can populate one without per-entry allocation. Build a registry with <see cref="RegistryBuilder{T}"/> or the <see cref="Registry"/> factory; there is no mutation after construction and no global registry anywhere.</summary>
/// <typeparam name="T">The definition type.</typeparam>
public sealed class Registry<T> : IRegistry, IReadOnlyCollection<RegistryEntry<T>>
    where T : class
{
    // Parallel arrays indexed by slot (0..Count-1). Slots keep insertion order.
    private readonly int[] _networkIds;
    private readonly Identifier[] _keys;
    private readonly T[] _values;

    // Slot lookups. _denseByNetworkId is set when network ids are exactly 0..Count-1 in order, letting the network-id indexer skip the dictionary; otherwise _networkIdToSlot is used.
    private readonly bool _denseByNetworkId;
    private readonly Dictionary<int, int>? _networkIdToSlot;
    private readonly Dictionary<Identifier, int> _keyToSlot;

    internal Registry(Identifier registryId, int[] networkIds, Identifier[] keys, T[] values)
    {
        RegistryId = registryId;
        _networkIds = networkIds;
        _keys = keys;
        _values = values;

        _keyToSlot = new Dictionary<Identifier, int>(keys.Length);
        bool dense = true;
        for (int slot = 0; slot < networkIds.Length; slot++)
        {
            if (networkIds[slot] != slot)
                dense = false;

            // Builder guarantees uniqueness, so indexer assignment is safe.
            _keyToSlot[keys[slot]] = slot;
        }

        _denseByNetworkId = dense;
        if (!dense)
        {
            _networkIdToSlot = new Dictionary<int, int>(networkIds.Length);
            for (int slot = 0; slot < networkIds.Length; slot++)
                _networkIdToSlot[networkIds[slot]] = slot;

        }
    }

    /// <inheritdoc/>
    public Identifier RegistryId { get; }

    /// <inheritdoc/>
    public int Count => _values.Length;

    /// <summary>The entry with the given network id.</summary>
    /// <exception cref="KeyNotFoundException">No entry has the given network id.</exception>
    public RegistryEntry<T> this[int networkId] =>
        TryGet(networkId, out var entry)
            ? entry
            : throw new KeyNotFoundException($"Registry '{RegistryId}' has no entry with network id {networkId}.");

    /// <summary>The entry with the given key.</summary>
    /// <exception cref="KeyNotFoundException">No entry has the given key.</exception>
    public RegistryEntry<T> this[Identifier id] =>
        TryGet(id, out var entry)
            ? entry
            : throw new KeyNotFoundException($"Registry '{RegistryId}' has no entry with key '{id}'.");

    /// <summary>Resolves the entry with the given network id.</summary>
    public bool TryGet(int networkId, out RegistryEntry<T> entry)
    {
        int slot;
        if (_denseByNetworkId)
        {
            if (networkId < 0 || networkId >= _values.Length)
            {
                entry = default;
                return false;
            }

            slot = networkId;
        }
        else if (!_networkIdToSlot!.TryGetValue(networkId, out slot))
        {
            entry = default;
            return false;
        }

        entry = EntryAt(slot);
        return true;
    }

    /// <summary>Resolves the entry with the given key.</summary>
    public bool TryGet(Identifier id, out RegistryEntry<T> entry)
    {
        if (_keyToSlot.TryGetValue(id, out int slot))
        {
            entry = EntryAt(slot);
            return true;
        }

        entry = default;
        return false;
    }

    /// <summary>Resolves the definition value for the given key.</summary>
    public bool TryGetValue(Identifier id, [MaybeNullWhen(false)] out T value)
    {
        if (_keyToSlot.TryGetValue(id, out int slot))
        {
            value = _values[slot];
            return true;
        }

        value = null;
        return false;
    }

    /// <inheritdoc/>
    public bool ContainsNetworkId(int networkId) => TryGet(networkId, out _);

    /// <inheritdoc/>
    public bool ContainsKey(Identifier id) => _keyToSlot.ContainsKey(id);

    /// <inheritdoc/>
    public bool TryGetKey(int networkId, out Identifier id)
    {
        if (TryGet(networkId, out var entry))
        {
            id = entry.Id;
            return true;
        }

        id = default;
        return false;
    }

    /// <inheritdoc/>
    public bool TryGetNetworkId(Identifier id, out int networkId)
    {
        if (_keyToSlot.TryGetValue(id, out int slot))
        {
            networkId = _networkIds[slot];
            return true;
        }

        networkId = default;
        return false;
    }

    /// <summary>Enumerates entries in insertion order.</summary>
    public IEnumerator<RegistryEntry<T>> GetEnumerator()
    {
        for (int slot = 0; slot < _values.Length; slot++)
            yield return EntryAt(slot);

    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private RegistryEntry<T> EntryAt(int slot) => new(_networkIds[slot], _keys[slot], _values[slot]);
}

/// <summary>Factory helpers for building registries without a fluent builder.</summary>
public static class Registry
{
    /// <summary>Creates a standalone entry that no registry owns. A decoder uses this to degrade a wire id its registry cannot resolve into a clearly named placeholder while keeping the original network id, so re-encoding the value stays byte-exact. The entry is never returned by a lookup on any registry.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static RegistryEntry<T> Direct<T>(int networkId, Identifier id, T value)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        return new RegistryEntry<T>(networkId, id, value);
    }

    /// <summary>Builds a registry from parallel spans of network ids, keys and definition values. The three spans must have equal length; network ids and keys must each be unique.</summary>
    /// <exception cref="ArgumentException">Lengths differ, or a network id or key repeats.</exception>
    public static Registry<T> FromEntries<T>(Identifier registryId, ReadOnlySpan<int> networkIds, ReadOnlySpan<Identifier> keys, ReadOnlySpan<T> values)
        where T : class
    {
        if (networkIds.Length != keys.Length || keys.Length != values.Length)
            throw new ArgumentException("networkIds, keys and values must have equal length.");

        var builder = new RegistryBuilder<T>(registryId, networkIds.Length);
        for (int i = 0; i < values.Length; i++)
            builder.Add(networkIds[i], keys[i], values[i]);

        return builder.Build();
    }
}
