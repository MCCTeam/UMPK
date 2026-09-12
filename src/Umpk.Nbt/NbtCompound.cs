using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Umpk.Nbt;

/// <summary>An order-preserving map of named tags (id 10). This compound preserves the exact order in which members were read or inserted. That property is normative: a decode followed by a re-encode reproduces the server's bytes only if member order survives the round trip, which a hash-ordered compound cannot guarantee. Re-assigning an existing key keeps that key at its original position and only replaces the value.</summary>
public sealed class NbtCompound : NbtTag, IEnumerable<KeyValuePair<string, NbtTag>>
{
    private readonly List<string> _order;
    private readonly Dictionary<string, NbtTag> _map;

    /// <summary>Creates an empty compound.</summary>
    public NbtCompound()
    {
        _order = [];
        _map = new Dictionary<string, NbtTag>(StringComparer.Ordinal);
    }

    /// <summary>The number of members.</summary>
    public int Count => _order.Count;

    /// <summary>True when the compound has no members.</summary>
    public bool IsEmpty => _order.Count == 0;

    /// <summary>The member keys, in insertion order.</summary>
    public IReadOnlyList<string> Keys => _order;

    /// <inheritdoc/>
    public override NbtTagType Type => NbtTagType.Compound;

    /// <summary>Gets or sets a member by key. Setting an existing key preserves its position.</summary>
    /// <exception cref="KeyNotFoundException">The key is absent on read.</exception>
    /// <exception cref="ArgumentNullException">The key or the assigned value is null.</exception>
    public NbtTag this[string key]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(key);
            return _map.TryGetValue(key, out NbtTag? value)
                ? value
                : throw new KeyNotFoundException($"No NBT member named '{key}'");
        }

        set => Put(key, value);
    }

    /// <summary>Inserts or replaces a member, preserving insertion order for existing keys.</summary>
    /// <exception cref="ArgumentNullException">The key or value is null.</exception>
    public void Put(string key, NbtTag value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        if (!_map.ContainsKey(key))
            _order.Add(key);

        _map[key] = value;
    }

    /// <summary>Puts a byte member.</summary>
    public void PutByte(string key, sbyte value) => Put(key, new NbtByte(value));

    /// <summary>Puts a boolean member (stored as a byte).</summary>
    public void PutBool(string key, bool value) => Put(key, new NbtByte(value));

    /// <summary>Puts a short member.</summary>
    public void PutShort(string key, short value) => Put(key, new NbtShort(value));

    /// <summary>Puts an int member.</summary>
    public void PutInt(string key, int value) => Put(key, new NbtInt(value));

    /// <summary>Puts a long member.</summary>
    public void PutLong(string key, long value) => Put(key, new NbtLong(value));

    /// <summary>Puts a float member.</summary>
    public void PutFloat(string key, float value) => Put(key, new NbtFloat(value));

    /// <summary>Puts a double member.</summary>
    public void PutDouble(string key, double value) => Put(key, new NbtDouble(value));

    /// <summary>Puts a string member.</summary>
    public void PutString(string key, string value) => Put(key, new NbtString(value));

    /// <summary>True when a member with the given key exists.</summary>
    public bool ContainsKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _map.ContainsKey(key);
    }

    /// <summary>Attempts to get a member.</summary>
    public bool TryGet(string key, [NotNullWhen(true)] out NbtTag? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _map.TryGetValue(key, out value);
    }

    /// <summary>Attempts to get a member as a specific tag type.</summary>
    public bool TryGet<T>(string key, [NotNullWhen(true)] out T? value)
        where T : NbtTag
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_map.TryGetValue(key, out NbtTag? tag) && tag is T typed)
        {
            value = typed;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>Gets the tag-type id of a member, or <see cref="NbtTagType.End"/> if absent.</summary>
    public NbtTagType GetTagType(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _map.TryGetValue(key, out NbtTag? tag) ? tag.Type : NbtTagType.End;
    }

    /// <summary>Removes a member. Returns true when a member was removed.</summary>
    public bool Remove(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_map.Remove(key))
        {
            _order.Remove(key);
            return true;
        }

        return false;
    }

    /// <summary>Removes every member.</summary>
    public void Clear()
    {
        _order.Clear();
        _map.Clear();
    }

    /// <summary>Reads a numeric member as <see cref="int"/>, or 0 if absent or non-numeric.</summary>
    public int GetInt(string key) => TryGet(key, out NbtNumeric? n) ? n.AsInt : 0;

    /// <summary>Reads a numeric member as <see cref="long"/>, or 0 if absent or non-numeric.</summary>
    public long GetLong(string key) => TryGet(key, out NbtNumeric? n) ? n.AsLong : 0L;

    /// <summary>Reads a numeric member as <see cref="short"/>, or 0 if absent or non-numeric.</summary>
    public short GetShort(string key) => TryGet(key, out NbtNumeric? n) ? n.AsShort : (short)0;

    /// <summary>Reads a numeric member as <see cref="sbyte"/>, or 0 if absent or non-numeric.</summary>
    public sbyte GetByte(string key) => TryGet(key, out NbtNumeric? n) ? n.AsSByte : (sbyte)0;

    /// <summary>Reads a numeric member as <see cref="float"/>, or 0 if absent or non-numeric.</summary>
    public float GetFloat(string key) => TryGet(key, out NbtNumeric? n) ? n.AsFloat : 0f;

    /// <summary>Reads a numeric member as <see cref="double"/>, or 0 if absent or non-numeric.</summary>
    public double GetDouble(string key) => TryGet(key, out NbtNumeric? n) ? n.AsDouble : 0d;

    /// <summary>Reads a byte member as a boolean (non-zero is true), or false if absent.</summary>
    public bool GetBool(string key) => TryGet(key, out NbtNumeric? n) && n.AsSByte != 0;

    /// <summary>Reads a string member, or the empty string if absent or not a string.</summary>
    public string GetString(string key) => TryGet(key, out NbtString? s) ? s.Value : string.Empty;

    /// <summary>Reads a compound member, or null if absent or not a compound.</summary>
    public NbtCompound? GetCompound(string key) => TryGet(key, out NbtCompound? c) ? c : null;

    /// <summary>Reads a list member, or null if absent or not a list.</summary>
    public NbtList? GetList(string key) => TryGet(key, out NbtList? l) ? l : null;

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, NbtTag>> GetEnumerator()
    {
        foreach (string key in _order)
            yield return new KeyValuePair<string, NbtTag>(key, _map[key]);

    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    public override NbtTag Copy()
    {
        var copy = new NbtCompound();
        foreach (string key in _order)
            copy.Put(key, _map[key].Copy());

        return copy;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (obj is not NbtCompound other || other._order.Count != _order.Count)
            return false;

        // Value equality ignores order (matching vanilla map equality); order only matters for re-encode.
        foreach (KeyValuePair<string, NbtTag> pair in _map)
            if (!other._map.TryGetValue(pair.Key, out NbtTag? value) || !pair.Value.Equals(value))
                return false;

        return true;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        // Order-independent so it agrees with order-independent Equals.
        int acc = 0;
        foreach (KeyValuePair<string, NbtTag> pair in _map)
            acc += HashCode.Combine(StringComparer.Ordinal.GetHashCode(pair.Key), pair.Value.GetHashCode());

        return acc;
    }
}
