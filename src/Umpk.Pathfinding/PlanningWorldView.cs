using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Physics;

namespace Umpk.Pathfinding;

/// <summary>
/// A point-in-time world view for planning, backed by a <see cref="RegionSnapshot"/>. Packed section payloads inside the region of interest are copied once, and every read uses that frozen copy. The view also implements <see cref="IPhysicsWorldView"/> so execution templates can forward-simulate against the same frozen terrain through <see cref="PhysicsSimulator"/>.
///
/// <para>Positions outside the captured region resolve to the snapshot's air/unknown state and are reported as an unloaded chunk.</para>
///
/// <para><see cref="Capture"/> copies every section in the box before the search starts; <see cref="CaptureOnDemand"/> copies each one the first time a read lands inside it. Both are bounded by the same box and answer every read identically, so they search the same graph. They differ in who may hold the result: a full capture is freely shareable, a demand-faulted one has one owner at a time.</para>
/// </summary>
public sealed class PlanningWorldView : IPhysicsWorldView
{
    private readonly RegionSnapshot _snapshot;
    private readonly IBlockShapeSource _shapes;
    private bool _waterChecked;
    private bool _mayContainWater;
    private bool _barrierChecked;
    private bool _mayContainBarrier;
    private bool _shapeOffsetChecked;
    private bool _mayContainShapeOffset;

    /// <summary>Wraps a captured region and its per-version block-shape source.</summary>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public PlanningWorldView(RegionSnapshot snapshot, IBlockShapeSource shapes)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(shapes);
        _snapshot = snapshot;
        _shapes = shapes;
    }

    /// <summary>The captured region.</summary>
    public RegionSnapshot Region => _snapshot;

    /// <summary>The per-version block-shape source, for callers (<see cref="Moves.MoveHelper.CanWalkOn"/> under <see cref="PathfinderOptions.AllowPartialHeightSupport"/>) that need a state's own collision boxes rather than just its <see cref="Umpk.Game.Blocks.BlockFlags.Solid"/> flag.</summary>
    public IBlockShapeSource Shapes => _shapes;

    /// <summary>Captures the start-to-goal bounding volume (plus a margin) from a live world into an immutable planning view. Capture cost is bounded and paid once per plan or replan, never per block update.</summary>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="margin"/> is negative.</exception>
    public static PlanningWorldView Capture(World world, IBlockShapeSource shapes, BlockPos a, BlockPos b, int margin = 16)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(shapes);
        ArgumentOutOfRangeException.ThrowIfNegative(margin);

        (BlockPos min, BlockPos max) = Bounds(a, b, margin);
        return new PlanningWorldView(world.CopyRegion(min, max, includeBiomes: false), shapes);
    }

    /// <summary>The same box as <see cref="Capture"/>, bounded identically, but materialised section by section on first read instead of copied up front. Construction is O(sections in the box) pointers and does not read a single block.</summary>
    /// <remarks>
    /// <para>The search reads a route, not a box. Instrumented over the shapes the pathfinding course records, a successful search touches 0.2 to 0.6% of the cells its region holds - 8,026 distinct cells of 1,318,149 on the 500-block row - because the 24-block margin buys 49 blocks of lateral and vertical slack where a straight route uses five. Capturing the box up front is therefore about two hundred times more copying than the search will ask for, and it is copying that has to finish before the search can start.</para>
    /// <para>The box is deliberately kept. A view bounded exactly as the eager capture was bounded answers <c>IsChunkLoaded</c> the same way, hits the same <c>unloadedChunkHits</c> boundary, and therefore searches the same graph and returns the same path. That identity keeps performance measurements comparable with eager capture.</para>
    /// <para>The cost is paid where the search does not go: a search that has to drain most of its box (an unreachable goal) faults nearly every section anyway and pays about 10% more for the privilege of having done it lazily. That case is the one that was already the slowest, and the 10% sits inside its budget by three orders of magnitude.</para>
    /// <para>A view built this way is SINGLE-OWNER; see <see cref="RegionSnapshot"/>.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="margin"/> is negative.</exception>
    public static PlanningWorldView CaptureOnDemand(World world, IBlockShapeSource shapes, BlockPos a, BlockPos b, int margin = 16)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(shapes);
        ArgumentOutOfRangeException.ThrowIfNegative(margin);

        (BlockPos min, BlockPos max) = Bounds(a, b, margin);
        return new PlanningWorldView(world.ViewRegion(min, max, includeBiomes: false), shapes);
    }

    private static (BlockPos Min, BlockPos Max) Bounds(BlockPos a, BlockPos b, int margin) => (
        new BlockPos(
            Math.Min(a.X, b.X) - margin,
            Math.Min(a.Y, b.Y) - margin,
            Math.Min(a.Z, b.Z) - margin),
        new BlockPos(
            Math.Max(a.X, b.X) + margin,
            Math.Max(a.Y, b.Y) + margin,
            Math.Max(a.Z, b.Z) + margin));

    /// <summary>Whether the region may hold water anywhere: waterlogged states included, lava excluded, exactly as <see cref="Moves.MoveHelper.IsWater"/> decides it.</summary>
    /// <remarks>
    /// <para>Answered from the sections' palettes, not by reading cells, and cached for the life of the view because the answer costs a few hundred predicate calls against a box of a million cells. FALSE is the useful direction and it is exact: no section overlapping the box can produce a water state, so no read of this view can return one. TRUE is permissive - a palette is a superset of what its cells hold, and a direct-width section answers true unconditionally - which only means the caller does the work it would have done anyway.</para>
    /// <para>On a demand-faulted view the answer describes the world at the moment it is first asked, which is inside the search. Water placed in the box after that is the ordinary terrain-changed- under-a-plan case, and the ordinary answers apply: <c>BreathValidator</c> still checks the finished plan, and a route that no longer works is replanned.</para>
    /// <para>The cache is a plain field. The demand-faulted view has one owner, and on a shared full capture two threads racing to compute it compute the same value from immutable copies.</para>
    /// </remarks>
    public bool MayContainWater
    {
        get
        {
            if (!_waterChecked)
            {
                _mayContainWater = _snapshot.MayContainBlockState(static state => Moves.MoveHelper.IsWater(state));
                _waterChecked = true;
            }

            return _mayContainWater;
        }
    }

    /// <summary>Whether the region may hold a door, trapdoor or fence gate anywhere, exactly as <see cref="Moves.MoveHelper.IsBarrierFamily"/> decides it.</summary>
    /// <remarks>The same palette question <see cref="MayContainWater"/> asks, and it exists for the same reason: the barrier family costs <see cref="Moves.MoveHelper.CanWalkThrough"/> a registry-id suffix test on every motion-blocking cell and one extra block read under every passable one, and the overwhelming majority of regions contain no such block at all. FALSE is exact - no section overlapping the box can produce a barrier state, so no read of this view can return one - and TRUE is permissive, which only means the caller does the work it would have done anyway.</remarks>
    public bool MayContainBarrier
    {
        get
        {
            if (!_barrierChecked)
            {
                _mayContainBarrier = _snapshot.MayContainBlockState(static state => Moves.MoveHelper.IsBarrierFamily(state));
                _barrierChecked = true;
            }

            return _mayContainBarrier;
        }
    }

    /// <summary>Whether the region may hold a block whose collision box is moved by its own position, which is the only family a lateral squeeze lane can ever open past.</summary>
    /// <remarks>
    /// <para>The same palette question <see cref="MayContainWater"/> and <see cref="MayContainBarrier"/> ask, and FALSE is exact in the same way: no section overlapping the box can produce such a state, so no read of this view can return one.</para>
    /// <para><b>This is what makes the feature free everywhere it does not apply.</b> A body offset to a cell face needs 0.6 of clear run on one side of a box, and a CENTRED box of width <c>w</c> offers <c>(1 - w) / 2</c>, which reaches 0.6 only at a negative width. So no block outside this family can open a lane at any position, and a region with none of them in it can skip the question outright rather than measuring boxes that cannot answer it. See <c>Umpk.Physics.BlockShapeOffset.HasOffset</c>.</para>
    /// </remarks>
    public bool MayContainShapeOffset
    {
        get
        {
            if (!_shapeOffsetChecked)
            {
                _mayContainShapeOffset = _snapshot.MayContainBlockState(
                    static state => Umpk.Physics.BlockShapeOffset.HasOffset(state));
                _shapeOffsetChecked = true;
            }

            return _mayContainShapeOffset;
        }
    }

    /// <summary>The block state at a position (air/unknown outside the captured region).</summary>
    public BlockState GetBlock(BlockPos pos) => _snapshot.GetBlock(pos);

    /// <inheritdoc/>
    public ReadOnlySpan<Aabb> GetCollisionShapes(BlockState state) => _shapes.GetCollisionShapes(state);

    /// <inheritdoc/>
    public bool IsChunkLoaded(BlockPos pos) => _snapshot.Contains(pos);
}
