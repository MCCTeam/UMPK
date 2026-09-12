using Umpk.Game.Entities;
using Umpk.Game.Registries;

namespace Umpk.Client.State;

/// <summary>The local player's own attribute map, plus the two things an <see cref="AttributeMap"/> alone cannot say: whether this session can observe attributes at all, and which attributes the server has ever actually named.</summary>
/// <remarks>
/// <para>This exists on <see cref="SelfState"/> for the same reason <see cref="SelfState.ActiveEffects"/> does: the local player is not a member of the shared <c>EntityStore</c>, so there is no tracked entity to hold its attribute map. Self attributes are therefore stored separately.</para>
/// <para><b>Keys are CANONICAL.</b> Every entry is filed under <see cref="AttributeIds.Canonical(Identifier)"/> of the resolved registry id, so <c>minecraft:movement_speed</c> finds the same instance on protocol 766 (where the registry names it <c>minecraft:generic.movement_speed</c>) and on 774 (where it does not). Collapsing the two is safe here and only here: a session carries exactly one era, whereas the shared dataset carries all of them and <c>horse.jump_strength</c>/<c>generic.jump_strength</c> would collide there with different ranges.</para>
/// <para><b>Seeded, never empty-equals-zero.</b> The server does NOT send a player its own attributes on a quiet join: the full syncable set is sent only to other trackers, and a player does not track itself, so the owning player receives only dirty attribute updates. The client runs off its own <c>player-attribute defaults</c> supplier until the first dirty broadcast arrives, and so does this: see <see cref="SeedPlayerDefaults"/>. An empty map must therefore never be read as "movement speed is zero".</para>
/// <para><b>Polarity, following <c>PathfinderCapabilities</c>' doctrine.</b> <see cref="Known"/> false plus an empty map means UNKNOWN; <see cref="Known"/> true plus no <see cref="ServerStated"/> entry for an attribute means "we are running on vanilla's seed, and the server has never said otherwise". Those are different claims and a planner must be able to tell them apart.</para>
/// </remarks>
public sealed class SelfAttributes
{
    /// <summary>The attributes vanilla's <c>player-attribute defaults</c> supplier declares, in canonical form, with the base value vanilla states for each. A null value means "use the registry default", which is the default supplied when registering a bare attribute.</summary>
    /// <remarks>
    /// The six explicit values are the player's overrides; everything else takes the registry's <c>RangedAttribute</c> default.
    /// <para><c>movement_speed 0.1</c> is essential. Its registry default is the mob value 0.7, which would make the bot walk seven times too fast before the first attribute packet. The 0.1 value matches <c>PhysicsEngineHolder.ReadMovementSpeed</c>'s quiet-session fallback.</para>
    /// <para>An entry the session's registry does not carry is skipped, not invented: 766 has no <c>sneaking_speed</c> and 735 has no <c>gravity</c>, and a consumer's own fallback is the right answer there rather than a value vanilla never had on that version.</para>
    /// </remarks>
    private static readonly (string Name, double? Base)[] PlayerSeeds =
    [
        // Base living-entity attributes.
        ("max_health", null),
        ("knockback_resistance", null),
        ("movement_speed", 0.1),
        ("armor", null),
        ("armor_toughness", null),
        ("max_absorption", null),
        ("step_height", null),
        ("scale", null),
        ("gravity", null),
        ("safe_fall_distance", null),
        ("fall_damage_multiplier", null),
        ("jump_strength", null),
        ("oxygen_bonus", null),
        ("burning_time", null),
        ("explosion_knockback_resistance", null),
        ("water_movement_efficiency", null),
        ("movement_efficiency", null),
        ("attack_knockback", null),
        ("camera_distance", null),
        // player-attribute defaults
        ("attack_damage", 1.0),
        ("attack_speed", null),
        ("luck", null),
        ("block_interaction_range", 4.5),
        ("entity_interaction_range", 3.0),
        ("block_break_speed", null),
        ("submerged_mining_speed", null),
        ("sneaking_speed", null),
        ("mining_efficiency", null),
        ("sweeping_damage_ratio", null),
        ("waypoint_transmit_range", 6.0E7),
        ("waypoint_receive_range", 6.0E7),
    ];

    private readonly AttributeMap _map = new();
    private readonly HashSet<Identifier> _serverStated = [];

    /// <summary>The resolved attribute instances, keyed canonically. Use <see cref="Value"/> or <see cref="ValueExcluding"/> rather than reading this directly unless you need a modifier list.</summary>
    public AttributeMap Map => _map;

    /// <summary>Whether this session can observe attributes. False when entity tracking is disabled or when the protocol carries no <c>minecraft:attribute</c> registry. Physics still uses seeded defaults, but a planner must not treat a seeded value as a server observation.</summary>
    public bool Known { get; internal set; }

    /// <summary>The canonical ids an <c>update_attributes</c> frame has named at least once since the last reset. This is what separates "the server told us 0.1" from "we seeded 0.1".</summary>
    public IReadOnlyCollection<Identifier> ServerStated => _serverStated;

    /// <summary>True when the server has named this attribute since the last reset.</summary>
    public bool IsServerStated(Identifier canonicalId) => _serverStated.Contains(canonicalId);

    /// <summary>The resolved value of an attribute, or <paramref name="fallback"/> when the map holds none.</summary>
    public double Value(Identifier canonicalId, double fallback) =>
        _map.GetValueOrDefault(canonicalId, fallback);

    /// <summary>The resolved value with one modifier left out, or <paramref name="fallback"/> when the map holds no instance. The modifier to exclude is the one the CALLER applies for itself; on <c>movement_speed</c> that is <c>minecraft:sprinting</c> and nothing else. See <see cref="AttributeInstance.ValueExcluding"/>.</summary>
    public double ValueExcluding(Identifier canonicalId, Identifier excludedModifierId, double fallback) =>
        _map.TryGet(canonicalId, out AttributeInstance instance)
            ? instance.ValueExcluding(excludedModifierId)
            : fallback;

    /// <summary>Gets or creates the instance for a resolved attribute, under its canonical key.</summary>
    internal AttributeInstance GetOrCreate(Identifier canonicalId, RegistryEntry<AttributeDefinition> attribute) =>
        _map.GetOrCreate(canonicalId, attribute);

    /// <summary>Records that an <c>update_attributes</c> frame named this attribute.</summary>
    internal void MarkServerStated(Identifier canonicalId) => _serverStated.Add(canonicalId);

    /// <summary>Drops everything and re-seeds from vanilla's player supplier. Called on login and on EVERY respawn; see <c>ConnectionApplier</c> for why the respawn arm is unconditional.</summary>
    internal void SeedPlayerDefaults(RegistryAccess? registries, bool known)
    {
        _map.Clear();
        _serverStated.Clear();
        Known = known;
        if (registries is null)
            return;

        Registry<AttributeDefinition> attributes = registries.Attributes;
        foreach ((string name, double? baseValue) in PlayerSeeds)
        {
            Identifier canonical = Identifier.Minecraft(name);
            if (!TryResolveEitherSpelling(attributes, canonical, out RegistryEntry<AttributeDefinition> entry))
                continue;

            AttributeInstance instance = _map.GetOrCreate(canonical, entry);
            if (baseValue is { } seeded)
                instance.BaseValue = seeded;

        }
    }

    /// <summary>Finds an attribute by canonical name in a registry that may carry either spelling. The registry is keyed by the RAW report name, so 766/767 hold <c>minecraft:generic.movement_speed</c> while 768+ hold <c>minecraft:movement_speed</c>; a canonical probe has to try both. The scan is over a registry of at most 40 entries and runs once per login, not per tick.</summary>
    private static bool TryResolveEitherSpelling(
        Registry<AttributeDefinition> attributes, Identifier canonical, out RegistryEntry<AttributeDefinition> entry)
    {
        if (attributes.TryGet(canonical, out entry))
            return true;

        foreach (RegistryEntry<AttributeDefinition> candidate in attributes)
            if (AttributeIds.Canonical(candidate.Id) == canonical)
            {
                entry = candidate;
                return true;
            }

        entry = default;
        return false;
    }
}
