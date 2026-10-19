using Umpk.Client.Actions;
using Umpk.Game.Entities;
using Umpk.Geometry;

namespace Umpk.Client.Internal;

/// <summary>Session-owned packet chunk availability and native legacy-player list/chunk membership.</summary>
internal sealed class EntityMembershipState
{
    private readonly HashSet<ChunkPos> _columns = [];
    private readonly Dictionary<Entity, Membership> _players = [];
    private readonly List<Membership> _reentry = [];
    private int _reentryCapacity = 16;

    public bool HasColumn(BlockPos cell) => _columns.Contains(ChunkPos.Containing(cell));

    public void Register(Entity entity, int protocol, bool isPlayer, int? healthIndex = null)
    {
        if (!isPlayer || !LegacyPlayerGeometryProfile.Supports(protocol))
            return;
        var membership = new Membership(entity, protocol == 47, healthIndex);
        _players.Add(entity, membership);
        TryAddToColumn(membership);
    }

    public void LoadColumn(ChunkPos column)
    {
        // A replacing packet installs a new native chunk. Existing added-to-chunk flags survive, but the new chunk has no old entity list.
        foreach (Membership player in _players.Values)
            if (player.Column == column)
                player.Column = null;
        _columns.Add(column);
    }

    /// <summary>Observes the native fill-or-create policy without discarding an existing chunk's entity list.</summary>
    public void EnsureColumn(ChunkPos column) => _columns.Add(column);

    public void UnloadColumn(ChunkPos column)
    {
        _columns.Remove(column);
        foreach (Membership player in _players.Values)
            if (player.Column == column)
            {
                player.Column = null;
                player.PendingUnload = true;
            }
    }

    public void BeginEntityPhase()
    {
        foreach (Membership player in _players.Values)
            if (player.PendingUnload)
            {
                player.PendingUnload = false;
                player.InEntityList = false;
                if (!TryGetHealth(player, out float health))
                {
                    player.MembershipKnown = false;
                    continue;
                }
                // Original WorldClient.onEntityRemoved queues a tracked player only while its native alive predicate is true. ID lookup survives the other path.
                if (player.Removed || !(health > 0))
                    continue;
                _reentry.Add(player);
                if (_reentry.Count > _reentryCapacity * 3 / 4)
                    _reentryCapacity *= 2;
            }
    }

    public bool CanTick(Entity entity) => !_players.TryGetValue(entity, out Membership? player) || player.MembershipKnown && player.InEntityList && player.Added && !player.Removed;

    /// <summary>Runs the inherited living death phase on the existing eligible actor tick, before remote interpolation and the player size phase.</summary>
    public void BeginPlayerTick(Entity entity)
    {
        if (!_players.TryGetValue(entity, out Membership? player))
            return;
        if (!TryGetHealth(player, out float health))
            player.LifetimeKnown = false;
        else if (health <= 0 && ++player.DeathTime == 20)
            player.Removed = true;
    }

    /// <summary>Supplies the measured legacy world-membership predicate separately from spatial bounds and the native blocks-building flag.</summary>
    public bool? IsWorldQueryMember(Entity entity)
    {
        if (!_players.TryGetValue(entity, out Membership? player))
            return true;
        if (player.Removed)
            return false;
        if (!player.MembershipKnown || !player.LifetimeKnown || !TryGetHealth(player, out _))
            return null;
        return player.Column is not null;
    }

    private static bool TryGetHealth(Membership player, out float health)
    {
        // Original constructors provide health 20 until a metadata frame supplies its value. A malformed stored kind cannot prove a lifetime or reentry decision.
        health = 20;
        if (player.HealthIndex is not { } index)
            return false;
        if (!player.Entity.Metadata.TryGet(index, out MetadataValue value))
            return true;
        if (value.Kind != MetadataValueKind.Float)
            return false;
        health = value.AsFloat();
        return true;
    }

    public void AfterEntityPhase(Entity entity)
    {
        if (!_players.TryGetValue(entity, out Membership? player) || !player.InEntityList)
            return;
        if (player.Removed)
        {
            player.InEntityList = false;
            player.Column = null;
            _reentry.Remove(player);
            return;
        }
        (int x, int y, int z) = Chunk(entity.Position);
        if (player.Added && player.X == x && player.Y == y && player.Z == z)
            return;
        player.Column = null;
        // 1.8.9 World sets added before EmptyChunk.addEntity; later clients leave it to the chunk. A forced player retains an earlier true value when the later empty-chunk method does nothing.
        player.Added |= player.SetsAddedForEmptyChunk;
        TryAddToColumn(player);
    }

    public void FinishWorldPhase()
    {
        // Original WorldClient reentry processes at most ten entries in Java HashSet bucket order. Entity.hashCode is its network id; equal-bucket insertion order survives ordinary table growth.
        for (int count = 0; count < 10 && _reentry.Count != 0; count++)
        {
            int selected = 0, bucket = Bucket(_reentry[0].Entity.Id);
            for (int i = 1; i < _reentry.Count; i++)
            {
                int candidate = Bucket(_reentry[i].Entity.Id);
                if (candidate < bucket)
                {
                    bucket = candidate;
                    selected = i;
                }
            }
            Membership player = _reentry[selected];
            _reentry.RemoveAt(selected);
            player.InEntityList = true;
            TryAddToColumn(player);
        }
    }

    private int Bucket(int id) => (id ^ (int)((uint)id >> 16)) & (_reentryCapacity - 1);

    private void TryAddToColumn(Membership player)
    {
        (int x, int y, int z) = Chunk(player.Entity.Position);
        var column = new ChunkPos(x, z);
        if (!_columns.Contains(column))
            return;
        player.Added = true;
        player.X = x;
        player.Y = Math.Clamp(y, 0, 15);
        player.Z = z;
        player.Column = column;
    }

    private static (int X, int Y, int Z) Chunk(Vec3d position)
        => ((int)Math.Clamp(Math.Floor(position.X / 16), int.MinValue, int.MaxValue),
            (int)Math.Clamp(Math.Floor(position.Y / 16), int.MinValue, int.MaxValue),
            (int)Math.Clamp(Math.Floor(position.Z / 16), int.MinValue, int.MaxValue));

    public void Remove(Entity entity)
    {
        if (_players.Remove(entity, out Membership? player))
            _reentry.Remove(player);
    }

    public void Clear()
    {
        _players.Clear();
        _columns.Clear();
        _reentry.Clear();
        _reentryCapacity = 16;
    }

    private sealed class Membership(Entity entity, bool setsAddedForEmptyChunk, int? healthIndex)
    {
        public Entity Entity { get; } = entity;
        public int? HealthIndex { get; } = healthIndex;
        public bool SetsAddedForEmptyChunk { get; } = setsAddedForEmptyChunk;
        public bool InEntityList { get; set; } = true;
        public bool Added { get; set; }
        public bool PendingUnload { get; set; }
        public int DeathTime { get; set; }
        public bool Removed { get; set; }
        public bool LifetimeKnown { get; set; } = true;
        public bool MembershipKnown { get; set; } = true;
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public ChunkPos? Column { get; set; }
    }
}
