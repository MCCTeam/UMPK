using System.Runtime.CompilerServices;
using Umpk.Game.Entities;
using Umpk.Geometry;

namespace Umpk.Client.Internal;

internal sealed partial class EntityMembershipState
{
    private readonly ConditionalWeakTable<Entity, ShulkerTickEnrollment> _shulkerTickOrder = new();
    private readonly List<Entity> _shulkerTickActors = [];
    private readonly List<OrderedShulkerActor> _shulkerOrderedActors = [];
    private readonly List<int> _shulkerActorSlots = [];
    private long _nextShulkerEnrollmentOrder;
    private int _shulkerTickSources;

    private void RegisterShulkerTickOrder(Entity entity, int protocol, bool isPlayer)
    {
        if (!ShulkerPushProfile.ForProtocol(protocol).HasRules)
            return;
        bool source = entity.Type.Id == Identifier.Minecraft("shulker");
        if (!isPlayer && !source)
            return;
        var cell = BlockPos.Containing(entity.Position);
        _shulkerTickOrder.Add(entity, new(ChunkPos.Containing(cell), isPlayer, isPlayer || HasColumn(cell), ++_nextShulkerEnrollmentOrder));
        if (source)
            _shulkerTickSources++;
    }

    private void RemoveShulkerTickOrder(Entity entity)
    {
        if (_shulkerTickOrder.TryGetValue(entity, out ShulkerTickEnrollment? value) && !value.IsPlayer)
            _shulkerTickSources--;
        _shulkerTickOrder.Remove(entity);
    }

    private void ClearShulkerTickOrder()
    {
        _shulkerTickOrder.Clear();
        _shulkerTickActors.Clear();
        _shulkerOrderedActors.Clear();
        _shulkerActorSlots.Clear();
        _shulkerTickSources = 0;
        _nextShulkerEnrollmentOrder = 0;
    }

    private void InvalidateShulkerTickOrder(ChunkPos column)
    {
        foreach (var pair in _shulkerTickOrder)
            if (!pair.Value.IsPlayer && pair.Value.Column == column)
                pair.Value.Known = false;
    }

    internal void ObserveShulkerPosition(Entity entity)
    {
        if (_shulkerTickOrder.TryGetValue(entity, out ShulkerTickEnrollment? value) && !value.IsPlayer
            && value.Column != ChunkPos.Containing(BlockPos.Containing(entity.Position)))
            value.Known = false;
    }

    internal bool IsShulkerTickOrderKnown(Entity entity)
    {
        ObserveShulkerPosition(entity);
        return _shulkerTickOrder.TryGetValue(entity, out ShulkerTickEnrollment? value) && !value.IsPlayer && value.Known;
    }

    internal IEnumerable<Entity> ShulkerTickActors(IEnumerable<Entity> stored)
    {
        if (_shulkerTickSources == 0)
            return stored;
        _shulkerTickActors.Clear();
        _shulkerOrderedActors.Clear();
        _shulkerActorSlots.Clear();
        foreach (Entity entity in stored)
        {
            ObserveShulkerPosition(entity);
            if (_shulkerTickOrder.TryGetValue(entity, out ShulkerTickEnrollment? value) && value.Known)
            {
                _shulkerActorSlots.Add(_shulkerTickActors.Count);
                _shulkerOrderedActors.Add(new(entity, value.Order));
            }
            _shulkerTickActors.Add(entity);
        }
        // Native EntityTickList retains enrollment order after removal and appends replacements. Dictionary slot reuse cannot supply that order. Unclassified actor slots retain their existing traversal.
        _shulkerOrderedActors.Sort(ShulkerActorComparer.Instance);
        for (int index = 0; index < _shulkerOrderedActors.Count; index++)
            _shulkerTickActors[_shulkerActorSlots[index]] = _shulkerOrderedActors[index].Entity;
        return _shulkerTickActors;
    }

    private sealed class ShulkerTickEnrollment(ChunkPos column, bool isPlayer, bool known, long order)
    {
        internal ChunkPos Column { get; } = column;
        internal bool IsPlayer { get; } = isPlayer;
        internal bool Known { get; set; } = known;
        internal long Order { get; } = order;
    }

    private readonly record struct OrderedShulkerActor(Entity Entity, long Order);

    private sealed class ShulkerActorComparer : IComparer<OrderedShulkerActor>
    {
        internal static readonly ShulkerActorComparer Instance = new();
        public int Compare(OrderedShulkerActor first, OrderedShulkerActor second) => first.Order.CompareTo(second.Order);
    }
}
