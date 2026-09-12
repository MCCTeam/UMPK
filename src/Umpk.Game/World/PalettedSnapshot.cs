namespace Umpk.Game.World;

/// <summary>An immutable, self-contained copy of a <see cref="PalettedContainer"/>'s cells at capture time (region capture): the pathfinder copies its region of interest into snapshots and reads them with no further coordination, since a snapshot owns its arrays and never mutates. Reads are plain array indexing.</summary>
public sealed class PalettedSnapshot
{
    private readonly int _cellCount;
    private readonly int _bitsPerEntry;
    private readonly int _entriesPerLong;
    private readonly long _mask;
    private readonly int[]? _palette;
    private readonly long[]? _data;
    private readonly int _singleValue;

    internal PalettedSnapshot(int cellCount, int bitsPerEntry, int[]? palette, long[] data)
    {
        _cellCount = cellCount;
        _bitsPerEntry = bitsPerEntry;
        _entriesPerLong = 64 / bitsPerEntry;
        _mask = (1L << bitsPerEntry) - 1L;
        _palette = palette;
        _data = data;
        _singleValue = 0;
    }

    private PalettedSnapshot(int cellCount, int singleValue)
    {
        _cellCount = cellCount;
        _bitsPerEntry = 0;
        _singleValue = singleValue;
    }

    internal static PalettedSnapshot Single(int cellCount, int value) => new(cellCount, value);

    /// <summary>The number of cells this snapshot holds.</summary>
    public int CellCount => _cellCount;

    /// <summary>Whether any cell in this snapshot MAY hold an id the predicate accepts, decided from the encoding rather than by reading cells.</summary>
    /// <remarks>Exact for the two encodings that name their id set: a single-value store holds one id, and an indirect store's palette is a superset of the ids in its cells (an id can outlive the last cell that used it, which makes a false positive possible and a false negative impossible). A direct store has no palette to test, so it answers true. The asymmetry is the contract: a caller may use this to SKIP work, never to conclude that something is present.</remarks>
    internal bool MayContainValue(Func<int, bool> predicate)
    {
        if (_data is null)
            return predicate(_singleValue);

        if (_palette is null)
            return true;

        foreach (int id in _palette)
            if (predicate(id))
                return true;

        return false;
    }

    /// <summary>Reads the raw id at a linear cell index.</summary>
    public int Get(int index)
    {
        if (_data is null)
            return _singleValue;

        int longIndex = index / _entriesPerLong;
        int bitOffset = (index - longIndex * _entriesPerLong) * _bitsPerEntry;
        int raw = (int)((_data[longIndex] >> bitOffset) & _mask);
        return _palette is null ? raw : _palette[raw];
    }
}
