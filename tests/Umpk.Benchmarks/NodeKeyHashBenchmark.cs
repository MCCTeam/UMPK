using BenchmarkDotNet.Attributes;
using Umpk.Pathfinding.Core;

namespace Umpk.Benchmarks;

/// <summary>What the A* node map's key costs to hash and compare, and what writing the hash out by hand buys.</summary>
/// <remarks>
/// <para>The search probes <c>AStarPathFinder.NodeKey</c> roughly ten times per node explored, so on the tail of the recorded distribution - 92,919 nodes on the slowest live plan - this is close to a million dictionary operations. The key's second field is a nine-field record struct, so a compiler-generated hash chains <c>EqualityComparer&lt;T&gt;.Default</c> over eleven fields.</para>
/// <para><c>NodeKey</c> is internal to <c>Umpk.Pathfinding</c>, so what is measured here is a MODEL of it: two structs over the same three fields, one left to the compiler and one written out, exercised through the same <c>Dictionary&lt;TKey, TValue&gt;</c> pattern the search uses. The effect on a real search is measured separately, by <see cref="RegionCaptureBenchmark"/>'s <c>sealed-100</c> shape, which explores tens of thousands of nodes before it fails.</para>
/// </remarks>
[MemoryDiagnoser]
public class NodeKeyHashBenchmark
{
    private const int Operations = 200_000;

    private Dictionary<GeneratedKey, int> _generated = null!;
    private Dictionary<WrittenOutKey, int> _writtenOut = null!;
    private Dictionary<CheapMixKey, int> _cheapMix = null!;

    /// <summary>Whether a run-up is in progress, which is the case the written-out hash short-circuits.</summary>
    [Params(false, true)]
    public bool Preparing { get; set; }

    /// <summary>Sizes both dictionaries so the run measures hashing rather than resizing.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _generated = new Dictionary<GeneratedKey, int>(Operations);
        _writtenOut = new Dictionary<WrittenOutKey, int>(Operations);
        _cheapMix = new Dictionary<CheapMixKey, int>(Operations);
    }

    private EntryPreparationState Preparation => Preparing
        ? new EntryPreparationState(EntryPreparationKind.SidewallRunup, 10, 64, -3, 1, 0, 2, 1, 0)
        : EntryPreparationState.None;

    /// <summary>Insert and probe with the compiler-generated record-struct key.</summary>
    [Benchmark(Baseline = true)]
    public int GeneratedRecordStruct()
    {
        _generated.Clear();
        EntryPreparationState preparation = Preparation;
        int found = 0;
        for (int i = 0; i < Operations; i++)
        {
            var key = new GeneratedKey(i, preparation, i & 7);
            _generated[key] = i;
            if (_generated.TryGetValue(key, out int _))
                found++;

        }

        return found;
    }

    /// <summary>Insert and probe with the same three fields and an explicit Equals/GetHashCode.</summary>
    [Benchmark]
    public int WrittenOutHash()
    {
        _writtenOut.Clear();
        EntryPreparationState preparation = Preparation;
        int found = 0;
        for (int i = 0; i < Operations; i++)
        {
            var key = new WrittenOutKey(i, preparation, i & 7);
            _writtenOut[key] = i;
            if (_writtenOut.TryGetValue(key, out int _))
                found++;

        }

        return found;
    }

    /// <summary>The same three fields again, hashed with a multiply-add chain instead of <c>HashCode</c>.</summary>
    [Benchmark]
    public int CheapMixHash()
    {
        _cheapMix.Clear();
        EntryPreparationState preparation = Preparation;
        int found = 0;
        for (int i = 0; i < Operations; i++)
        {
            var key = new CheapMixKey(i, preparation, i & 7);
            _cheapMix[key] = i;
            if (_cheapMix.TryGetValue(key, out int _))
                found++;

        }

        return found;
    }

    private readonly record struct GeneratedKey(long PackedPosition, EntryPreparationState EntryPreparation, int AirBand);

    private readonly struct CheapMixKey(long packedPosition, EntryPreparationState entryPreparation, int airBand)
        : IEquatable<CheapMixKey>
    {
        private const int Prime = -1521134295;

        private readonly long _packedPosition = packedPosition;
        private readonly EntryPreparationState _entryPreparation = entryPreparation;
        private readonly int _airBand = airBand;

        public bool Equals(CheapMixKey other)
        {
            if (_packedPosition != other._packedPosition || _airBand != other._airBand)
                return false;

            EntryPreparationState a = _entryPreparation;
            EntryPreparationState b = other._entryPreparation;
            return a.Kind == b.Kind
                && a.OriginX == b.OriginX
                && a.OriginY == b.OriginY
                && a.OriginZ == b.OriginZ
                && a.ForwardX == b.ForwardX
                && a.ForwardZ == b.ForwardZ
                && a.RequiredSteps == b.RequiredSteps
                && a.BackwardSteps == b.BackwardSteps
                && a.ReturnSteps == b.ReturnSteps;
        }

        public override bool Equals(object? obj) => obj is CheapMixKey other && Equals(other);

        public override int GetHashCode()
        {
            long packed = _packedPosition;
            int hash = ((int)packed ^ (int)(packed >> 32)) * Prime;
            hash = (hash + _airBand) * Prime;

            EntryPreparationState preparation = _entryPreparation;
            if (preparation.Kind == EntryPreparationKind.None)
                return hash;

            hash = (hash + (int)preparation.Kind) * Prime;
            hash = (hash + preparation.OriginX) * Prime;
            hash = (hash + preparation.OriginY) * Prime;
            hash = (hash + preparation.OriginZ) * Prime;
            hash = (hash + preparation.ForwardX) * Prime;
            hash = (hash + preparation.ForwardZ) * Prime;
            return (hash + ((preparation.RequiredSteps << 16) | (preparation.BackwardSteps << 8) | preparation.ReturnSteps)) * Prime;
        }
    }

    private readonly struct WrittenOutKey(long packedPosition, EntryPreparationState entryPreparation, int airBand)
        : IEquatable<WrittenOutKey>
    {
        private readonly long _packedPosition = packedPosition;
        private readonly EntryPreparationState _entryPreparation = entryPreparation;
        private readonly int _airBand = airBand;

        public bool Equals(WrittenOutKey other)
        {
            if (_packedPosition != other._packedPosition || _airBand != other._airBand)
                return false;

            EntryPreparationState a = _entryPreparation;
            EntryPreparationState b = other._entryPreparation;
            return a.Kind == b.Kind
                && a.OriginX == b.OriginX
                && a.OriginY == b.OriginY
                && a.OriginZ == b.OriginZ
                && a.ForwardX == b.ForwardX
                && a.ForwardZ == b.ForwardZ
                && a.RequiredSteps == b.RequiredSteps
                && a.BackwardSteps == b.BackwardSteps
                && a.ReturnSteps == b.ReturnSteps;
        }

        public override bool Equals(object? obj) => obj is WrittenOutKey other && Equals(other);

        public override int GetHashCode()
        {
            EntryPreparationState preparation = _entryPreparation;
            if (preparation.Kind == EntryPreparationKind.None)
                return HashCode.Combine(_packedPosition, _airBand);

            var hash = default(HashCode);
            hash.Add(_packedPosition);
            hash.Add(_airBand);
            hash.Add((int)preparation.Kind);
            hash.Add(preparation.OriginX);
            hash.Add(preparation.OriginY);
            hash.Add(preparation.OriginZ);
            hash.Add(preparation.ForwardX);
            hash.Add(preparation.ForwardZ);
            hash.Add((preparation.RequiredSteps << 16) | (preparation.BackwardSteps << 8) | preparation.ReturnSteps);
            return hash.ToHashCode();
        }
    }
}
