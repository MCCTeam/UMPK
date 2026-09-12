using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Umpk.Game.Items;

/// <summary>The unified item-metadata surface. A map is an item-type <em>prototype</em> (the item's default components) plus a per-stack <em>patch</em> of add/override and remove entries. The effective view a consumer reads is the prototype merged with the patch; the patch alone is what the 1.20.5+ wire carries. Consumers do not need to handle the per-version representation split.</summary>
/// <remarks>The map is immutable. <see cref="With{T}"/> and <see cref="Without{T}"/> return a new map whose prototype is shared and whose patch is a fresh dictionary, so functional updates never mutate a value that flowed into an event.</remarks>
public sealed class DataComponentMap : IEnumerable<DataComponentEntry>, IEquatable<DataComponentMap>
{
    private static readonly IReadOnlyDictionary<DataComponentType, object> EmptyPrototype =
        new Dictionary<DataComponentType, object>();

    private readonly IReadOnlyDictionary<DataComponentType, object> _prototype;

    // Patch entries. A null value marks a removal of the prototype's component; a non-null value adds or overrides. Absence from this dictionary means "defer to prototype".
    private readonly IReadOnlyDictionary<DataComponentType, object?> _patch;

    private DataComponentMap(
        IReadOnlyDictionary<DataComponentType, object> prototype,
        IReadOnlyDictionary<DataComponentType, object?> patch)
    {
        _prototype = prototype;
        _patch = patch;
    }

    /// <summary>An empty map with no prototype and no patch.</summary>
    public static DataComponentMap Empty { get; } = new(EmptyPrototype, new Dictionary<DataComponentType, object?>());

    /// <summary>The number of components in the effective view.</summary>
    public int Count
    {
        get
        {
            int count = 0;
            foreach (DataComponentType type in EffectiveKeys())
            {
                _ = type;
                count++;
            }

            return count;
        }
    }

    /// <summary>True when the effective view carries no components.</summary>
    public bool IsEmpty => !EffectiveKeys().GetEnumerator().MoveNext();

    /// <summary>True when the patch carries no entries (the map equals its prototype).</summary>
    public bool HasNoPatch => _patch.Count == 0;

    /// <summary>Builds a map from an item-type prototype and a patch. The prototype is the item's default components; each patch entry adds/overrides or, when a removal, hides the matching prototype component. The patch is canonicalized: a set entry equal to the prototype default is dropped, and a removal for a component the prototype does not carry is dropped, so equal maps always have equal patches.</summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static DataComponentMap Create(
        IReadOnlyDictionary<DataComponentType, object> prototype,
        IReadOnlyList<DataComponentEntry> patch)
    {
        ArgumentNullException.ThrowIfNull(prototype);
        ArgumentNullException.ThrowIfNull(patch);

        var patchMap = new Dictionary<DataComponentType, object?>();
        foreach (DataComponentEntry entry in patch)
        {
            bool hasDefault = prototype.TryGetValue(entry.Type, out object? defaultValue);
            if (entry.IsRemoval)
            {
                // A removal only means something when the prototype carries the component.
                if (hasDefault)
                    patchMap[entry.Type] = null;

                else
                    patchMap.Remove(entry.Type);

            }
            else if (hasDefault && defaultValue!.Equals(entry.Value))
            {
                // Setting the prototype default: the patch carries nothing.
                patchMap.Remove(entry.Type);
            }
            else
                patchMap[entry.Type] = entry.Value;

        }

        return new DataComponentMap(prototype, patchMap);
    }

    /// <summary>Builds a map with only a prototype and an empty patch.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="prototype"/> is null.</exception>
    public static DataComponentMap FromPrototype(IReadOnlyDictionary<DataComponentType, object> prototype)
    {
        ArgumentNullException.ThrowIfNull(prototype);
        return new DataComponentMap(prototype, new Dictionary<DataComponentType, object?>());
    }

    /// <summary>Attempts to read the effective value of a component.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is null.</exception>
    public bool TryGet<T>(DataComponentType<T> type, [NotNullWhen(true)] out T? value)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(type);
        if (_patch.TryGetValue(type, out object? patched))
        {
            // Present in the patch: either an override (non-null) or a removal (null).
            value = patched as T;
            return patched is not null;
        }

        if (_prototype.TryGetValue(type, out object? proto))
        {
            value = (T)proto;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>Reads the effective value of a component, or null when absent.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is null.</exception>
    public T? Get<T>(DataComponentType<T> type)
        where T : class => TryGet(type, out T? value) ? value : null;

    /// <summary>True when the component is present in the effective view.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is null.</exception>
    public bool Has<T>(DataComponentType<T> type)
        where T : class => TryGet(type, out _);

    /// <summary>Returns a copy with the component set to <paramref name="value"/>. If the value equals the prototype's default, the patch entry is dropped so the map stays minimal (the wire would carry nothing), matching vanilla patch minimization.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> or <paramref name="value"/> is null.</exception>
    public DataComponentMap With<T>(DataComponentType<T> type, T value)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(value);

        var next = new Dictionary<DataComponentType, object?>(_patch);
        if (_prototype.TryGetValue(type, out object? proto) && proto.Equals(value))
        {
            // Setting back to the prototype default: the patch no longer needs an entry.
            next.Remove(type);
        }
        else
            next[type] = value;

        return new DataComponentMap(_prototype, next);
    }

    /// <summary>Returns a copy with the component removed from the effective view. If the prototype carries the component the patch records a removal marker; otherwise any override entry is simply dropped.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is null.</exception>
    public DataComponentMap Without<T>(DataComponentType<T> type)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(type);
        var next = new Dictionary<DataComponentType, object?>(_patch);
        if (_prototype.ContainsKey(type))
        {
            next[type] = null; // explicit removal marker
        }
        else
            next.Remove(type);

        return new DataComponentMap(_prototype, next);
    }

    /// <summary>The patch entries, in an unspecified but stable order: the add/override and removal entries that differ from the prototype. This is what the 1.20.5+ wire carries and what Java codecs encode. A removal is emitted only when the prototype actually carries the component.</summary>
    public IReadOnlyList<DataComponentEntry> Patch
    {
        get
        {
            var list = new List<DataComponentEntry>(_patch.Count);
            foreach (KeyValuePair<DataComponentType, object?> pair in _patch)
            {
                if (pair.Value is null)
                {
                    // Only a real removal (prototype has it) is a wire removal entry.
                    if (_prototype.ContainsKey(pair.Key))
                        list.Add(DataComponentEntry.Remove(pair.Key));

                }
                else
                    list.Add(DataComponentEntry.Set(pair.Key, pair.Value));

            }

            return list;
        }
    }

    /// <summary>The effective component entries (prototype merged with patch), each as a set entry.</summary>
    public IEnumerable<DataComponentEntry> Effective
    {
        get
        {
            foreach (DataComponentType type in EffectiveKeys())
                yield return DataComponentEntry.Set(type, EffectiveValue(type)!);

        }
    }

    private IEnumerable<DataComponentType> EffectiveKeys()
    {
        foreach (KeyValuePair<DataComponentType, object> proto in _prototype)
        {
            // Prototype key survives unless the patch removed it.
            if (!_patch.TryGetValue(proto.Key, out object? patched) || patched is not null)
                yield return proto.Key;

        }

        foreach (KeyValuePair<DataComponentType, object?> patch in _patch)
        {
            // Patch-only additions (not present in the prototype) with a non-null value.
            if (patch.Value is not null && !_prototype.ContainsKey(patch.Key))
                yield return patch.Key;

        }
    }

    private object? EffectiveValue(DataComponentType type)
    {
        if (_patch.TryGetValue(type, out object? patched))
            return patched;

        return _prototype.TryGetValue(type, out object? proto) ? proto : null;
    }

    /// <inheritdoc/>
    public IEnumerator<DataComponentEntry> GetEnumerator() => Effective.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Value equality over the effective component view. Two maps are equal when they expose the same components with equal values, regardless of how the prototype/patch split produced them.</summary>
    public bool EffectiveEquals(DataComponentMap other)
    {
        ArgumentNullException.ThrowIfNull(other);
        int count = 0;
        foreach (DataComponentType type in EffectiveKeys())
        {
            object? mine = EffectiveValue(type);
            object? theirs = other.EffectiveValue(type);
            if (theirs is null || !mine!.Equals(theirs))
                return false;

            count++;
        }

        return count == other.Count;
    }

    /// <summary>Structural value equality: the prototypes must be equal AND the patches must be equal. Two maps with the same effective view but a different prototype/patch split are NOT structurally equal; use <see cref="EffectiveEquals"/> for the effective-view comparison (the ItemStack path). Patches produced by <see cref="Create"/>/<see cref="With{T}"/>/<see cref="Without{T}"/> are canonical, so equal prototype + equal effective view implies equal patch.</summary>
    public bool Equals(DataComponentMap? other)
    {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        return DictionariesEqual(_prototype, other._prototype) && PatchesEqual(_patch, other._patch);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is DataComponentMap other && Equals(other);

    /// <summary>Order-independent hash over the prototype and patch entries, with the patch hash weighted by 31.</summary>
    public override int GetHashCode()
    {
        int prototypeHash = 0;
        foreach (KeyValuePair<DataComponentType, object> pair in _prototype)
            prototypeHash += HashCode.Combine(pair.Key, pair.Value);

        int patchHash = 0;
        foreach (KeyValuePair<DataComponentType, object?> pair in _patch)
            patchHash += HashCode.Combine(pair.Key, pair.Value);

        return prototypeHash + patchHash * 31;
    }

    private static bool DictionariesEqual(
        IReadOnlyDictionary<DataComponentType, object> left,
        IReadOnlyDictionary<DataComponentType, object> right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (left.Count != right.Count)
            return false;

        foreach (KeyValuePair<DataComponentType, object> pair in left)
            if (!right.TryGetValue(pair.Key, out object? theirs) || !pair.Value.Equals(theirs))
                return false;

        return true;
    }

    private static bool PatchesEqual(
        IReadOnlyDictionary<DataComponentType, object?> left,
        IReadOnlyDictionary<DataComponentType, object?> right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (left.Count != right.Count)
            return false;

        foreach (KeyValuePair<DataComponentType, object?> pair in left)
        {
            if (!right.TryGetValue(pair.Key, out object? theirs))
                return false;

            // Null is the removal marker; both sides must agree on marker vs value.
            if (pair.Value is null ? theirs is not null : !pair.Value.Equals(theirs))
                return false;

        }

        return true;
    }
}
