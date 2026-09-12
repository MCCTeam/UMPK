using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Text;

namespace Umpk.Game.Entities;

/// <summary>A tracked entity: a typed model carrying identity, kinematics, the rider graph, and the auxiliary collections for equipment, effects, attributes, and metadata.</summary>
/// <remarks>State fields are mutated on the session loop by the client/navigator layer's update handlers. Rider-graph edges are maintained only through <see cref="EntityStore"/> so both directions stay consistent and cycles are rejected; the mutators here are internal for that reason.</remarks>
public sealed class Entity
{
    private readonly List<Entity> _passengers = [];
    private readonly Dictionary<EquipmentSlot, IMetadataSlot?> _equipment = [];
    private readonly Dictionary<int, EffectInstance> _effects = [];

    /// <summary>Creates an entity with the given network id, uuid, and resolved type.</summary>
    /// <param name="id">The server-assigned entity id.</param>
    /// <param name="uuid">The entity uuid.</param>
    /// <param name="type">The resolved entity-type handle.</param>
    /// <param name="keySource">The tier-2 metadata key source to bind, or null for tier-1-only metadata.</param>
    public Entity(int id, Guid uuid, RegistryEntry<EntityTypeDefinition> type, IMetadataKeySource? keySource = null)
    {
        Id = id;
        Uuid = uuid;
        Type = type;
        Metadata = new EntityMetadata(type, keySource);
        Attributes = new AttributeMap();
    }

    /// <summary>The server-assigned entity id.</summary>
    public int Id { get; }

    /// <summary>The entity uuid.</summary>
    public Guid Uuid { get; }

    /// <summary>The resolved entity type.</summary>
    public RegistryEntry<EntityTypeDefinition> Type { get; }

    /// <summary>The entity position.</summary>
    public Vec3d Position { get; set; }

    /// <summary>The entity velocity.</summary>
    public Vec3d Velocity { get; set; }

    /// <summary>Body yaw, in degrees.</summary>
    public float Yaw { get; set; }

    /// <summary>Pitch, in degrees.</summary>
    public float Pitch { get; set; }

    /// <summary>Head yaw, in degrees.</summary>
    public float HeadYaw { get; set; }

    /// <summary>Whether the entity is on the ground per the last position update.</summary>
    public bool OnGround { get; set; }

    /// <summary>The current pose, mirrored from metadata when a semantic key resolves it.</summary>
    public EntityPose Pose { get; set; } = EntityPose.Standing;

    /// <summary>The custom display name, if any.</summary>
    public Component? CustomName { get; set; }

    /// <summary>The entity holding this entity's leash, if any.</summary>
    public Entity? LeashHolder { get; set; }

    /// <summary>The player profile, for player entities only.</summary>
    public GameProfile? PlayerProfile { get; set; }

    /// <summary>The two-tier metadata store.</summary>
    public EntityMetadata Metadata { get; }

    /// <summary>The stack carried by a dropped-item entity, or null when absent/empty/not yet received.</summary>
    public IMetadataSlot? CarriedItem { get; set; }

    /// <summary>The attribute map (base values plus modifiers).</summary>
    public AttributeMap Attributes { get; }

    /// <summary>The vehicle this entity currently rides, if any.</summary>
    public Entity? Vehicle { get; internal set; }

    /// <summary>The entities riding this entity, in mount order.</summary>
    public IReadOnlyList<Entity> Passengers => _passengers;

    /// <summary>The equipment currently worn/held, by slot. The value is the item-slot placeholder (null = empty).</summary>
    public IReadOnlyDictionary<EquipmentSlot, IMetadataSlot?> Equipment => _equipment;

    /// <summary>The active status effects, keyed by effect network id.</summary>
    public IReadOnlyDictionary<int, EffectInstance> Effects => _effects;

    /// <summary>Sets (or clears) the equipment in the given slot. Pass null for an empty slot.</summary>
    public void SetEquipment(EquipmentSlot slot, IMetadataSlot? item) => _equipment[slot] = item;

    /// <summary>Gets the equipment in the given slot; false when the slot was never set.</summary>
    public bool TryGetEquipment(EquipmentSlot slot, out IMetadataSlot? item) => _equipment.TryGetValue(slot, out item);

    /// <summary>Adds a status effect, or refreshes the existing one for the same effect id (last write wins, matching vanilla's replace-on-reapply behavior). Returns true when an existing effect was replaced.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="effect"/> is null.</exception>
    public bool AddOrRefreshEffect(EffectInstance effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        int key = effect.Effect.NetworkId;
        bool replaced = _effects.ContainsKey(key);
        _effects[key] = effect;
        return replaced;
    }

    /// <summary>Removes the effect with the given effect network id; returns true when one was removed.</summary>
    public bool RemoveEffect(int effectNetworkId) => _effects.Remove(effectNetworkId);

    /// <summary>Gets the active effect for the given effect network id, if present.</summary>
    public bool TryGetEffect(int effectNetworkId, out EffectInstance effect) => _effects.TryGetValue(effectNetworkId, out effect!);

    /// <summary>Removes every active effect.</summary>
    public void ClearEffects() => _effects.Clear();

    internal void AddPassengerEdge(Entity passenger)
    {
        if (!_passengers.Contains(passenger))
            _passengers.Add(passenger);

    }

    internal void RemovePassengerEdge(Entity passenger) => _passengers.Remove(passenger);

    internal void ClearPassengerEdges() => _passengers.Clear();

    internal bool HasPassenger(Entity passenger) => _passengers.Contains(passenger);
}
