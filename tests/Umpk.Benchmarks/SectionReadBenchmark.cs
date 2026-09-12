using BenchmarkDotNet.Attributes;
using Umpk.Game.World;

namespace Umpk.Benchmarks;

/// <summary>Establishes the baseline that a paletted section read costs array-indexing time. Random reads over a section in each of the three encodings are compared against a plain <c>int[4096]</c> baseline. A near-flat ratio confirms the paletted read stays negligible.</summary>
[MemoryDiagnoser]
public class SectionReadBenchmark
{
    private const int Reads = 4096;

    private readonly int[] _indices = new int[Reads];
    private readonly int[] _flat = new int[ChunkSection.BlockCells];
    private ChunkSection _single = null!;
    private ChunkSection _indirect = null!;
    private ChunkSection _direct = null!;

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(12345);
        for (int i = 0; i < Reads; i++)
            _indices[i] = rng.Next(ChunkSection.BlockCells);

        // Single-value: every cell the same id.
        _single = ChunkSection.Filled(1);

        // Indirect: a modest palette (16 distinct ids) laid over the section.
        _indirect = ChunkSection.Filled(0);
        for (int i = 0; i < ChunkSection.BlockCells; i++)
        {
            int x = i & 15, z = (i >> 4) & 15, y = i >> 8;
            int id = 1 + (i % 16);
            _indirect.SetBlockStateId(x, y, z, id);
            _flat[i] = id;
        }

        // Direct: 4096 distinct ids force promotion to the direct encoding.
        _direct = ChunkSection.Filled(0);
        for (int i = 0; i < ChunkSection.BlockCells; i++)
        {
            int x = i & 15, z = (i >> 4) & 15, y = i >> 8;
            _direct.SetBlockStateId(x, y, z, i + 1);
        }
    }

    [Benchmark(Baseline = true)]
    public long FlatArray()
    {
        long acc = 0;
        for (int i = 0; i < Reads; i++)
            acc += _flat[_indices[i]];

        return acc;
    }

    [Benchmark]
    public long SingleValue()
    {
        long acc = 0;
        for (int i = 0; i < Reads; i++)
        {
            int idx = _indices[i];
            acc += _single.GetBlockStateId(idx & 15, idx >> 8, (idx >> 4) & 15);
        }

        return acc;
    }

    [Benchmark]
    public long Indirect()
    {
        long acc = 0;
        for (int i = 0; i < Reads; i++)
        {
            int idx = _indices[i];
            acc += _indirect.GetBlockStateId(idx & 15, idx >> 8, (idx >> 4) & 15);
        }

        return acc;
    }

    [Benchmark]
    public long Direct()
    {
        long acc = 0;
        for (int i = 0; i < Reads; i++)
        {
            int idx = _indices[i];
            acc += _direct.GetBlockStateId(idx & 15, idx >> 8, (idx >> 4) & 15);
        }

        return acc;
    }
}
