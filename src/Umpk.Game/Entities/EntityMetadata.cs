using Umpk.Game.Registries;

namespace Umpk.Game.Entities;

/// <summary>
/// The two-tier entity-metadata surface.
/// <para>Tier 1 is a complete, typed, index-keyed store: every field the wire delivers is stored by its numeric index as a <see cref="MetadataValue"/>, and is readable with no dataset help at all The Java data/codec layer's metadata codecs feed it through <see cref="Set(int, MetadataValue)"/>.</para>
/// <para>Tier 2 is semantic resolution: a <see cref="MetadataKey{T}"/> resolves, through the bound <see cref="IMetadataKeySource"/> and the owning entity type, to a tier-1 index and a typed projection. A tier-2 miss (no source, unmapped key, or absent index) never throws; it returns false and the default.</para>
/// </summary>
public sealed class EntityMetadata
{
    private readonly Dictionary<int, MetadataValue> _values = [];
    private readonly RegistryEntry<EntityTypeDefinition> _entityType;
    private readonly IMetadataKeySource? _keySource;

    /// <summary>Creates a tier-1-only metadata store (no semantic key resolution).</summary>
    /// <param name="entityType">The owning entity type (used only by tier-2 resolution).</param>
    public EntityMetadata(RegistryEntry<EntityTypeDefinition> entityType)
        : this(entityType, null)
    {
    }

    /// <summary>Creates a metadata store with an optional tier-2 key source.</summary>
    /// <param name="entityType">The owning entity type, resolved against the key source in tier 2.</param>
    /// <param name="keySource">The tier-2 resolution seam, or null for tier-1-only.</param>
    public EntityMetadata(RegistryEntry<EntityTypeDefinition> entityType, IMetadataKeySource? keySource)
    {
        _entityType = entityType;
        _keySource = keySource;
    }

    /// <summary>The number of tier-1 entries currently stored.</summary>
    public int Count => _values.Count;

    /// <summary>The tier-1 indices currently present, in no defined order.</summary>
    public IReadOnlyCollection<int> Indices => _values.Keys;

    /// <summary>True when a tier-2 key source is bound to this store.</summary>
    public bool HasKeySource => _keySource is not null;

    /// <summary>Tier-1 write: stores <paramref name="value"/> at <paramref name="index"/>, overwriting any existing entry. This is the entry point the Java data/codec layer's metadata decoders call per field.</summary>
    public void Set(int index, MetadataValue value) => _values[index] = value;

    /// <summary>Tier-1 read: gets the raw value at <paramref name="index"/> when present.</summary>
    public bool TryGet(int index, out MetadataValue value) => _values.TryGetValue(index, out value);

    /// <summary>Tier-1 presence check.</summary>
    public bool Contains(int index) => _values.ContainsKey(index);

    /// <summary>Tier-1 removal; returns true when an entry was removed.</summary>
    public bool Remove(int index) => _values.Remove(index);

    /// <summary>Removes every tier-1 entry.</summary>
    public void Clear() => _values.Clear();

    /// <summary>Enumerates the tier-1 entries as (index, value) pairs, in no defined order.</summary>
    public IEnumerable<KeyValuePair<int, MetadataValue>> Entries => _values;

    /// <summary>Tier-2 resolution WITHOUT the projection: resolves <paramref name="key"/> to its tier-1 index for the owning entity type and returns the raw stored value. Returns false when no source is bound, the key is unmapped, or the index is absent.</summary>
    /// <remarks>Needed where a field's wire TYPE changes across eras while its meaning does not: the custom name is a string before 1.13 and an optional component from 1.13, so a reader has to branch on the stored kind rather than commit to one projection. A key's own projection covers a single kind and reports the others as a graceful miss, which would silently lose the pre-1.13 form.</remarks>
    public bool TryResolve(MetadataKey key, out MetadataValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        value = default;
        return _keySource is not null
            && _keySource.TryResolveIndex(_entityType, key, out int index)
            && _values.TryGetValue(index, out value);
    }

    /// <summary>Tier-2 read: resolves <paramref name="key"/> for the owning entity type through the bound key source, reads the tier-1 value at the resolved index, and projects it. Returns false (with <paramref name="value"/> set to default) when no source is bound, the key is unmapped for this entity type, the resolved index is absent, or the projection throws <see cref="InvalidOperationException"/> because the stored kind does not match the key.</summary>
    public bool TryGet<T>(MetadataKey<T> key, out T value)
    {
        ArgumentNullException.ThrowIfNull(key);
        value = default!;

        if (_keySource is null || !_keySource.TryResolveIndex(_entityType, key, out int index))
            return false;

        if (!_values.TryGetValue(index, out MetadataValue raw))
            return false;

        try
        {
            value = key.Project(raw);
            return true;
        }
        catch (InvalidOperationException)
        {
            // Kind mismatch between the stored tier-1 value and the key's projection: treat as a graceful miss rather than propagating, so a dataset/version skew never crashes a read.
            value = default!;
            return false;
        }
    }
}
