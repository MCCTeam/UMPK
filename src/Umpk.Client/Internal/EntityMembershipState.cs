using System.Runtime.CompilerServices;
using Umpk.Client.Actions;
using Umpk.Game.Entities;
using Umpk.Geometry;

namespace Umpk.Client.Internal;

/// <summary>Session-owned packet chunk availability and measured legacy actor list and chunk membership.</summary>
internal sealed class EntityMembershipState
{
    private readonly HashSet<ChunkPos> _columns = [];
    private readonly ConditionalWeakTable<Entity, object> _cows = new();
    private static readonly object CowMarker = new();
    private readonly Dictionary<Entity, Membership> _players = [];
    private readonly List<Membership> _reentry = [];
    private int _reentryCapacity = 16;

    public bool HasColumn(BlockPos cell) => _columns.Contains(ChunkPos.Containing(cell));

    public void Register(Entity entity, int protocol, bool isPlayer, int? healthIndex = null)
    {
        if (CowMotionProfile.Supports(entity, protocol))
        {
            _cows.Add(entity, CowMarker);
            return;
        }
        bool falling = LegacyFallingBlockProfile.Supports(entity, protocol);
        bool ambient = LegacyAmbientMobProfile.TryGet(entity, protocol, out LegacyAmbientMobProfile ambientProfile);
        if ((!isPlayer || !LegacyPlayerGeometryProfile.Supports(protocol)) && !falling && !ambient)
            return;
        var membership = new Membership(entity, protocol == 47, healthIndex, isPlayer, ambient ? ambientProfile : null)
        {
            InEntityList = protocol == 47 || isPlayer || HasColumn(new BlockPos((int)Math.Floor(entity.Position.X), 64, (int)Math.Floor(entity.Position.Z))),
        };
        _players.Add(entity, membership);
        TryAddToColumn(membership);
        if (!membership.InEntityList)
            QueueReentry(membership);
    }

    public void LoadColumn(ChunkPos column)
    {
        // A replacing packet installs a new native chunk. Existing added-to-chunk flags survive, but the new chunk has no old entity list.
        foreach (Membership player in _players.Values)
            if (player.Column == column && (player.AmbientProfile is null || player.MembershipKnown))
                player.Column = null;
        _columns.Add(column);
    }

    /// <summary>Observes the native fill-or-create policy without discarding an existing chunk's entity list.</summary>
    public void EnsureColumn(ChunkPos column) => _columns.Add(column);

    public void UnloadColumn(ChunkPos column)
    {
        _columns.Remove(column);
        foreach (Membership player in _players.Values)
            if (player.Column == column && (player.AmbientProfile is null || player.MembershipKnown))
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
                QueueReentry(player);
            }
    }

    public bool CanTick(Entity entity, bool runsNormally = true)
    {
        if (_cows.TryGetValue(entity, out _))
            return runsNormally && double.IsFinite(entity.Position.X) && double.IsFinite(entity.Position.Z)
                && entity.Position.X is > int.MinValue and < int.MaxValue && entity.Position.Z is > int.MinValue and < int.MaxValue
                && HasColumn(BlockPos.Containing(entity.Position));
        return !_players.TryGetValue(entity, out Membership? player) || player.MembershipKnown && player.InEntityList && player.Added && !player.Removed;
    }

    /// <summary>Runs the inherited living death phase on the existing eligible actor tick, before remote interpolation and geometry phases.</summary>
    public void BeginPlayerTick(Entity entity)
    {
        if (!_players.TryGetValue(entity, out Membership? player) || !player.IsLiving)
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
        if (!player.IsLiving)
            return player.MembershipKnown ? player.Column is not null : null;
        if (!player.MembershipKnown || !player.LifetimeKnown || !TryGetHealth(player, out _))
            return null;
        return player.Column is not null;
    }

    /// <summary>Retains the last proven ambient membership snapshot without using it as current post-travel registration evidence.</summary>
    public void InvalidateAmbientRegistration(Entity entity)
    {
        if (_players.TryGetValue(entity, out Membership? member) && member.AmbientProfile is not null)
            member.MembershipKnown = false;
    }

    private static bool TryGetHealth(Membership player, out float health)
    {
        // Original constructors supply family health (bat 6, squid 10, player 20) until metadata supplies its value. A malformed kind cannot prove lifetime or reentry.
        health = player.AmbientProfile?.DefaultHealth ?? 20;
        if (!player.IsLiving)
            return true;
        if (player.HealthIndex is not { } index)
            return false;
        if (!player.Entity.Metadata.TryGet(index, out MetadataValue value))
            return true;
        if (value.Kind != MetadataValueKind.Float)
            return false;
        health = value.AsFloat();
        return true;
    }

    internal bool TryGetLegacyMembership(Entity entity, out LegacyMembershipSnapshot snapshot)
    {
        snapshot = default;
        if (!_players.TryGetValue(entity, out Membership? member))
            return false;
        bool healthKnown = TryGetHealth(member, out float health);
        snapshot = new(member.InEntityList, member.Added, member.Removed, member.X, member.Y, member.Z,
            member.Column is not null ? member.Y : null, healthKnown, health, member.DeathTime, member.MembershipKnown);
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
        if (player.AmbientProfile is not null && !player.MembershipKnown)
            return;
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
            if (player.AmbientProfile is not null && !player.MembershipKnown)
                continue;
            (int x, _, int z) = Chunk(player.Entity.Position);
            if (player.SetsAddedForEmptyChunk || player.IsPlayer || _columns.Contains(new ChunkPos(x, z)))
            {
                player.InEntityList = true;
                TryAddToColumn(player);
            }
            else
                QueueReentry(player);
        }
    }

    private void QueueReentry(Membership member)
    {
        if (_reentry.Contains(member))
            return;
        _reentry.Add(member);
        if (_reentry.Count > _reentryCapacity * 3 / 4)
            _reentryCapacity *= 2;
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
        _cows.Remove(entity);
        if (_players.Remove(entity, out Membership? player))
            _reentry.Remove(player);
    }

    public void Clear()
    {
        _players.Clear();
        _cows.Clear();
        _columns.Clear();
        _reentry.Clear();
        _reentryCapacity = 16;
    }

    private sealed class Membership(Entity entity, bool setsAddedForEmptyChunk, int? healthIndex, bool isPlayer, LegacyAmbientMobProfile? ambientProfile = null)
    {
        public Entity Entity { get; } = entity;
        public int? HealthIndex { get; } = healthIndex;
        public bool SetsAddedForEmptyChunk { get; } = setsAddedForEmptyChunk;
        public bool IsPlayer { get; } = isPlayer;
        public LegacyAmbientMobProfile? AmbientProfile { get; } = ambientProfile;
        public bool IsLiving => IsPlayer || AmbientProfile is not null;
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

    /// <summary>Membership fields are the last proven snapshot when post-travel registration is unknown.</summary>
    internal readonly record struct LegacyMembershipSnapshot(bool InEntityList, bool Added, bool Removed, int X, int Y, int Z, int? ActualSection, bool HealthKnown, float Health, int DeathTime, bool RegistrationKnown);
}
