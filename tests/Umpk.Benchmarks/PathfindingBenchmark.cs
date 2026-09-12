using BenchmarkDotNet.Attributes;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Pathfinding;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Physics;

namespace Umpk.Benchmarks;

/// <summary>Benchmarks the pathfinding module (the budget seam): planning time on a representative course, and the per-tick execution decision cost (one <c>PathExecutor.Tick</c> including the template's <see cref="PhysicsSimulator"/> lookahead). A per-tick budget keeps execution regressions surfacing in CI rather than as gameplay stutter.</summary>
/// <remarks><see cref="RegionCaptureBenchmark"/> covers region capture with boxes sized to the course's recorded <c>regionCells</c> distribution and with every column inside them loaded.</remarks>
[MemoryDiagnoser]
public class PathfindingBenchmark
{
    private const int FloorY = 64;
    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(770);

    private World _world = null!;
    private CourseBlockShapes _shapes = null!;
    private BlockPos _start;
    private BlockPos _goal;
    private PlanningWorldView _view = null!;
    private PathfinderOptions _options = null!;
    private PathExecutionContext _execCtx = null!;
    private IReadOnlyList<PathSegment> _segments = null!;
    private PathExecutor _executor = null!;
    private PhysicsState _tickState;

    [GlobalSetup]
    public void Setup()
    {
        (_world, _shapes) = BuildCourse();
        _start = new BlockPos(0, FloorY + 1, 0);
        _goal = new BlockPos(40, FloorY + 1, 8);
        _options = PathfinderOptions.Default;
        _view = PlanningWorldView.Capture(_world, _shapes, _start, _goal, margin: 16);

        PathResult plan = PathPlanner.FindPath(_view, _options, _start, new GoalBlock(_goal));
        _execCtx = new PathExecutionContext(_view, Profile);
        _segments = PathSegmentBuilder.FromPath(plan.Path);

        _executor = new PathExecutor(_execCtx, _segments);
        var engine = new PlayerPhysics(_view, Profile);
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(_segments.Count > 0 ? _segments[0].Start : new Vec3d(0.5, FloorY + 1, 0.5), 0f, 0f);
        engine.Step(MovementInput.None);
        _tickState = engine.State;
    }

    /// <summary>Plan a full path on a representative course (capture excluded; measured separately).</summary>
    [Benchmark]
    public PathStatus PlanCourse()
        => PathPlanner.FindPath(_view, _options, _start, new GoalBlock(_goal)).Status;

    /// <summary>One execution decision tick, including the template's forward-simulation lookahead.</summary>
    [Benchmark]
    public bool ExecutionTick()
    {
        PathExecutorTick step = _executor.Tick(_tickState);
        // Re-arm a fresh executor if it finished so the benchmark keeps measuring a mid-path tick.
        if (step.State != PathExecutorState.InProgress)
            _executor = new PathExecutor(_execCtx, _segments);

        return step.DeviationExceeded;
    }

    private static (World World, CourseBlockShapes Shapes) BuildCourse()
    {
        var dimType = Registry.FromEntries(
            RegistryIds.DimensionType,
            [0],
            [Identifier.Minecraft("overworld")],
            [new DimensionTypeDefinition(-64, 384, true)]);
        var dimension = new DimensionState(dimType[0], Identifier.Minecraft("overworld"));
        var biomes = new RegistryBuilder<BiomeDefinition>(RegistryIds.Biome, 1)
            .Add(0, Identifier.Minecraft("plains"), new BiomeDefinition())
            .Build();
        var world = new World(dimension, new CourseBlockData(), biomes);

        // A representative course: a long floor with a step-up shelf, a small drop, and a water pool.
        for (int x = -4; x <= 48; x++)
        {
            for (int z = -4; z <= 12; z++)
            {
                world.SetBlockStateId(new BlockPos(x, FloorY, z), 1); // stone floor
            }
        }

        // Step-up shelf x=12..20, one block higher.
        for (int x = 12; x <= 20; x++)
            for (int z = -4; z <= 12; z++)
                world.SetBlockStateId(new BlockPos(x, FloorY + 1, z), 1);

        // Water pool x=28..34 (deep), remove the floor and fill with water.
        for (int x = 28; x <= 34; x++)
        {
            for (int z = -1; z <= 2; z++)
            {
                world.SetBlockStateId(new BlockPos(x, FloorY, z), 0);
                world.SetBlockStateId(new BlockPos(x, FloorY - 1, z), 0);
                world.SetBlockStateId(new BlockPos(x, FloorY - 2, z), 1);
                for (int y = FloorY - 1; y <= FloorY + 1; y++)
                {
                    world.SetBlockStateId(new BlockPos(x, y, z), 2); // water
                }
            }
        }

        return (world, new CourseBlockShapes());
    }

    private sealed class CourseBlockShapes : IBlockShapeSource
    {
        private static readonly Aabb[] Cube = [new(0, 0, 0, 1, 1, 1)];
        private static readonly Aabb[] Empty = [];

        public ReadOnlySpan<Aabb> GetCollisionShapes(BlockState state) => Shapes(state);

        public ReadOnlySpan<Aabb> GetCollisionShapes(int stateId) => stateId == 1 ? Cube : Empty;

        public ReadOnlySpan<Aabb> GetOutlineShapes(BlockState state) => Shapes(state);

        public ReadOnlySpan<Aabb> GetOutlineShapes(int stateId) => stateId == 1 ? Cube : Empty;

        private static ReadOnlySpan<Aabb> Shapes(BlockState state) => state.BlocksMotion && !state.IsFluid ? Cube : Empty;
    }

    private sealed class CourseBlockData : IBlockDataSource
    {
        public CourseBlockData()
        {
            Blocks = new RegistryBuilder<BlockDefinition>(RegistryIds.Block, 3)
                .Add(0, Identifier.Minecraft("air"), new BlockDefinition(0, 0, 0))
                .Add(1, Identifier.Minecraft("stone"), new BlockDefinition(1, 1, 1))
                .Add(2, Identifier.Minecraft("water"), new BlockDefinition(2, 2, 2))
                .Build();
        }

        public Registry<BlockDefinition> Blocks { get; }

        public int UnknownStateId => 0;

        public bool IsLegacy => false;

        public int StateCount => 3;

        public bool IsValidState(int stateId) => stateId is >= 0 and < 3;

        public int GetBlockNetworkId(int stateId) => IsValidState(stateId) ? stateId : 0;

        public int GetDefaultStateId(int blockNetworkId) => blockNetworkId;

        public BlockFlags GetFlags(int stateId) => stateId switch
        {
            0 => BlockFlags.Air | BlockFlags.Replaceable,
            2 => BlockFlags.Fluid | BlockFlags.Replaceable,
            _ => BlockFlags.Solid | BlockFlags.BlocksMotion,
        };

        public float GetFriction(int stateId) => 0.6f;

        public float GetSpeedFactor(int stateId) => 1.0f;

        public float GetJumpFactor(int stateId) => 1.0f;

        public IReadOnlyList<string> GetPropertyNames(int stateId) => [];

        public bool TryGetPropertyValue(int stateId, string propertyName, out string value)
        {
            value = string.Empty;
            return false;
        }

        public bool TryDecodeLegacy(int stateId, out int blockId, out int meta)
        {
            blockId = -1;
            meta = -1;
            return false;
        }

        public int EncodeLegacy(int blockId, int meta) => (blockId << 4) | (meta & 0xF);
    }
}
