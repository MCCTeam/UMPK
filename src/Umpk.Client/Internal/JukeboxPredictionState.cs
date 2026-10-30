using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;

namespace Umpk.Client.Internal;

/// <summary>Owns received-state shadows and native local block mutations without changing the public received world.</summary>
internal sealed class JukeboxPredictionState(EntityMembershipState? membership = null)
{
    private World? _world;
    private PredictedBlock? _blocks;
    private PredictedBlock?[] _pending = [];
    private int _pendingCount;

    private sealed class PredictedBlock(BlockPos position)
    {
        internal readonly BlockPos Position = position;
        internal PredictedBlock? Next;
        internal int? Current;
        internal int Shadow;
        internal int Observed;
        internal int? Sequence;
        internal Vec3d PlayerPosition;
        internal bool Sequenced;
    }

    internal BlockState GetBlock(World world, BlockPos position)
    {
        if (membership is not null && !membership.HasColumn(position))
            return UnavailableBlock(world);
        BlockState observed = world.GetBlock(position);
        return TryProject(world, position, observed, out BlockState current) ? current : observed;
    }

    internal bool TryProject(World world, BlockPos position, BlockState observed, out BlockState projected)
    {
        projected = observed;
        if (!ReferenceEquals(_world, world) || world.GetColumn(position) is null || membership is not null && !membership.HasColumn(position))
            return false;
        PredictedBlock? block = Find(position);
        if (block?.Current is not int current || !block.Sequenced && block.Observed != observed.StateId)
            return false;
        projected = new BlockState(world.BlockData, current);
        return true;
    }

    internal void Commit(World world, BlockPos position)
    {
        BlockState current = GetBlock(world, position);
        if (TryEmptyState(current, out int empty))
            Commit(world, position, empty, sequence: null, default);
    }

    internal void Commit(World world, BlockPos position, int state, int? sequence, Vec3d playerPosition)
    {
        Synchronize(world);
        if (world.GetColumn(position) is null || membership is not null && !membership.HasColumn(position))
            return;
        BlockState current = GetBlock(world, position);
        if (!current.IsValid || current.StateId == state)
            return;
        PredictedBlock? block = Find(position);
        if (block is null)
        {
            block = new PredictedBlock(position) { Next = _blocks };
            _blocks = block;
        }
        if (block.Sequence is null)
        {
            block.Shadow = current.StateId;
            block.PlayerPosition = playerPosition;
            if (sequence is not null)
                AddPending(block);
        }
        block.Current = state;
        block.Observed = world.GetBlock(position).StateId;
        block.Sequence = sequence;
        block.Sequenced = sequence is not null;
    }

    internal void ObserveBlock(BlockPos position) => ObserveBlock(position, null);

    internal void ObserveBlock(BlockPos position, int? state)
    {
        PredictedBlock? block = Find(position);
        if (block is null)
            return;
        if (block.Sequence is not null && state is int shadow)
        {
            // A full chunk can replace current state without ending the pending mutation. Capture that current state before a later verified update changes only the shadow.
            if (block.Current is null && _world is { } world && world.GetColumn(position) is not null
                && (membership is null || membership.HasColumn(position)))
                block.Current = world.GetBlock(position).StateId;
            block.Shadow = shadow;
        }
        else
            Remove(block);
    }

    internal void ObserveColumn(ChunkColumn column, bool full)
    {
        for (PredictedBlock? block = _blocks; block is not null;)
        {
            PredictedBlock? next = block.Next;
            if (ChunkPos.Containing(block.Position) == column.Position && (full || column.GetSectionForY(block.Position.Y) is not null))
            {
                if (block.Sequence is not null)
                    block.Current = null;
                else
                    Remove(block);
            }
            block = next;
        }
    }

    internal void ObserveUnload(ChunkPos chunk)
    {
        for (PredictedBlock? block = _blocks; block is not null;)
        {
            PredictedBlock? next = block.Next;
            if (ChunkPos.Containing(block.Position) == chunk)
            {
                if (block.Sequence is not null)
                    block.Current = null;
                else
                    Remove(block);
            }
            block = next;
        }
    }

    internal void Acknowledge(World world, int sequence, Action<BlockPos, BlockState, Vec3d> restorePosition, bool restoreCapturedPosition = true)
    {
        if (!ReferenceEquals(_world, world) || _pendingCount == 0)
            return;
        // Original Long2ObjectOpenHashMap iterator: zero key first, descending slots, then keys shifted across the wrap during iterator removal.
        int cursor = _pending.Length - 1;
        int remaining = _pendingCount;
        bool zero = _pending[cursor] is not null;
        List<BlockPos>? wrapped = null;
        while (remaining-- != 0)
        {
            int slot;
            if (zero)
            {
                zero = false;
                slot = _pending.Length - 1;
            }
            else
            {
                while (--cursor >= 0 && _pending[cursor] is null) { }
                slot = cursor >= 0 ? cursor : FindPending(wrapped![-cursor - 1]);
            }
            PredictedBlock block = _pending[slot]!;
            if (block.Sequence is not int mutation || mutation > sequence)
                continue;
            if (slot == _pending.Length - 1)
                _pending[slot] = null;
            else if (cursor < 0)
            {
                ShiftPending(slot, ref wrapped, recordWrapped: false);
                _pendingCount--;
                int capacity = _pending.Length - 1;
                if (capacity > 32 && _pendingCount < (capacity * 3 / 4) / 4)
                    RehashPending(capacity / 2);
                Restore(block);
                continue;
            }
            else
            {
                ShiftPending(slot, ref wrapped, recordWrapped: true);
            }
            _pendingCount--;
            Restore(block);
        }

        void Restore(PredictedBlock block)
        {
            block.Sequence = null;
            if (world.GetColumn(block.Position) is null || membership is not null && !membership.HasColumn(block.Position))
            {
                BlockState absent = UnavailableBlock(world);
                Remove(block);
                // EmptyLevelChunk refuses the state write, but syncBlockState still tests the supplied shadow against the player's body.
                if (absent.StateId != block.Shadow && restoreCapturedPosition)
                    restorePosition(block.Position, new BlockState(world.BlockData, block.Shadow), block.PlayerPosition);
                return;
            }
            BlockState current = GetBlock(world, block.Position);
            block.Current = block.Shadow;
            if (current.StateId != block.Shadow && restoreCapturedPosition)
                restorePosition(block.Position, new BlockState(world.BlockData, block.Shadow), block.PlayerPosition);
            if (world.GetBlock(block.Position).StateId == block.Shadow)
                Remove(block);
        }
    }

    private static ulong Packed(BlockPos position)
        => ((ulong)(position.X & 0x3FFFFFF) << 38) | ((ulong)(position.Z & 0x3FFFFFF) << 12) | (uint)(position.Y & 0xFFF);

    private static int HashSlot(BlockPos position, int mask)
    {
        ulong mixed = unchecked(Packed(position) * 11400714819323198485UL);
        mixed ^= mixed >> 32;
        return (int)(mixed ^ (mixed >> 16)) & mask;
    }

    private int FindPending(BlockPos position)
    {
        int capacity = _pending.Length - 1;
        if (Packed(position) == 0)
            return capacity;
        int slot = HashSlot(position, capacity - 1);
        while (_pending[slot] is { } existing && Packed(existing.Position) != Packed(position))
            slot = (slot + 1) & (capacity - 1);
        return slot;
    }

    private void AddPending(PredictedBlock block)
    {
        if (_pending.Length == 0)
            _pending = new PredictedBlock?[33];
        int slot = FindPending(block.Position);
        _pending[slot] = block;
        int capacity = _pending.Length - 1;
        if (++_pendingCount > capacity * 3 / 4)
            RehashPending(capacity * 2);
    }

    private void RehashPending(int capacity)
    {
        PredictedBlock?[] old = _pending;
        _pending = new PredictedBlock?[capacity + 1];
        for (int index = old.Length - 2; index >= 0; index--)
            if (old[index] is { } block)
                _pending[FindPending(block.Position)] = block;
        _pending[capacity] = old[^1];
    }

    private void ShiftPending(int position, ref List<BlockPos>? wrapped, bool recordWrapped)
    {
        int mask = _pending.Length - 2;
        while (true)
        {
            int last = position;
            position = (position + 1) & mask;
            while (_pending[position] is { } next)
            {
                int home = HashSlot(next.Position, mask);
                if (last <= position ? last >= home || home > position : last >= home && home > position)
                    break;
                position = (position + 1) & mask;
            }
            if (_pending[position] is not { } block)
            {
                _pending[last] = null;
                return;
            }
            if (recordWrapped && position < last)
                (wrapped ??= []).Add(block.Position);
            _pending[last] = block;
        }
    }

    internal void Clear()
    {
        _world = null;
        _blocks = null;
        _pending = [];
        _pendingCount = 0;
    }

    private static BlockState UnavailableBlock(World world)
        => world.BlockData.Blocks.TryGet(Identifier.Minecraft("void_air"), out RegistryEntry<BlockDefinition> empty)
            ? new BlockState(world.BlockData, empty.Value.DefaultStateId)
            : new BlockState(world.BlockData, world.BlockData.UnknownStateId);

    private PredictedBlock? Find(BlockPos position)
    {
        for (PredictedBlock? block = _blocks; block is not null; block = block.Next)
            if (block.Position == position)
                return block;
        return null;
    }

    private void Remove(PredictedBlock removed)
    {
        PredictedBlock? previous = null;
        for (PredictedBlock? block = _blocks; block is not null; block = block.Next)
        {
            if (ReferenceEquals(block, removed))
            {
                if (previous is null)
                    _blocks = block.Next;
                else
                    previous.Next = block.Next;
                block.Next = null;
                return;
            }
            previous = block;
        }
    }

    private void Synchronize(World world)
    {
        if (ReferenceEquals(_world, world))
            return;
        Clear();
        _world = world;
    }

    internal static bool TryEmptyState(BlockState observed, out int state)
    {
        state = 0;
        if (!observed.IsValid || !observed.Block.Id.IsMinecraft || observed.Block.Id.Path != "jukebox")
            return false;
        if (observed.LegacyMeta > 0)
        {
            state = observed.StateId & ~15;
            return true;
        }
        return observed.TryGetProperty("has_record", out string record) && record == "true"
            && TryPropertyState(observed, "has_record", "false", out state);
    }

    internal static bool TryPropertyState(BlockState observed, string property, string value, out int state)
    {
        state = 0;
        if (!observed.IsValid)
            return false;
        BlockDefinition definition = observed.Block.Value;
        for (int candidate = definition.MinStateId; candidate < definition.MinStateId + definition.StateCount; candidate++)
        {
            if (!definition.TryGetPropertyValue(candidate, property, out string target) || target != value)
                continue;
            bool matches = true;
            foreach (string existing in observed.PropertyNames)
            {
                if (existing == property)
                    continue;
                if (!definition.TryGetPropertyValue(candidate, existing, out string next)
                    || !observed.TryGetProperty(existing, out string previous) || next != previous)
                {
                    matches = false;
                    break;
                }
            }
            if (matches)
            {
                state = candidate;
                return true;
            }
        }
        return false;
    }
}
