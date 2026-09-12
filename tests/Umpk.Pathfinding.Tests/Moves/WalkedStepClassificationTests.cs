using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Xunit;

namespace Umpk.Pathfinding.Tests.Moves;

public sealed class WalkedStepClassificationTests
{
    private const int FloorY = 60;
    private const double Tolerance = 1.0E-6;

    /// <summary>A slab kerb: the body steps up 0.5 and walks, at the walk price.</summary>
    [Fact]
    public void AKerbOntoASlab_IsAWalk()
    {
        (MoveType type, double cost) = Step(FixtureWorld.BottomSlab, up: true);

        Assert.Equal(MoveType.Traverse, type);
        Assert.Equal(ActionCosts.SprintOneBlock, cost, Tolerance);
    }

    /// <summary>And the way back down it, which the cardinal descend move owns rather than the step arm.</summary>
    [Fact]
    public void AKerbOffASlab_IsAWalk()
    {
        (MoveType type, double cost) = Step(FixtureWorld.BottomSlab, up: false);

        Assert.Equal(MoveType.Traverse, type);
        Assert.Equal(ActionCosts.SprintOneBlock, cost, Tolerance);
    }

    /// <summary>The watch item. A full block rises 1.0, which is above the auto-step, so it stays exactly the <c>Ascend</c> it has always been at exactly the price it has always cost. Course rows B2 <c>stairs10</c> and C9 <c>stairdown</c> are full-block staircases and must not move.</summary>
    [Fact]
    public void AFullBlockStep_IsStillAnAscend()
    {
        (MoveType type, double cost) = Step(FixtureWorld.Stone, up: true);

        Assert.Equal(MoveType.Ascend, type);
        Assert.Equal(ActionCosts.SprintOneBlock + ActionCosts.JumpPenalty, cost, Tolerance);
    }

    /// <summary>And the full-block drop stays a <c>Descend</c>, priced with its fall.</summary>
    [Fact]
    public void AFullBlockDrop_IsStillADescend()
    {
        (MoveType type, double cost) = Step(FixtureWorld.Stone, up: false);

        Assert.Equal(MoveType.Descend, type);
        Assert.Equal(ActionCosts.WalkOffBlock + ActionCosts.FallCost(1), cost, Tolerance);
    }

    /// <summary>A bottom-half stair rises a whole 1.0 in resolved elevation and is STILL walked, because its entry is two 0.5 shelves and the body takes them one at a time. This is what the ladder buys over a flat "destination footprint support" comparison, and it is the difference between the fixture's 33-tick walk and its 72-tick jump-held run.</summary>
    [Fact]
    public void AStairTreadEnteredFromTheLowSide_IsAWalk()
    {
        // FixtureBlockShapes' bottom stair is [[0,0,0,1,0.5,1],[0,0.5,0,1,1,0.5]]: the raised half sits against the cell's LOW-Z face, so the low shelf is met first coming from high Z.
        (MoveType type, double cost) = OntoTheTread(fromZ: 3, dz: -1);

        Assert.Equal(MoveType.Traverse, type);
        Assert.Equal(ActionCosts.SprintOneBlock, cost, Tolerance);
    }

    /// <summary>The same tread entered against its riser is one 1.0 shelf, and stays an <c>Ascend</c>. This is the direction-awareness earning its place: a cell-centre support height answers 1.0 from all four sides and would call this a walk too.</summary>
    [Fact]
    public void AStairTreadEnteredFromTheRiserSide_IsStillAnAscend()
    {
        (MoveType type, double cost) = OntoTheTread(fromZ: 1, dz: +1);

        Assert.Equal(MoveType.Ascend, type);
        Assert.Equal(ActionCosts.SprintOneBlock + ActionCosts.JumpPenalty, cost, Tolerance);
    }

    private static (MoveType Type, double Cost) OntoTheTread(int fromZ, int dz)
    {
        var world = new FixtureWorld();
        world.Floor(-6, 10, -6, 10, FloorY);
        world.Fill(-6, FloorY + 1, 2, 10, FloorY + 1, 2, FixtureWorld.StairsBottom);

        return Emitted(
            world,
            new BlockPos(0, FloorY + 1, fromZ),
            new BlockPos(0, FloorY + 2, fromZ + dz));
    }

    /// <summary>A slab kerb climbed FROM a slab is a full 1.0 and must not be walked. The naive reading of the ladder - which opens at the destination cell's own floor - calls this two 0.5 shelves and is wrong; the body is standing half a block down and takes the whole rise at once.</summary>
    [Fact]
    public void ASlabKerbClimbedFromASlab_IsStillAnAscend()
    {
        var world = new FixtureWorld();
        world.Floor(-6, 10, -4, 4, FloorY);
        world.Fill(-6, FloorY + 1, -4, 0, FloorY + 1, 4, FixtureWorld.BottomSlab);
        world.Fill(1, FloorY + 2, -4, 10, FloorY + 2, 4, FixtureWorld.BottomSlab);
        world.Fill(1, FloorY + 1, -4, 10, FloorY + 1, 4, FixtureWorld.Stone);

        (MoveType type, double cost) = Emitted(world, new BlockPos(0, FloorY + 2, 0), new BlockPos(1, FloorY + 3, 0));

        Assert.Equal(MoveType.Ascend, type);
        Assert.Equal(ActionCosts.SprintOneBlock + ActionCosts.JumpPenalty, cost, Tolerance);
    }

    /// <summary>A carpet lies ON the floor rather than raising it, so the cell beside it is a whole node-Y lower and the planner has always called the step off it a <c>Descend</c> with a fall in its price. The real drop is 0.0625.</summary>
    [Fact]
    public void SteppingOffACarpet_IsAWalk()
    {
        var world = new FixtureWorld();
        world.Floor(-6, 10, -4, 4, FloorY);
        world.Fill(-6, FloorY + 1, -4, 0, FloorY + 1, 4, FixtureWorld.Carpet);

        (MoveType type, double cost) = Emitted(world, new BlockPos(0, FloorY + 2, 0), new BlockPos(1, FloorY + 1, 0));

        Assert.Equal(MoveType.Traverse, type);
        Assert.Equal(ActionCosts.SprintOneBlock, cost, Tolerance);
    }

    /// <summary>One step onto a support laid on the floor plane, entered along +X.</summary>
    private static (MoveType Type, double Cost) Step(int stateId, bool up)
    {
        var world = new FixtureWorld();
        world.Floor(-6, 10, -6, 10, FloorY);
        world.Fill(1, FloorY + 1, -6, 10, FloorY + 1, 10, stateId);

        var low = new BlockPos(0, FloorY + 1, 0);
        var high = new BlockPos(1, FloorY + 2, 0);
        return up ? Emitted(world, low, high) : Emitted(world, high, low);
    }

    /// <summary>The move the real expander set emits from one cell to another, or a failure naming what it did emit.</summary>
    private static (MoveType Type, double Cost) Emitted(FixtureWorld world, BlockPos from, BlockPos to)
    {
        CalculationContext ctx = FixtureContext.Build(world, from, to, PathfinderOptions.Default);
        IMoveExpander[] expanders = AStarPathFinder.BuildDefaultExpanders();
        var buffer = new MoveNeighbor[expanders.Sum(e => e.MaxNeighbors)];

        var found = new List<MoveNeighbor>();
        int offset = 0;
        foreach (IMoveExpander expander in expanders)
        {
            Span<MoveNeighbor> slot = buffer.AsSpan(offset, expander.MaxNeighbors);
            int produced = expander.Expand(ctx, from.X, from.Y, from.Z, slot);
            offset += expander.MaxNeighbors;
            for (int i = 0; i < produced; i++)
                if (slot[i].DestX == to.X && slot[i].DestY == to.Y && slot[i].DestZ == to.Z)
                    found.Add(slot[i]);

        }

        Assert.True(
            found.Count == 1,
            $"expected exactly one move ({from.X},{from.Y},{from.Z}) -> ({to.X},{to.Y},{to.Z}), got "
            + $"{found.Count}: {string.Join(", ", found.Select(f => $"{f.MoveType}@{f.Cost:0.####}"))}");

        return (found[0].MoveType, found[0].Cost);
    }
}
