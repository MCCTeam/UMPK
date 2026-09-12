// PhysicsWalk steps the vanilla movement engine with no server attached. It builds a tiny flat world in memory, drops a player onto it, walks forward for three seconds, and prints where the player ends up.
//
// Run it like this:
//
//   dotnet run --project samples/PhysicsWalk
//
// The engine is tick based, 20 ticks per second, same as vanilla. One Step call is one tick. There is no solver here, only simulation, so the sample settles onto the ground first before it measures anything.
using Umpk;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Physics;

// A flat stone floor at y 63, air everywhere else. The player spawns above it, falls, lands, then walks. All positions are loaded, so IsChunkLoaded is true.
var world = new FlatWorld();
var engine = new PlayerPhysics(world, PhysicsProfile.Modern);
engine.SetConditions(PhysicsConditions.Default);
engine.Reset(new Vec3d(0.5, 65.0, 0.5), yaw: 0f, pitch: 0f);

// Settle first. Reset zeroes velocity and flags, so the engine does not know it stands on anything yet. Two quiet ticks give it ground and fluid state before the real inputs start. Every physics test in the repo does this.
engine.Step(MovementInput.None);
engine.Step(MovementInput.None);
Console.WriteLine($"Start: {Format(engine.State.Position)} onGround={engine.State.OnGround}");

// Walk forward for 60 ticks, which is three seconds of game time. The player accelerates from standstill, so the distance stays short of steady speed times time. The floor is wide on purpose, so the walk stays on solid ground instead of running off the edge mid sample.
var walking = new MovementInput { Forward = true };
for (int tick = 1; tick <= 60; tick++)
{
    StepResult result = engine.Step(walking);
    if (result.Events.Landed)
    {
        Console.WriteLine($"Tick {tick}: landed after falling {result.Events.LandingFallDistance:F2} blocks.");
    }

    if (tick % 20 == 0)
    {
        Console.WriteLine($"Tick {tick}: {Format(result.State.Position)} speed={HorizontalSpeed(result.State.Velocity):F2} b/s");
    }
}

Console.WriteLine($"End: {Format(engine.State.Position)} onGround={engine.State.OnGround}");
Console.WriteLine();

// One jump from a fresh standing start, then coast. Reset first so the jump starts on solid ground instead of wherever the walk ended.
engine.Reset(new Vec3d(0.5, 64.0, 0.5), yaw: 0f, pitch: 0f);
engine.Step(MovementInput.None);
engine.Step(MovementInput.None);
StepResult jump = engine.Step(new MovementInput { Forward = true, Jump = true });
Console.WriteLine($"After jump tick: {Format(jump.State.Position)} vertical={jump.State.Velocity.Y:F3}");
for (int tick = 0; tick < 10; tick++)
{
    engine.Step(walking);
}

Console.WriteLine($"Ten ticks later: {Format(engine.State.Position)} onGround={engine.State.OnGround}");

return 0;

static string Format(Vec3d p) => $"({p.X:F2}, {p.Y:F2}, {p.Z:F2})";

static double HorizontalSpeed(Vec3d v) => Math.Sqrt(v.X * v.X + v.Z * v.Z) * 20.0;

// A minimal block table with two states. Air is 0, stone is 1. State id equals block id here, which keeps the mapping trivial. Real versions use JavaGameData for this. This sample stays small on purpose so the movement logic stays visible instead of hiding behind data loading.
sealed class SimpleBlockData : IBlockDataSource
{
    private readonly Registry<BlockDefinition> _blocks;

    public SimpleBlockData()
    {
        int[] ids = [0, 1];
        Identifier[] keys = [Identifier.Minecraft("air"), Identifier.Minecraft("stone")];
        var values = new BlockDefinition[]
        {
            new(0, 0, 0),
            new(1, 1, 1),
        };
        _blocks = Registry.FromEntries<BlockDefinition>(Identifier.Minecraft("block"), ids, keys, values);
    }

    public Registry<BlockDefinition> Blocks => _blocks;

    public int UnknownStateId => 0;

    public bool IsLegacy => false;

    public int StateCount => 2;

    public bool IsValidState(int stateId) => stateId is 0 or 1;

    public int GetBlockNetworkId(int stateId) => IsValidState(stateId) ? stateId : 0;

    public int GetDefaultStateId(int blockNetworkId) => blockNetworkId is 0 or 1 ? blockNetworkId : 0;

    public BlockFlags GetFlags(int stateId) => stateId switch
    {
        0 => BlockFlags.Air | BlockFlags.Replaceable,
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

// A flat world view over that table. Collision boxes use local block space, 0 to 1. Air has no boxes. Stone is a full cube.
sealed class FlatWorld : IPhysicsWorldView
{
    private static readonly Aabb FullCube = new(0, 0, 0, 1, 1, 1);
    private static readonly Aabb[] Empty = [];
    private static readonly Aabb[] Cube = [FullCube];

    private readonly SimpleBlockData _data = new();

    public BlockState GetBlock(BlockPos pos)
    {
        // Floor at y 63, forty blocks each way. Everything else is air. Wide enough that a 60 tick walk stays on solid ground.
        bool floor = pos.Y == 63 && pos.X >= -40 && pos.X <= 40 && pos.Z >= -40 && pos.Z <= 40;
        return new BlockState(_data, floor ? 1 : 0);
    }

    public ReadOnlySpan<Aabb> GetCollisionShapes(BlockState state) =>
        state.StateId == 1 ? Cube : Empty;

    public bool IsChunkLoaded(BlockPos pos) => true;

    public void CollectEntityColliders(in Aabb region, ICollection<Aabb> into)
    {
    }
}
