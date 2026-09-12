using System.Collections;
using Umpk.Geometry;

namespace Umpk.Physics;

/// <summary>A reusable growable list of collision AABBs, owned by <see cref="PlayerPhysics"/> and reused across ticks so collision gathering allocates nothing on the steady-state path (the zero-allocation tick requirement). Grows by doubling; after warmup the backing array is stable. Implements <see cref="ICollection{T}"/> so it can be passed to <see cref="IPhysicsWorldView.CollectEntityColliders"/> without a per-call adapter allocation.</summary>
internal sealed class ColliderBuffer : ICollection<Aabb>
{
    private Aabb[] _items;
    private int _count;

    public ColliderBuffer(int capacity = 64)
    {
        _items = new Aabb[capacity];
    }

    public int Count => _count;

    public bool IsReadOnly => false;

    public Aabb this[int index] => _items[index];

    public ReadOnlySpan<Aabb> Span => _items.AsSpan(0, _count);

    public void Clear() => _count = 0;

    public void Add(Aabb box)
    {
        if (_count == _items.Length)
            Array.Resize(ref _items, _items.Length * 2);

        _items[_count++] = box;
    }

    public bool Contains(Aabb item) => Array.IndexOf(_items, item, 0, _count) >= 0;

    public void CopyTo(Aabb[] array, int arrayIndex) => Array.Copy(_items, 0, array, arrayIndex, _count);

    public bool Remove(Aabb item) => throw new NotSupportedException();

    public IEnumerator<Aabb> GetEnumerator()
    {
        for (int i = 0; i < _count; i++)
            yield return _items[i];

    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
