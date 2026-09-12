namespace Umpk.Game.Registries;

/// <summary>The immutable, per-session view over all registries. One is built at login/configuration and, per the codec-context memory model, it is immutable for the life of a phase and swapped wholesale at a phase pause rather than mutated in place. There is no global or static registry access anywhere. Typed properties resolve the well-known vanilla registries; <see cref="TryGetRegistry"/> reaches any registry present in the backing snapshot by id.</summary>
public sealed class RegistryAccess
{
    private readonly RegistrySnapshot _snapshot;

    private RegistryAccess(
        RegistrySnapshot snapshot,
        Registry<BlockDefinition> blocks,
        Registry<ItemDefinition> items,
        Registry<EntityTypeDefinition> entityTypes,
        Registry<DimensionTypeDefinition> dimensionTypes,
        Registry<BiomeDefinition> biomes,
        Registry<EnchantmentDefinition> enchantments,
        Registry<MobEffectDefinition> mobEffects,
        Registry<AttributeDefinition> attributes,
        Registry<Umpk.Game.Inventory.MenuTypeDefinition> menuTypes,
        Registry<EntityTypeDefinition>? legacyObjectTypes,
        Registry<ChatTypeDefinition>? chatTypes)
    {
        _snapshot = snapshot;
        MenuTypes = menuTypes;
        LegacyObjectTypes = legacyObjectTypes;
        ChatTypes = chatTypes;
        Blocks = blocks;
        Items = items;
        EntityTypes = entityTypes;
        DimensionTypes = dimensionTypes;
        Biomes = biomes;
        Enchantments = enchantments;
        MobEffects = mobEffects;
        Attributes = attributes;
    }

    /// <summary>The block registry (<c>minecraft:block</c>).</summary>
    public Registry<BlockDefinition> Blocks { get; }

    /// <summary>The item registry (<c>minecraft:item</c>).</summary>
    public Registry<ItemDefinition> Items { get; }

    /// <summary>The entity-type registry (<c>minecraft:entity_type</c>).</summary>
    public Registry<EntityTypeDefinition> EntityTypes { get; }

    /// <summary>The pre-1.14 SpawnObject entity-type registry, or null on versions with a single id space. Present only when the snapshot carries <see cref="RegistryIds.LegacyObjectType"/>, which is exactly the pre-1.14 era: those versions spawn objects through an id space disjoint from <see cref="EntityTypes"/>, so an <c>add_entity</c> type id must be resolved here and an <c>add_mob</c> one against <see cref="EntityTypes"/>. Resolving either against the other's table does not miss, it MISLABELS (object 1 is a boat, mob 1 is a dropped item).</summary>
    public Registry<EntityTypeDefinition>? LegacyObjectTypes { get; }

    /// <summary>True when this version has separate SpawnObject and SpawnMob entity id spaces (pre-1.14), i.e. when <see cref="LegacyObjectTypes"/> is present.</summary>
    public bool HasSplitEntityIdSpaces => LegacyObjectTypes is not null;

    /// <summary>The dimension-type registry (<c>minecraft:dimension_type</c>).</summary>
    public Registry<DimensionTypeDefinition> DimensionTypes { get; }

    /// <summary>The chat-type registry (<c>minecraft:chat_type</c>), or null when the session has none. Null is the honest answer below 1.19 (protocol 759), where no such registry exists on the wire because the server composes the chat line itself, and on a 1.19+ session before the server's registry data has been applied. Present only when the server actually sent the table.</summary>
    public Registry<ChatTypeDefinition>? ChatTypes { get; }

    /// <summary>The biome registry (<c>minecraft:worldgen/biome</c>).</summary>
    public Registry<BiomeDefinition> Biomes { get; }

    /// <summary>The enchantment registry (<c>minecraft:enchantment</c>).</summary>
    public Registry<EnchantmentDefinition> Enchantments { get; }

    /// <summary>The container-window kind registry (<c>minecraft:menu</c>). Populated on every supported version: from 1.14 with the ids the server's registry report declares, and before that with the window-type strings open_screen carries (keyed by identifier, numbered synthetically). Resolving an open container against this is what lets a consumer name the window instead of guessing its kind from its slot count.</summary>
    public Registry<Umpk.Game.Inventory.MenuTypeDefinition> MenuTypes { get; }

    /// <summary>The status-effect registry (<c>minecraft:mob_effect</c>).</summary>
    public Registry<MobEffectDefinition> MobEffects { get; }

    /// <summary>The attribute registry (<c>minecraft:attribute</c>).</summary>
    public Registry<AttributeDefinition> Attributes { get; }

    /// <summary>The snapshot this access reads from, for reaching non-well-known registries.</summary>
    public RegistrySnapshot Snapshot => _snapshot;

    /// <summary>Resolves any registry in the backing snapshot by id, in its non-generic form.</summary>
    public bool TryGetRegistry(Identifier registryId, out IRegistry registry) =>
        _snapshot.TryGetRegistry(registryId, out registry);

    /// <summary>Builds a <see cref="RegistryAccess"/> over a snapshot. The snapshot must contain each of the well-known registries named in <see cref="RegistryIds"/> with the expected element type.</summary>
    /// <exception cref="ArgumentException">A required registry is missing or has the wrong element type.</exception>
    public static RegistryAccess FromSnapshot(RegistrySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new RegistryAccess(
            snapshot,
            Require<BlockDefinition>(snapshot, RegistryIds.Block),
            Require<ItemDefinition>(snapshot, RegistryIds.Item),
            Require<EntityTypeDefinition>(snapshot, RegistryIds.EntityType),
            Require<DimensionTypeDefinition>(snapshot, RegistryIds.DimensionType),
            Require<BiomeDefinition>(snapshot, RegistryIds.Biome),
            Require<EnchantmentDefinition>(snapshot, RegistryIds.Enchantment),
            Require<MobEffectDefinition>(snapshot, RegistryIds.MobEffect),
            Require<AttributeDefinition>(snapshot, RegistryIds.Attribute),
            Require<Umpk.Game.Inventory.MenuTypeDefinition>(snapshot, RegistryIds.Menu),
            snapshot.TryGetRegistry(RegistryIds.LegacyObjectType, out Registry<EntityTypeDefinition> objectTypes) ? objectTypes : null,
            snapshot.TryGetRegistry(RegistryIds.ChatType, out Registry<ChatTypeDefinition> chatTypes) ? chatTypes : null);
    }

    private static Registry<T> Require<T>(RegistrySnapshot snapshot, Identifier registryId)
        where T : class
    {
        if (!snapshot.TryGetRegistry<T>(registryId, out var registry))
            throw new ArgumentException($"Snapshot is missing the required registry '{registryId}' of type {typeof(T).Name}.", nameof(snapshot));

        return registry;
    }
}
