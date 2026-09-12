using BenchmarkDotNet.Attributes;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Physics;

namespace Umpk.Benchmarks;

/// <summary>Benchmarks one 20-TPS physics <see cref="PlayerPhysics.Step"/> with <see cref="MemoryDiagnoser"/> to prove the steady-state tick allocates zero bytes and to track per-tick cost. Uses a self-contained in-memory world so the benchmark has no external inputs.</summary>
[MemoryDiagnoser]
public class PhysicsTickBenchmark
{
    private PlayerPhysics _flatWalker = null!;
    private PlayerPhysics _stepUpWalker = null!;
    private readonly MovementInput _sprint = new() { Forward = true, Sprint = true };
    private readonly MovementInput _sprintJump = new() { Forward = true, Sprint = true, Jump = true };

    [GlobalSetup]
    public void Setup()
    {
        var flat = new BenchWorld();
        flat.Floor(-8, 400, 63, BlockKind.Stone);
        _flatWalker = new PlayerPhysics(flat, PhysicsProfile.Modern);
        _flatWalker.SetConditions(PhysicsConditions.Default);
        _flatWalker.Reset(new Vec3d(0.5, 64, 0.5), 0f, 0f);

        var stair = new BenchWorld();
        stair.Floor(-8, 400, 63, BlockKind.Stone);
        stair.Fill(3, 64, 400, 64, BlockKind.Slab);
        _stepUpWalker = new PlayerPhysics(stair, PhysicsProfile.Modern);
        _stepUpWalker.SetConditions(PhysicsConditions.Default);
        _stepUpWalker.Reset(new Vec3d(0.5, 64, 2.0), 0f, 0f);

        // Warm the buffers.
        for (int i = 0; i < 100; i++)
        {
            _flatWalker.Step(_sprint);
            _stepUpWalker.Step(_sprint);
        }
    }

    [Benchmark(Baseline = true)]
    public PhysicsState FlatSprintStep() => _flatWalker.Step(_sprint).State;

    [Benchmark]
    public PhysicsState SprintJumpStep() => _flatWalker.Step(_sprintJump).State;

    [Benchmark]
    public PhysicsState StepUpStep() => _stepUpWalker.Step(_sprint).State;

    /// <summary>A tiny voxel world for the benchmark. Kept internal to the benchmark project so it does not take a dependency on the test project. Blocks default to air.</summary>
    private sealed class BenchWorld : IPhysicsWorldView
    {
        private static readonly Aabb FullCube = new(0, 0, 0, 1, 1, 1);
        private static readonly Aabb SlabBox = new(0, 0, 0, 1, 0.5, 1);
        private static readonly Aabb[] Empty = [];
        private static readonly Aabb[] Cube = [FullCube];
        private static readonly Aabb[] Slab = [SlabBox];

        private readonly BenchBlockData _data = new();
        private readonly Dictionary<BlockPos, BlockKind> _blocks = [];

        public void Floor(int x1, int x2, int y, BlockKind kind)
        {
            for (int x = x1; x <= x2; x++)
                _blocks[new BlockPos(x, y, 0)] = kind;

        }

        public void Fill(int x1, int y1, int x2, int y2, BlockKind kind)
        {
            for (int x = x1; x <= x2; x++)
                for (int y = y1; y <= y2; y++)
                    _blocks[new BlockPos(x, y, 0)] = kind;

        }

        public BlockState GetBlock(BlockPos pos)
        {
            BlockKind kind = _blocks.TryGetValue(pos with { Z = 0 }, out BlockKind k) ? k : BlockKind.Air;
            return new BlockState(_data, (int)kind);
        }

        public ReadOnlySpan<Aabb> GetCollisionShapes(BlockState state) => (BlockKind)state.StateId switch
        {
            BlockKind.Air => Empty,
            BlockKind.Slab => Slab,
            _ => Cube,
        };

        public bool IsChunkLoaded(BlockPos pos) => true;
    }

    private enum BlockKind
    {
        Air = 0,
        Stone = 1,
        Slab = 2,
    }

    private sealed class BenchBlockData : IBlockDataSource
    {
        private readonly Registry<BlockDefinition> _blocks = Registry.FromEntries<BlockDefinition>(
            Identifier.Minecraft("block"),
            [0, 1, 2],
            [Identifier.Minecraft("air"), Identifier.Minecraft("stone"), Identifier.Minecraft("smooth_stone_slab")],
            [new BlockDefinition(0, 0, 0), new BlockDefinition(1, 1, 1), new BlockDefinition(2, 2, 2)]);

        public Registry<BlockDefinition> Blocks => _blocks;

        public int UnknownStateId => 0;

        public bool IsLegacy => false;

        public int StateCount => 3;

        public bool IsValidState(int stateId) => stateId is >= 0 and < 3;

        public int GetBlockNetworkId(int stateId) => IsValidState(stateId) ? stateId : 0;

        public int GetDefaultStateId(int blockNetworkId) => blockNetworkId;

        public BlockFlags GetFlags(int stateId) => stateId switch
        {
            0 => BlockFlags.Air,
            2 => BlockFlags.BlocksMotion,
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

        public int EncodeLegacy(int blockId, int meta) => (blockId << 4) | meta;
    }
}
