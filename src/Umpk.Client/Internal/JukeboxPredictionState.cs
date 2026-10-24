using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;

namespace Umpk.Client.Internal;

/// <summary>The interaction owner's local jukebox clearing, separate from the server-observed world.</summary>
internal sealed class JukeboxPredictionState
{
    private World? _world;
    private readonly List<BlockPos> _positions = [];
    private readonly List<int> _states = [];

    internal bool TryProject(World world, BlockPos position, BlockState observed, out BlockState projected)
    {
        projected = observed;
        if (!ReferenceEquals(_world, world))
            return false;
        int index = _positions.IndexOf(position);
        if (index < 0 || _states[index] != observed.StateId || !TryEmptyState(observed, out int empty))
            return false;
        projected = new BlockState(observed.Source, empty);
        return true;
    }

    internal void Commit(World world, BlockPos position)
    {
        Synchronize(world);
        BlockState observed = world.GetBlock(position);
        if (!TryEmptyState(observed, out _))
            return;
        int index = _positions.IndexOf(position);
        if (index >= 0)
        {
            _states[index] = observed.StateId;
            return;
        }
        int count = _positions.Count + 1;
        _positions.EnsureCapacity(count);
        _states.EnsureCapacity(count);
        _positions.Add(position);
        _states.Add(observed.StateId);
    }

    internal void ObserveBlock(BlockPos position)
    {
        int index = _positions.IndexOf(position);
        if (index >= 0)
            RemoveAt(index);
    }

    internal void ObserveColumn(ChunkColumn column, bool full)
    {
        for (int index = _positions.Count - 1; index >= 0; index--)
        {
            BlockPos position = _positions[index];
            if (ChunkPos.Containing(position) == column.Position && (full || column.GetSectionForY(position.Y) is not null))
                RemoveAt(index);
        }
    }

    internal void ObserveUnload(ChunkPos chunk)
    {
        for (int index = _positions.Count - 1; index >= 0; index--)
            if (ChunkPos.Containing(_positions[index]) == chunk)
                RemoveAt(index);
    }

    internal void Clear()
    {
        _world = null;
        _positions.Clear();
        _states.Clear();
    }

    private void Synchronize(World world)
    {
        if (ReferenceEquals(_world, world))
            return;
        Clear();
        _world = world;
    }

    private void RemoveAt(int index)
    {
        _positions.RemoveAt(index);
        _states.RemoveAt(index);
    }

    private static bool TryEmptyState(BlockState observed, out int state)
    {
        state = 0;
        if (!observed.IsValid || !observed.Block.Id.IsMinecraft || observed.Block.Id.Path != "jukebox")
            return false;
        if (observed.LegacyMeta > 0)
        {
            state = observed.StateId & ~15;
            return true;
        }
        if (!observed.TryGetProperty("has_record", out string record) || record != "true")
            return false;
        BlockDefinition definition = observed.Block.Value;
        for (int candidate = definition.MinStateId; candidate < definition.MinStateId + definition.StateCount; candidate++)
        {
            if (definition.TryGetPropertyValue(candidate, "has_record", out string hasRecord) && hasRecord == "false")
            {
                state = candidate;
                return true;
            }
        }
        return false;
    }
}
