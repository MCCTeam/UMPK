using System.Threading;

namespace Umpk.Game.World;

/// <summary>
/// A paletted store of raw ids over a fixed number of cells, in the three wire encodings: single-value (one id for every cell, no backing array), indirect (a palette of ids plus packed palette-indices in a <see cref="BitStorage"/>), and direct (raw ids packed at a fixed global width, no palette). Block sections use 4096 cells; biome containers use 64 cells at 4x4x4 resolution.
///
/// <para>Encoding transitions happen only on <see cref="Set"/> when a new id will not fit the current palette; the store rebuilds into a wider encoding and swaps the backing state by reference. Single-value promotes to indirect on the first differing id; indirect widens or promotes to direct when the palette exceeds the direct threshold. Reads follow the mutation contract through <see cref="BitStorage"/> and a volatile <see cref="_state"/> reference, so an off-loop reader that captures <see cref="_state"/> once sees a self-consistent encoding.</para>
///
/// <para>Indirect widths honor the per-kind strategy: block containers floor at 4 bits while biome containers use 1-3 bit linear palettes. The direct width is <c>ceillog2(global id count)</c> like vanilla's global palette when the count is known; otherwise it is derived from the widest id actually stored and re-widened on demand when a wider id arrives (rebuild-and-swap, never silent truncation).</para>
/// </summary>
internal sealed class PalettedContainer
{
    private readonly int _cellCount;
    private readonly int _configuredDirectBits;
    private readonly int _minIndirectBits;
    private readonly int _maxIndirectBits;

    // The whole mutable encoding is behind one reference so a reader gets a consistent (palette, storage) pair with a single volatile read even while the session loop swaps in a wider encoding.
    private State _state;

    private PalettedContainer(int cellCount, int directBits, int minIndirectBits, int maxIndirectBits, State state)
    {
        _cellCount = cellCount;
        _configuredDirectBits = directBits;
        _minIndirectBits = minIndirectBits;
        _maxIndirectBits = maxIndirectBits;
        _state = state;
    }

    internal int CellCount => _cellCount;

    /// <summary>The number of distinct ids currently in the palette (1 for single-value, the id count for direct).</summary>
    internal int PaletteCount => Volatile.Read(ref _state).PaletteCount;

    /// <summary>The current bits-per-entry (0 for single-value).</summary>
    internal int BitsPerEntry => Volatile.Read(ref _state).Storage?.BitsPerEntry ?? 0;

    /// <summary>True when the container stores raw ids at the direct width (no palette).</summary>
    internal bool IsDirect
    {
        get
        {
            State state = Volatile.Read(ref _state);
            return state.Storage is not null && state.Palette is null;
        }
    }

    /// <summary>Creates a single-value container filled with one id. <paramref name="directBits"/> is the width used when the container is later promoted to direct storage; pass 0 to derive it from the widest id present at promotion time (re-widened on demand).</summary>
    internal static PalettedContainer SingleValue(int cellCount, int directBits, int minIndirectBits, int maxIndirectBits, int value) =>
        new(cellCount, directBits, minIndirectBits, maxIndirectBits, State.Single(value));

    /// <summary>Creates an indirect container from a decoded palette and packed palette-index storage (the shape a chunk decoder produces). The storage width must match <paramref name="bitsPerEntry"/>.</summary>
    internal static PalettedContainer Indirect(int cellCount, int directBits, int minIndirectBits, int maxIndirectBits, int[] palette, int bitsPerEntry, long[] packedData)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(packedData);
        var storage = new BitStorage(bitsPerEntry, cellCount, packedData);
        return new PalettedContainer(cellCount, directBits, minIndirectBits, maxIndirectBits, State.Indirect((int[])palette.Clone(), storage));
    }

    /// <summary>Creates a direct container from packed raw ids at the direct width (the shape a chunk decoder produces for wide sections).</summary>
    internal static PalettedContainer Direct(int cellCount, int directBits, int minIndirectBits, int maxIndirectBits, long[] packedData)
    {
        ArgumentNullException.ThrowIfNull(packedData);
        var storage = new BitStorage(directBits, cellCount, packedData);
        return new PalettedContainer(cellCount, directBits, minIndirectBits, maxIndirectBits, State.Direct(storage));
    }

    /// <summary>Reads the raw id at a linear cell index with per-read atomicity.</summary>
    internal int Get(int index)
    {
        State state = Volatile.Read(ref _state);
        if (state.Storage is null)
            return state.SingleValue;

        int raw = state.Storage.Get(index);
        int[]? palette = state.Palette;
        return palette is null ? raw : palette[raw];
    }

    /// <summary>Writes the raw id at a linear cell index. Grows or promotes the encoding when needed, swapping the backing state by reference. Session-loop only.</summary>
    internal void Set(int index, int value)
    {
        State state = _state;

        if (state.Storage is null)
        {
            if (value == state.SingleValue)
                return;

            // Promote single-value -> indirect with the two ids, at the minimum indirect width.
            state = PromoteFromSingle(state.SingleValue, value);
            Volatile.Write(ref _state, state);
        }

        if (state.Palette is null)
        {
            // Direct: store the raw id, re-widening the storage first when the id does not fit the current width (rebuild-and-swap; a masked write would silently corrupt the cell).
            if (!FitsWidth(value, state.Storage!.BitsPerEntry))
            {
                state = WidenDirect(state.Storage!, value);
                Volatile.Write(ref _state, state);
            }

            state.Storage!.Set(index, value);
            return;
        }

        int paletteIndex = IndexOfInPalette(state.Palette, value);
        if (paletteIndex < 0)
        {
            state = GrowPalette(state, value, out paletteIndex);
            Volatile.Write(ref _state, state);

            if (state.Palette is null)
            {
                // Grew past the indirect ceiling: now direct, the "index" is the raw id itself.
                state.Storage!.Set(index, value);
                return;
            }
        }

        state.Storage!.Set(index, paletteIndex);
    }

    /// <summary>Whether any cell MAY hold an id the predicate accepts, decided from the encoding rather than by reading cells. See <see cref="PalettedSnapshot.MayContainValue"/> for the contract: exact for single-value, a superset for indirect, and unconditionally true for direct, so a caller may use it to skip work and never to conclude presence.</summary>
    internal bool MayContainValue(Func<int, bool> predicate)
    {
        State state = Volatile.Read(ref _state);
        if (state.Storage is null)
            return predicate(state.SingleValue);

        if (state.Palette is null)
            return true;

        foreach (int id in state.Palette)
            if (predicate(id))
                return true;

        return false;
    }

    /// <summary>Copies the current encoding into an immutable snapshot the pathfinder can read off-loop.</summary>
    internal PalettedSnapshot Capture()
    {
        State state = Volatile.Read(ref _state);
        if (state.Storage is null)
            return PalettedSnapshot.Single(_cellCount, state.SingleValue);

        long[] data = state.Storage.CopyData();
        int[]? palette = state.Palette is null ? null : (int[])state.Palette.Clone();
        return new PalettedSnapshot(_cellCount, state.Storage.BitsPerEntry, palette, data);
    }

    private State PromoteFromSingle(int existing, int newValue)
    {
        int bits = MinIndirectBits(2);
        var storage = new BitStorage(bits, _cellCount);
        // Every cell currently holds `existing` at palette index 0; only the new write differs, so palette index 0 stays valid for all cells. The caller writes newValue at its cell after.
        return State.Indirect([existing, newValue], storage);
    }

    private State GrowPalette(State state, int newValue, out int newIndex)
    {
        int[] oldPalette = state.Palette!;
        int newCount = oldPalette.Length + 1;
        int requiredBits = MinIndirectBits(newCount);

        if (requiredBits > _maxIndirectBits)
        {
            // Promote to direct: rewrite every cell as its resolved raw id at the direct width.
            int directBits = DirectBitsFor(oldPalette, newValue);
            var directStorage = new BitStorage(directBits, _cellCount);
            BitStorage old = state.Storage!;
            for (int i = 0; i < _cellCount; i++)
                directStorage.Set(i, oldPalette[old.Get(i)]);

            newIndex = newValue; // in direct mode the "index" is the raw id itself
            return State.Direct(directStorage);
        }

        var newPalette = new int[newCount];
        Array.Copy(oldPalette, newPalette, oldPalette.Length);
        newPalette[oldPalette.Length] = newValue;
        newIndex = oldPalette.Length;

        BitStorage current = state.Storage!;

        // Palette growth rebuilds into a NEW backing array and swaps by reference. The old state (with its shorter palette) keeps referencing the old storage, so an off-loop reader holding that pair never sees a palette index the old palette cannot resolve. Even when the packed indices still fit the current width, we copy rather than share the storage instance.
        int targetBits = Math.Max(requiredBits, current.BitsPerEntry);
        var rebuilt = new BitStorage(targetBits, _cellCount);
        for (int i = 0; i < _cellCount; i++)
            rebuilt.Set(i, current.Get(i));

        return State.Indirect(newPalette, rebuilt);
    }

    // Direct re-widen: rebuild the raw-id storage at a width that fits `newValue` and swap by reference.
    private State WidenDirect(BitStorage current, int newValue)
    {
        int bits = Math.Max(current.BitsPerEntry, BitsFor(newValue));
        var widened = new BitStorage(bits, _cellCount);
        for (int i = 0; i < _cellCount; i++)
            widened.Set(i, current.Get(i));

        return State.Direct(widened);
    }

    // The direct width at promotion time: the configured ceillog2(global id count) when known, otherwise derived from the widest id actually present (re-widened later on demand).
    private int DirectBitsFor(int[] palette, int newValue)
    {
        if (_configuredDirectBits > 0)
            return _configuredDirectBits;

        int bits = BitsFor(newValue);
        foreach (int id in palette)
            bits = Math.Max(bits, BitsFor(id));

        return bits;
    }

    // The vanilla per-kind minimum indirect width: blocks floor at 4 bits, biomes at 1 bit (block-state palette selection vs createForBiomes).
    private int MinIndirectBits(int paletteCount)
    {
        int bits = _minIndirectBits;
        while ((1 << bits) < paletteCount)
            bits++;

        return bits;
    }

    // ceillog2(value + 1): the narrowest width that can represent `value` (min 1 bit). Long shifts keep the loop overflow-safe for ids near int.MaxValue (capped at 31 bits).
    private static int BitsFor(int value)
    {
        int bits = 1;
        while (bits < 31 && value >= 1L << bits)
            bits++;

        return bits;
    }

    // Whether a non-negative id fits an entry width (widths >= 32 always fit an int id).
    private static bool FitsWidth(int value, int bits) => bits >= 32 || (uint)value < 1u << bits;

    private static int IndexOfInPalette(int[] palette, int value)
    {
        for (int i = 0; i < palette.Length; i++)
            if (palette[i] == value)
                return i;

        return -1;
    }

    // The atomic unit swapped on encoding transitions. Exactly one of the three shapes is live:
    //   single-value: Storage == null, Palette == null, SingleValue holds the id.
    //   indirect:     Storage != null, Palette != null (palette-index -> raw id).
    //   direct:       Storage != null, Palette == null (storage holds raw ids).
    private sealed class State
    {
        private State(int singleValue, int[]? palette, BitStorage? storage)
        {
            SingleValue = singleValue;
            Palette = palette;
            Storage = storage;
        }

        internal int SingleValue { get; }

        internal int[]? Palette { get; }

        internal BitStorage? Storage { get; }

        internal int PaletteCount => Storage is null ? 1 : Palette?.Length ?? (1 << Math.Min(Storage.BitsPerEntry, 30));

        internal static State Single(int value) => new(value, null, null);

        internal static State Indirect(int[] palette, BitStorage storage) => new(0, palette, storage);

        internal static State Direct(BitStorage storage) => new(0, null, storage);
    }
}
