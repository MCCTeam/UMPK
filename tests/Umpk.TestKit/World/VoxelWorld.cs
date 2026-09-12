using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Physics;

namespace Umpk.TestKit.World;

/// <summary>A hand-built in-memory voxel world usable as both an <see cref="IBlockDataSource"/> (so <see cref="BlockState"/> values resolve) and an <see cref="IPhysicsWorldView"/> (so the physics engine and pathfinder can run against it). Build one with <see cref="VoxelWorldBuilder"/>. The block table is the small fixed <see cref="VoxelBlock"/> set; state id equals the enum value.</summary>
/// <remarks>This provides the "voxel world builder" TestKit item and follows the rule that tests supply their own block/shape table over an in-memory map rather than pulling generated data. Positions with no explicitly-set block read as air; chunks are considered loaded within the builder's declared bounds and unloaded outside (so off-world fall behavior is testable).</remarks>
public sealed class VoxelWorld : IBlockDataSource, IPhysicsWorldView
{
    private static readonly string[] Paths = ["air", "stone", "ice", "smooth_stone_slab", "ladder", "water"];

    private static readonly Aabb FullCube = new(0, 0, 0, 1, 1, 1);
    private static readonly Aabb BottomSlab = new(0, 0, 0, 1, 0.5, 1);
    private static readonly Aabb[] Empty = [];
    private static readonly Aabb[] Full = [FullCube];
    private static readonly Aabb[] Slab = [BottomSlab];

    private readonly Dictionary<BlockPos, int> _blocks;
    private readonly Aabb _loadedBounds;
    private readonly Registry<BlockDefinition> _registry;

    internal VoxelWorld(Dictionary<BlockPos, int> blocks, Aabb loadedBounds)
    {
        _blocks = blocks;
        _loadedBounds = loadedBounds;

        int n = Paths.Length;
        var networkIds = new int[n];
        var keys = new Identifier[n];
        var values = new BlockDefinition[n];
        for (int i = 0; i < n; i++)
        {
            networkIds[i] = i;
            keys[i] = Identifier.Minecraft(Paths[i]);
            values[i] = new BlockDefinition(i, i, i);
        }

        _registry = Registry.FromEntries<BlockDefinition>(Identifier.Minecraft("block"), networkIds, keys, values);
    }

    // IBlockDataSource

    /// <inheritdoc />
    public Registry<BlockDefinition> Blocks => _registry;

    /// <inheritdoc />
    public int UnknownStateId => (int)VoxelBlock.Air;

    /// <inheritdoc />
    public bool IsLegacy => false;

    /// <inheritdoc />
    public int StateCount => Paths.Length;

    /// <inheritdoc />
    public bool IsValidState(int stateId) => stateId >= 0 && stateId < Paths.Length;

    /// <inheritdoc />
    public int GetBlockNetworkId(int stateId) => IsValidState(stateId) ? stateId : UnknownStateId;

    /// <inheritdoc />
    public int GetDefaultStateId(int blockNetworkId) => blockNetworkId;

    /// <inheritdoc />
    public BlockFlags GetFlags(int stateId) => (VoxelBlock)stateId switch
    {
        VoxelBlock.Air => BlockFlags.Air | BlockFlags.Replaceable,
        VoxelBlock.Water => BlockFlags.Fluid | BlockFlags.Replaceable,
        VoxelBlock.Ladder => BlockFlags.Climbable,
        VoxelBlock.Slab => BlockFlags.BlocksMotion,
        _ => BlockFlags.Solid | BlockFlags.BlocksMotion,
    };

    /// <inheritdoc />
    public float GetFriction(int stateId) => (VoxelBlock)stateId == VoxelBlock.Ice ? 0.98f : 0.6f;

    /// <inheritdoc />
    public float GetSpeedFactor(int stateId) => 1.0f;

    /// <inheritdoc />
    public float GetJumpFactor(int stateId) => 1.0f;

    /// <inheritdoc />
    public IReadOnlyList<string> GetPropertyNames(int stateId) => [];

    /// <inheritdoc />
    public bool TryGetPropertyValue(int stateId, string propertyName, out string value)
    {
        value = string.Empty;
        return false;
    }

    /// <inheritdoc />
    public bool TryDecodeLegacy(int stateId, out int blockId, out int meta)
    {
        blockId = 0;
        meta = 0;
        return false;
    }

    /// <inheritdoc />
    public int EncodeLegacy(int blockId, int meta) => (blockId << 4) | meta;

    // IPhysicsWorldView

    /// <inheritdoc />
    public BlockState GetBlock(BlockPos pos)
    {
        int stateId = _blocks.TryGetValue(pos, out int s) ? s : (int)VoxelBlock.Air;
        return new BlockState(this, stateId);
    }

    /// <summary>The <see cref="VoxelBlock"/> kind at a position (air if unset).</summary>
    public VoxelBlock GetKind(BlockPos pos) => (VoxelBlock)(_blocks.TryGetValue(pos, out int s) ? s : 0);

    /// <inheritdoc />
    public ReadOnlySpan<Aabb> GetCollisionShapes(BlockState state) => ShapesFor(state.StateId);

    /// <inheritdoc />
    public bool IsChunkLoaded(BlockPos pos) =>
        pos.X >= _loadedBounds.MinX && pos.X <= _loadedBounds.MaxX &&
        pos.Z >= _loadedBounds.MinZ && pos.Z <= _loadedBounds.MaxZ;

    private static ReadOnlySpan<Aabb> ShapesFor(int stateId) => (VoxelBlock)stateId switch
    {
        VoxelBlock.Air or VoxelBlock.Ladder or VoxelBlock.Water => Empty,
        VoxelBlock.Slab => Slab,
        _ => Full,
    };
}
