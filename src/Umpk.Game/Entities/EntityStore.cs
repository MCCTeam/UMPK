using System.Diagnostics.CodeAnalysis;
using Umpk.Geometry;

namespace Umpk.Game.Entities;

/// <summary>
/// The per-session entity table and rider-graph owner. The entity-tracking model could sit as an <c>EntityTracker</c> in <c>Umpk.Client</c>, but the storage and graph invariants are a <c>Umpk.Game</c> concern, so the state container lives here and the client package's tracker composes it. Provides id/uuid lookup, spatial queries, and the only correct way to mutate vehicle/passenger edges: both directions are kept consistent, self-mounts and cycles are rejected, and despawn detaches an entity from both sides.
///
/// <para>There is deliberately no bulk act-on-type method here (no <c>AttackAllOfType</c>): a rate-unaware serverbound fan-out is exactly what anti-cheat reads as an attack. The loop over a query's results stays in the consumer, where pacing between actions is a product decision this store cannot make for every caller.</para>
/// </summary>
public sealed class EntityStore
{
    private readonly Dictionary<int, Entity> _byId = [];
    private readonly Dictionary<Guid, Entity> _byUuid = [];

    /// <summary>All tracked entities.</summary>
    public IReadOnlyCollection<Entity> All => _byId.Values;

    /// <summary>The number of tracked entities.</summary>
    public int Count => _byId.Count;

    /// <summary>Gets the entity with the given id, or null when untracked.</summary>
    public Entity? Get(int entityId) => _byId.GetValueOrDefault(entityId);

    /// <summary>Gets the entity with the given uuid.</summary>
    public bool TryGetByUuid(Guid uuid, [NotNullWhen(true)] out Entity? entity) => _byUuid.TryGetValue(uuid, out entity);

    /// <summary>Gets the entity with the given id.</summary>
    public bool TryGet(int entityId, [NotNullWhen(true)] out Entity? entity) => _byId.TryGetValue(entityId, out entity);

    /// <summary>Adds a newly spawned entity. Returns the added entity, or the existing one if an entity with the same id is already tracked (spawn packets for an already-known id do not replace it here; the update handlers mutate the existing instance).</summary>
    /// <exception cref="ArgumentNullException"><paramref name="entity"/> is null.</exception>
    public Entity Add(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (_byId.TryGetValue(entity.Id, out Entity? existing))
            return existing;

        _byId[entity.Id] = entity;
        _byUuid[entity.Uuid] = entity;
        return entity;
    }

    /// <summary>Removes (despawns) the entity with the given id and detaches it from the rider graph on both sides: it is unmounted from any vehicle and all its own passengers are dismounted. Returns the removed entity, or null when it was not tracked.</summary>
    public Entity? Remove(int entityId)
    {
        if (!_byId.TryGetValue(entityId, out Entity? entity))
            return null;

        DetachFromVehicle(entity);
        foreach (Entity passenger in entity.Passengers.ToArray())
            passenger.Vehicle = null;

        entity.ClearPassengerEdges();

        _byId.Remove(entityId);
        _byUuid.Remove(entity.Uuid);
        return entity;
    }

    /// <summary>Removes every tracked entity and clears all rider edges.</summary>
    public void Clear()
    {
        foreach (Entity entity in _byId.Values)
        {
            entity.Vehicle = null;
            entity.ClearPassengerEdges();
        }

        _byId.Clear();
        _byUuid.Clear();
    }

    /// <summary>Enumerates tracked entities within <paramref name="range"/> of <paramref name="center"/>.</summary>
    public IEnumerable<Entity> Nearby(Vec3d center, double range)
    {
        double rangeSqr = range * range;
        foreach (Entity entity in _byId.Values)
            if (entity.Position.Subtract(center).LengthSqr() <= rangeSqr)
                yield return entity;

    }

    /// <summary>The single closest tracked entity within <paramref name="range"/> of <paramref name="center"/>, or null when none qualify. Uses the same inclusive squared-distance boundary as <see cref="Nearby"/>. When <paramref name="predicate"/> is given, it is applied BEFORE distance is considered, so a closer entity that fails the predicate never shadows a farther one that passes it. A tie is broken by the lowest <see cref="Entity.Id"/>, independent of table order.</summary>
    public Entity? Nearest(Vec3d center, double range, Func<Entity, bool>? predicate = null)
    {
        double rangeSqr = range * range;
        Entity? best = null;
        double bestSqr = 0;
        foreach (Entity entity in _byId.Values)
        {
            if (predicate is not null && !predicate(entity))
                continue;

            double sqr = entity.Position.Subtract(center).LengthSqr();
            if (sqr > rangeSqr)
                continue;

            if (best is null || sqr < bestSqr || (sqr == bestSqr && entity.Id < best.Id))
            {
                best = entity;
                bestSqr = sqr;
            }
        }

        return best;
    }

    /// <summary>Enumerates tracked entities whose type id matches <paramref name="typeId"/>, a namespaced or bare id (for example <c>minecraft:zombie</c> or <c>zombie</c>). A bare id matches only the <c>minecraft</c> namespace; see <see cref="IdentifierMatch"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="typeId"/> is null, empty, or whitespace.</exception>
    public IEnumerable<Entity> OfType(string typeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeId);
        string needle = typeId.Trim();
        return OfTypeIterator(needle, IdentifierMatch.BarePath(needle));
    }

    private IEnumerable<Entity> OfTypeIterator(string needle, string barePath)
    {
        foreach (Entity entity in _byId.Values)
            if (IdentifierMatch.Matches(entity.Type.Id, needle, barePath))
                yield return entity;

    }

    /// <summary>Enumerates tracked entities whose type id equals <paramref name="typeId"/> exactly. Unlike <see cref="OfType(string)"/> there is no bare-path ambiguity to resolve: an already-resolved <see cref="Identifier"/> either names the type or it does not.</summary>
    public IEnumerable<Entity> OfType(Identifier typeId)
    {
        foreach (Entity entity in _byId.Values)
            if (entity.Type.Id.Equals(typeId))
                yield return entity;

    }

    /// <summary>The tracked entities within <paramref name="range"/> of <paramref name="center"/>, nearest first, capped at <paramref name="maxResults"/>. Ties keep the table's own iteration order.</summary>
    public IReadOnlyList<Entity> NearestFirst(Vec3d center, double range, int maxResults = int.MaxValue)
    {
        if (maxResults <= 0)
            return [];

        double rangeSqr = range * range;
        var hits = new List<(Entity Entity, double DistanceSqr, int Order)>();
        int order = 0;
        foreach (Entity entity in _byId.Values)
        {
            double sqr = entity.Position.Subtract(center).LengthSqr();
            if (sqr <= rangeSqr)
                hits.Add((entity, sqr, order++));

        }

        hits.Sort((a, b) =>
        {
            int byDistance = a.DistanceSqr.CompareTo(b.DistanceSqr);
            return byDistance != 0 ? byDistance : a.Order.CompareTo(b.Order);
        });

        int count = Math.Min(maxResults, hits.Count);
        var results = new Entity[count];
        for (int i = 0; i < count; i++)
            results[i] = hits[i].Entity;

        return results;
    }

    /// <summary>Replaces <paramref name="vehicle"/>'s passenger list with <paramref name="passengers"/> (the <c>SetPassengers</c> packet). Both directions are updated: former passengers no longer referenced are unmounted, new ones get their <see cref="Entity.Vehicle"/> set, and any prior vehicle of a new passenger is cleared. A passenger that would create a mount cycle (mounting itself, or mounting one of its own transitive passengers) is rejected.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="vehicle"/> or <paramref name="passengers"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A requested edge would form a rider cycle.</exception>
    public void SetPassengers(Entity vehicle, IReadOnlyList<Entity> passengers)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(passengers);

        foreach (Entity passenger in passengers)
        {
            ArgumentNullException.ThrowIfNull(passenger);
            if (ReferenceEquals(passenger, vehicle) || IsTransitivePassengerOf(vehicle, passenger))
                throw new InvalidOperationException(
                    $"Mounting entity {passenger.Id} on {vehicle.Id} would create a rider cycle.");

        }

        // Detach passengers that are no longer present.
        foreach (Entity former in vehicle.Passengers.ToArray())
            if (!passengers.Contains(former))
            {
                former.Vehicle = null;
                vehicle.RemovePassengerEdge(former);
            }

        // Attach new passengers, moving each off any prior vehicle first.
        foreach (Entity passenger in passengers)
        {
            if (passenger.Vehicle is { } prior && !ReferenceEquals(prior, vehicle))
                prior.RemovePassengerEdge(passenger);

            passenger.Vehicle = vehicle;
            vehicle.AddPassengerEdge(passenger);
        }
    }

    /// <summary>Mounts <paramref name="passenger"/> onto <paramref name="vehicle"/> as a single edge, keeping both directions consistent, or dismounts it when <paramref name="vehicle"/> is null. Rejects self-mounts and cycles.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="passenger"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The edge would form a rider cycle.</exception>
    public void SetVehicle(Entity passenger, Entity? vehicle)
    {
        ArgumentNullException.ThrowIfNull(passenger);

        if (vehicle is null)
        {
            DetachFromVehicle(passenger);
            return;
        }

        if (ReferenceEquals(passenger, vehicle) || IsTransitivePassengerOf(vehicle, passenger))
            throw new InvalidOperationException(
                $"Mounting entity {passenger.Id} on {vehicle.Id} would create a rider cycle.");

        DetachFromVehicle(passenger);
        passenger.Vehicle = vehicle;
        vehicle.AddPassengerEdge(passenger);
    }

    private static void DetachFromVehicle(Entity passenger)
    {
        if (passenger.Vehicle is { } vehicle)
        {
            vehicle.RemovePassengerEdge(passenger);
            passenger.Vehicle = null;
        }
    }

    /// <summary>True when <paramref name="candidate"/> is <paramref name="root"/> or rides it transitively.</summary>
    private static bool IsTransitivePassengerOf(Entity candidate, Entity root)
    {
        foreach (Entity direct in root.Passengers)
            if (ReferenceEquals(direct, candidate) || IsTransitivePassengerOf(candidate, direct))
                return true;

        return false;
    }
}
