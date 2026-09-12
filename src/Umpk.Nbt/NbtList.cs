using System.Collections;

namespace Umpk.Nbt;

/// <summary>A homogeneous, ordered list of tags (id 9). Every element must share one <see cref="ElementType"/>; the list records that type on the first insert and rejects mismatched additions. An empty list has element type <see cref="NbtTagType.End"/> on the wire.</summary>
public sealed class NbtList : NbtTag, IList<NbtTag>, IReadOnlyList<NbtTag>
{
    private readonly List<NbtTag> _items;
    private NbtTagType _elementType;

    /// <summary>Creates an empty list.</summary>
    public NbtList()
    {
        _items = [];
        _elementType = NbtTagType.End;
    }

    /// <summary>Creates a list pre-declared to hold a specific element type.</summary>
    public NbtList(NbtTagType elementType)
    {
        _items = [];
        _elementType = elementType;
    }

    internal NbtList(List<NbtTag> items, NbtTagType elementType)
    {
        _items = items;
        _elementType = elementType;
    }

    /// <summary>
    /// The element type this list serializes as: <see cref="NbtTagType.End"/> when empty, the single type when every element shares one, and <see cref="NbtTagType.Compound"/> when the contents are MIXED.
    /// <para>The last case is required by the wire format. A list of components is routinely mixed, because a plain unstyled component collapses to a bare string and a styled one does not because a plain unstyled component collapses to a bare string and a styled one does not. NBT has one element-type byte, so a mixed list is written as Compound and each non-compound element is wrapped as <c>{"": value}</c>, then unwrapped on read. The wire convention is the same across the supported component eras, so nothing here is era-gated.</para>
    /// </summary>
    public NbtTagType ElementType
    {
        get
        {
            NbtTagType found = NbtTagType.End;
            foreach (NbtTag item in _items)
            {
                if (found == NbtTagType.End)
                    found = item.Type;

                else if (found != item.Type)
                {
                    // Mixed lists use the compound tag id.
                    return NbtTagType.Compound;
                }
            }

            return found == NbtTagType.End ? _elementType : found;
        }
    }

    /// <inheritdoc/>
    public override NbtTagType Type => NbtTagType.List;

    /// <inheritdoc/>
    public int Count => _items.Count;

    /// <inheritdoc/>
    public bool IsReadOnly => false;

    /// <inheritdoc/>
    public NbtTag this[int index]
    {
        get => _items[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _items[index] = value;
        }
    }

    /// <summary>Appends a tag. Mixed element types are ALLOWED and serialize through vanilla's wrapping convention; see <see cref="ElementType"/>. Refusing them here is what made a chat component with both plain and styled children unencodable.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public void Add(NbtTag item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_items.Count == 0)
            _elementType = item.Type;

        _items.Add(item);
    }

    /// <inheritdoc cref="Add"/>
    public void Insert(int index, NbtTag item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_items.Count == 0)
            _elementType = item.Type;

        _items.Insert(index, item);
    }

    /// <inheritdoc/>
    public bool Remove(NbtTag item) => _items.Remove(item);

    /// <inheritdoc/>
    public void RemoveAt(int index) => _items.RemoveAt(index);

    /// <inheritdoc/>
    public void Clear()
    {
        _items.Clear();
        _elementType = NbtTagType.End;
    }

    /// <inheritdoc/>
    public bool Contains(NbtTag item) => _items.Contains(item);

    /// <inheritdoc/>
    public int IndexOf(NbtTag item) => _items.IndexOf(item);

    /// <inheritdoc/>
    public void CopyTo(NbtTag[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <inheritdoc/>
    public IEnumerator<NbtTag> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc/>
    public override NbtTag Copy()
    {
        var copy = new List<NbtTag>(_items.Count);
        foreach (NbtTag tag in _items)
            copy.Add(tag.Copy());

        return new NbtList(copy, _elementType);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (obj is not NbtList other || other._items.Count != _items.Count || other._elementType != _elementType)
            return false;

        for (int i = 0; i < _items.Count; i++)
            if (!_items[i].Equals(other._items[i]))
                return false;

        return true;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(_elementType);
        foreach (NbtTag tag in _items)
            hash.Add(tag);

        return hash.ToHashCode();
    }

}
