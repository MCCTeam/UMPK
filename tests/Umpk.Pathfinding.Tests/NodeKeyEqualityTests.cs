using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Moves;
using Xunit;

namespace Umpk.Pathfinding.Tests;

public sealed class NodeKeyEqualityTests
{
    private static readonly EntryPreparationState Sidewall = new(
        EntryPreparationKind.SidewallRunup, 10, 64, -3, 1, 0, RequiredSteps: 2, BackwardSteps: 1, ReturnSteps: 0);

    private static AStarPathFinder.NodeKey Key(long packed, EntryPreparationState preparation, int band)
        => new(packed, preparation, band, AStarPathFinder.NodeKey.CentredLateral);

    private static AStarPathFinder.NodeKey Key(
        long packed, EntryPreparationState preparation, int band, LateralQuantum lateralX, LateralQuantum lateralZ)
        => new(packed, preparation, band, AStarPathFinder.NodeKey.PackLateral(lateralX, lateralZ));

    /// <summary>Keys built from the same three values are the same key, however they were built.</summary>
    [Fact]
    public void IdenticalValues_AreEqualAndHashEqually()
    {
        AStarPathFinder.NodeKey a = Key(PathNode.Pack(3, 64, -7), EntryPreparationState.None, 0);
        AStarPathFinder.NodeKey b = Key(PathNode.Pack(3, 64, -7), EntryPreparationState.None, 0);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());

        AStarPathFinder.NodeKey c = Key(PathNode.Pack(3, 64, -7), Sidewall, 4);
        AStarPathFinder.NodeKey d = Key(PathNode.Pack(3, 64, -7), Sidewall, 4);

        Assert.Equal(c, d);
        Assert.Equal(c.GetHashCode(), d.GetHashCode());
    }

    /// <summary>Position, air band and preparation are each a dimension of the key in their own right.</summary>
    [Fact]
    public void EachFieldSeparatesKeys()
    {
        AStarPathFinder.NodeKey baseline = Key(PathNode.Pack(3, 64, -7), EntryPreparationState.None, 0);

        Assert.NotEqual(baseline, Key(PathNode.Pack(4, 64, -7), EntryPreparationState.None, 0));
        Assert.NotEqual(baseline, Key(PathNode.Pack(3, 65, -7), EntryPreparationState.None, 0));
        Assert.NotEqual(baseline, Key(PathNode.Pack(3, 64, -6), EntryPreparationState.None, 0));
        Assert.NotEqual(baseline, Key(PathNode.Pack(3, 64, -7), EntryPreparationState.None, 1));
        Assert.NotEqual(baseline, Key(PathNode.Pack(3, 64, -7), Sidewall, 0));
        Assert.NotEqual(
            baseline,
            Key(PathNode.Pack(3, 64, -7), EntryPreparationState.None, 0, LateralQuantum.NearFace, LateralQuantum.Centre));
        Assert.NotEqual(
            baseline,
            Key(PathNode.Pack(3, 64, -7), EntryPreparationState.None, 0, LateralQuantum.Centre, LateralQuantum.FarFace));
    }

    /// <summary>The lateral is a dimension of the key, and the two axes are separate dimensions of it: a body flush with its cell's low X face and one flush with its low Z face are in the same cell and are not the same search state, because a cardinal traverse out of that cell can only carry the component perpendicular to its own heading.</summary>
    [Fact]
    public void TheTwoLateralAxes_AreSeparateDimensions()
    {
        AStarPathFinder.NodeKey onX = Key(
            PathNode.Pack(3, 64, -7), EntryPreparationState.None, 0, LateralQuantum.NearFace, LateralQuantum.Centre);
        AStarPathFinder.NodeKey onZ = Key(
            PathNode.Pack(3, 64, -7), EntryPreparationState.None, 0, LateralQuantum.Centre, LateralQuantum.NearFace);

        Assert.NotEqual(onX, onZ);
        Assert.NotEqual(onX, Key(
            PathNode.Pack(3, 64, -7), EntryPreparationState.None, 0, LateralQuantum.FarFace, LateralQuantum.Centre));
    }

    /// <summary>Every one of the nine lateral pairs packs to a distinct value, so no two of them can ever be merged by the node map. A packing that collided would silently let a body leave a cell by an edge that was only legal at some other lateral.</summary>
    [Fact]
    public void EveryLateralPair_PacksDistinctly()
    {
        LateralQuantum[] all = [LateralQuantum.NearFace, LateralQuantum.Centre, LateralQuantum.FarFace];
        var seen = new HashSet<int>();

        foreach (LateralQuantum lx in all)
            foreach (LateralQuantum lz in all)
                Assert.True(
                    seen.Add(AStarPathFinder.NodeKey.PackLateral(lx, lz)),
                    $"({lx}, {lz}) collides with an earlier pair");

        Assert.Equal(9, seen.Count);
        Assert.Equal(
            AStarPathFinder.NodeKey.CentredLateral,
            AStarPathFinder.NodeKey.PackLateral(LateralQuantum.Centre, LateralQuantum.Centre));
    }

    /// <summary>Every field of the preparation state separates keys too. This is the case a short-circuit gets wrong: two run-ups that agree on their kind and origin but are one step apart on the backward leg are DIFFERENT search states, and merging them loses the longer run-up.</summary>
    [Theory]
    [InlineData(0)]  // Kind
    [InlineData(1)]  // OriginX
    [InlineData(2)]  // OriginY
    [InlineData(3)]  // OriginZ
    [InlineData(4)]  // ForwardX
    [InlineData(5)]  // ForwardZ
    [InlineData(6)]  // RequiredSteps
    [InlineData(7)]  // BackwardSteps
    [InlineData(8)]  // ReturnSteps
    public void EveryPreparationField_SeparatesKeys(int field)
    {
        EntryPreparationState changed = field switch
        {
            0 => Sidewall with { Kind = EntryPreparationKind.None },
            1 => Sidewall with { OriginX = Sidewall.OriginX + 1 },
            2 => Sidewall with { OriginY = Sidewall.OriginY + 1 },
            3 => Sidewall with { OriginZ = Sidewall.OriginZ + 1 },
            4 => Sidewall with { ForwardX = Sidewall.ForwardX + 1 },
            5 => Sidewall with { ForwardZ = Sidewall.ForwardZ + 1 },
            6 => Sidewall with { RequiredSteps = (byte)(Sidewall.RequiredSteps + 1) },
            7 => Sidewall with { BackwardSteps = (byte)(Sidewall.BackwardSteps + 1) },
            _ => Sidewall with { ReturnSteps = (byte)(Sidewall.ReturnSteps + 1) },
        };

        long packed = PathNode.Pack(3, 64, -7);
        Assert.NotEqual(Key(packed, Sidewall, 2), Key(packed, changed, 2));
    }

    /// <summary>The equality is not merely "not obviously wrong": a hash set of keys over a grid of positions, bands and preparations holds exactly as many entries as there are distinct triples.</summary>
    [Fact]
    public void DistinctTriples_StayDistinctInAHashSet()
    {
        EntryPreparationState[] preparations =
        [
            EntryPreparationState.None,
            Sidewall,
            Sidewall with { BackwardSteps = 2 },
            Sidewall with { OriginX = 11 },
        ];

        var seen = new HashSet<AStarPathFinder.NodeKey>();
        int expected = 0;
        for (int x = 0; x < 12; x++)
            for (int y = 60; y < 68; y++)
                foreach (EntryPreparationState preparation in preparations)
                    for (int band = 0; band < 8; band++)
                    {
                        seen.Add(Key(PathNode.Pack(x, y, 0), preparation, band));
                        expected++;
                    }

        Assert.Equal(expected, seen.Count);
    }

    /// <summary>A key round-trips through a dictionary probe built from an independently constructed copy.</summary>
    [Fact]
    public void ADictionaryProbe_FindsAnIndependentlyBuiltKey()
    {
        var map = new Dictionary<AStarPathFinder.NodeKey, string>
        {
            [Key(PathNode.Pack(-9, 71, 240), Sidewall, 3)] = "hit",
        };

        Assert.True(map.TryGetValue(
            Key(PathNode.Pack(-9, 71, 240), new EntryPreparationState(
                EntryPreparationKind.SidewallRunup, 10, 64, -3, 1, 0, 2, 1, 0), 3),
            out string? value));
        Assert.Equal("hit", value);

        Assert.False(map.ContainsKey(Key(PathNode.Pack(-9, 71, 240), Sidewall, 4)));
    }
}
