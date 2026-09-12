using System.Runtime.CompilerServices;
using System.Threading;

namespace Umpk.Game.World;

/// <summary>
/// A packed array of fixed-width entries over a <c>long[]</c>, using the 1.16+ padded wire layout. Each long packs <c>64 / bitsPerEntry</c> entries and never straddles a long boundary. This is the storage primitive under an indirect or direct <see cref="PalettedContainer"/>.
///
/// <para>The mutation contract is realized here: <see cref="Get"/> reads a single backing long with <c>Volatile.Read</c> and <see cref="Set"/> publishes a single backing long with <c>Interlocked.Exchange</c>. Because every entry lives entirely inside one long, an off-loop reader never observes a torn value: it sees either the long before a write or the long after it. Writes happen only on the session loop, so the read-modify-write in <see cref="Set"/> needs no compare-exchange loop.</para>
/// </summary>
internal sealed class BitStorage
{
    private readonly long[] _data;
    private readonly int _bitsPerEntry;
    private readonly int _entriesPerLong;
    private readonly long _mask;
    private readonly int _size;

    /// <summary>Creates zero-initialized storage for <paramref name="size"/> entries of the given width.</summary>
    internal BitStorage(int bitsPerEntry, int size)
    {
        _bitsPerEntry = bitsPerEntry;
        _size = size;
        _entriesPerLong = 64 / bitsPerEntry;
        _mask = (1L << bitsPerEntry) - 1L;
        int longCount = (size + _entriesPerLong - 1) / _entriesPerLong;
        _data = new long[longCount];
    }

    /// <summary>Wraps an already-decoded backing array (used when installing a decoded section).</summary>
    internal BitStorage(int bitsPerEntry, int size, long[] data)
    {
        _bitsPerEntry = bitsPerEntry;
        _size = size;
        _entriesPerLong = 64 / bitsPerEntry;
        _mask = (1L << bitsPerEntry) - 1L;
        int longCount = (size + _entriesPerLong - 1) / _entriesPerLong;
        if (data.Length != longCount)
            throw new ArgumentException($"Backing array has {data.Length} longs; expected {longCount} for {size} entries at {bitsPerEntry} bits.", nameof(data));

        _data = data;
    }

    internal int BitsPerEntry => _bitsPerEntry;

    internal int Size => _size;

    internal int LongCount => _data.Length;

    /// <summary>Reads the entry at <paramref name="index"/> with per-read atomicity.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int Get(int index)
    {
        int longIndex = index / _entriesPerLong;
        int bitOffset = (index - longIndex * _entriesPerLong) * _bitsPerEntry;
        long value = Volatile.Read(ref _data[longIndex]);
        return (int)((value >> bitOffset) & _mask);
    }

    /// <summary>Writes <paramref name="value"/> at <paramref name="index"/>, publishing the modified long atomically. Caller runs on the session loop; no other writer exists.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> does not fit the entry width. Silent truncation via the mask would corrupt cells invisibly; the owning container re-widens before writing, so a throw here means a width bookkeeping bug.</exception>
    internal void Set(int index, int value)
    {
        if ((value & _mask) != value)
            throw new ArgumentOutOfRangeException(nameof(value), value, $"Value does not fit in {_bitsPerEntry} bits.");

        int longIndex = index / _entriesPerLong;
        int bitOffset = (index - longIndex * _entriesPerLong) * _bitsPerEntry;
        long current = Volatile.Read(ref _data[longIndex]);
        long updated = (current & ~(_mask << bitOffset)) | ((value & _mask) << bitOffset);
        Interlocked.Exchange(ref _data[longIndex], updated);
    }

    /// <summary>Copies the backing longs into a new array (region capture).</summary>
    internal long[] CopyData()
    {
        var copy = new long[_data.Length];
        for (int i = 0; i < _data.Length; i++)
            copy[i] = Volatile.Read(ref _data[i]);

        return copy;
    }
}
