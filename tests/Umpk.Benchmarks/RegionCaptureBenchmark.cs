using BenchmarkDotNet.Attributes;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Pathfinding;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Goals;

namespace Umpk.Benchmarks;

/// <summary>The cost of the planner's region capture at the sizes it is actually asked for, and the split between capture and search in one plan.</summary>
/// <remarks>
/// <para>The boxes use representative planner region sizes: a start-equals-goal plan captures 49^3 = 117,649 cells, the 100-block row captures 149*49*49 = 357,749, and the 500-block row captures 549*49*49 = 1,318,149. The margin is 24, which is the margin <c>PhysicsEngineHolder.CapturePlan</c> passes, so the boxes here are the boxes a live session builds.</para>
/// <para>The fixture loads every column the box covers, with the two sections under the floor carrying a real 32-entry palette. That matters: an unloaded column is a null check and a single-value section captures without copying anything, so a fixture that leaves the box mostly empty measures the absence of terrain rather than the cost of copying it.</para>
/// <para><see cref="PlanAndCapture"/> exists because <c>PathDiagnostics.ElapsedMilliseconds</c> - the <c>planMs</c> the course records - measures the SEARCH only. Capture has never been on the same scale as anything the harness reports.</para>
/// </remarks>
[MemoryDiagnoser]
public class RegionCaptureBenchmark
{
    private const int FloorY = 64;
    private const int Margin = 24;

    private static readonly object BuildLock = new();
    private static World? sharedWorld;
    private static World? sharedSealedWorld;
    private static CaptureBlockShapes? sharedShapes;

    private World _world = null!;
    private CaptureBlockShapes _shapes = null!;
    private BlockPos _start;
    private BlockPos _goal;
    private PathfinderOptions _options = null!;

    /// <summary>The shape being planned. The three ordinary rows size the box to the recorded distribution; <c>sealed-100</c> puts a stone shell round the goal in the same box, so the search has to drain every reachable cell before it fails. That is the shape demand faulting cannot win on, and it is here to bound the loss rather than to be avoided.</summary>
    [Params("start=goal", "route-100", "route-500", "sealed-100")]
    public string Shape { get; set; } = "start=goal";

    /// <summary>Builds the runway worlds once per process and picks this run's start and goal.</summary>
    [GlobalSetup]
    public void Setup()
    {
        bool isSealed = Shape.StartsWith("sealed", StringComparison.Ordinal);
        (_world, _shapes) = SharedCourse(isSealed);
        _start = new BlockPos(0, FloorY + 1, 0);
        _goal = new BlockPos(RouteLengthOf(Shape), FloorY + 1, 0);
        _options = PathfinderOptions.Default;
    }

    /// <summary>The region capture alone, paid once per plan and once per replan.</summary>
    [Benchmark]
    public PlanningWorldView CaptureRegion()
        => PlanningWorldView.Capture(_world, _shapes, _start, _goal, Margin);

    /// <summary>Capture plus search, which is what a plan actually costs end to end.</summary>
    [Benchmark]
    public PathStatus PlanAndCapture()
    {
        PlanningWorldView view = PlanningWorldView.Capture(_world, _shapes, _start, _goal, Margin);
        return PathPlanner.FindPath(view, _options, _start, new GoalBlock(_goal)).Status;
    }

    /// <summary>The same plan with nothing copied up front: sections materialise as the search reads them.</summary>
    [Benchmark]
    public PathStatus PlanOnDemand()
    {
        PlanningWorldView view = PlanningWorldView.CaptureOnDemand(_world, _shapes, _start, _goal, Margin);
        return PathPlanner.FindPath(view, _options, _start, new GoalBlock(_goal)).Status;
    }

    private static int RouteLengthOf(string shape) => shape switch
    {
        "route-100" or "sealed-100" => 100,
        "route-500" => 500,
        _ => 0,
    };

    private static (World World, CaptureBlockShapes Shapes) SharedCourse(bool isSealed)
    {
        lock (BuildLock)
        {
            sharedShapes ??= new CaptureBlockShapes();

            if (!isSealed)
            {
                sharedWorld ??= BuildRunway().World;
                return (sharedWorld, sharedShapes);
            }

            if (sharedSealedWorld is null)
            {
                sharedSealedWorld = BuildRunway().World;
                SealGoal(sharedSealedWorld, 100);
            }

            return (sharedSealedWorld, sharedShapes);
        }
    }

    /// <summary>Encloses the goal in a stone shell so no route to it exists inside the box.</summary>
    private static void SealGoal(World world, int goalX)
    {
        for (int x = goalX - 3; x <= goalX + 3; x++)
            for (int z = -3; z <= 3; z++)
            {
                bool wall = x == goalX - 3 || x == goalX + 3 || z == -3 || z == 3;
                for (int y = FloorY + 1; y <= FloorY + 5; y++)
                    if (wall || y == FloorY + 5)
                        world.SetBlockStateId(new BlockPos(x, y, z), CaptureBlockData.Stone(1));

            }

    }

    /// <summary>A straight runway from x = -Margin to x = 500 + Margin, wide enough to cover the widest box, with every column loaded and the sub-floor sections palettised.</summary>
    private static (World World, CaptureBlockShapes Shapes) BuildRunway()
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
        var data = new CaptureBlockData();
        var world = new World(dimension, data, biomes);

        int minX = -Margin, maxX = 500 + Margin;
        int minZ = -Margin, maxZ = Margin;
        int minY = FloorY + 1 - Margin, maxY = FloorY + 1 + Margin;

        int firstSection = (minY - dimension.MinY) >> 4;
        int lastSection = (maxY - dimension.MinY) >> 4;

        for (int cx = minX >> 4; cx <= maxX >> 4; cx++)
            for (int cz = minZ >> 4; cz <= maxZ >> 4; cz++)
            {
                var column = new ChunkColumn(new ChunkPos(cx, cz), dimension, data.StateCount);
                for (int s = firstSection; s <= lastSection; s++)
                {
                    int sectionMinY = dimension.MinY + (s * ChunkSection.Size);
                    column.SetSection(s, BuildSection(sectionMinY, cx, cz));
                }

                world.LoadColumn(column);
            }

        return (world, new CaptureBlockShapes());
    }

    private static ChunkSection BuildSection(int sectionMinY, int cx, int cz)
    {
        int sectionMaxY = sectionMinY + ChunkSection.Size - 1;

        if (sectionMinY > FloorY)
        {
            // Above the floor: open air, which really is a single-value section in a live world.
            return ChunkSection.Filled(CaptureBlockData.Air, 0, 0);
        }

        if (sectionMaxY < FloorY)
        {
            // Under the floor: a 32-entry palette, the shape ordinary stone-and-ore terrain has.
            var solid = ChunkSection.Filled(CaptureBlockData.Stone(0), 0, 0);
            for (int i = 0; i < ChunkSection.BlockCells; i++)
            {
                int x = i & 15, z = (i >> 4) & 15, y = i >> 8;
                solid.SetBlockStateId(x, y, z, CaptureBlockData.Stone(((x * 7) + (z * 13) + (y * 3) + cx + cz) & 31));
            }

            return solid;
        }

        // The section straddling the floor: solid up to and including the floor, air above it.
        var mixed = ChunkSection.Filled(CaptureBlockData.Air, 0, 0);
        for (int y = sectionMinY; y <= FloorY; y++)
            for (int z = 0; z < ChunkSection.Size; z++)
                for (int x = 0; x < ChunkSection.Size; x++)
                    mixed.SetBlockStateId(x, y - sectionMinY, z, CaptureBlockData.Stone(((x * 7) + (z * 13) + (y * 3)) & 31));

        return mixed;
    }

    /// <summary>Block collision shapes for the capture fixture: solids are full cubes, nothing else collides.</summary>
    private sealed class CaptureBlockShapes : IBlockShapeSource
    {
        private static readonly Aabb[] Cube = [new(0, 0, 0, 1, 1, 1)];
        private static readonly Aabb[] Empty = [];

        public ReadOnlySpan<Aabb> GetCollisionShapes(BlockState state) => state.BlocksMotion && !state.IsFluid ? Cube : Empty;

        public ReadOnlySpan<Aabb> GetCollisionShapes(int stateId) => stateId >= CaptureBlockData.StoneBase ? Cube : Empty;

        public ReadOnlySpan<Aabb> GetOutlineShapes(BlockState state) => GetCollisionShapes(state);

        public ReadOnlySpan<Aabb> GetOutlineShapes(int stateId) => GetCollisionShapes(stateId);
    }

    /// <summary>A block data source with a state count in the hundreds, so a section can carry a genuine multi-entry palette instead of collapsing to the three ids a toy fixture has.</summary>
    private sealed class CaptureBlockData : IBlockDataSource
    {
        public const int Air = 0;
        public const int Water = 1;
        public const int StoneBase = 2;
        public const int StoneVariants = 400;

        public CaptureBlockData()
        {
            var builder = new RegistryBuilder<BlockDefinition>(RegistryIds.Block, StoneBase + StoneVariants);
            builder.Add(Air, Identifier.Minecraft("air"), new BlockDefinition(Air, Air, Air));
            builder.Add(Water, Identifier.Minecraft("water"), new BlockDefinition(Water, Water, Water));
            for (int i = 0; i < StoneVariants; i++)
            {
                int id = StoneBase + i;
                builder.Add(id, Identifier.Minecraft("stone_" + i), new BlockDefinition(id, id, id));
            }

            Blocks = builder.Build();
        }

        public Registry<BlockDefinition> Blocks { get; }

        public int UnknownStateId => 0;

        public bool IsLegacy => false;

        public int StateCount => StoneBase + StoneVariants;

        public static int Stone(int i) => StoneBase + (i % StoneVariants);

        public bool IsValidState(int stateId) => stateId >= 0 && stateId < StateCount;

        public int GetBlockNetworkId(int stateId) => IsValidState(stateId) ? stateId : 0;

        public int GetDefaultStateId(int blockNetworkId) => blockNetworkId;

        public BlockFlags GetFlags(int stateId) => stateId switch
        {
            Air => BlockFlags.Air | BlockFlags.Replaceable,
            Water => BlockFlags.Fluid | BlockFlags.Replaceable,
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
