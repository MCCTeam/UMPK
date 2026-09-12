using Umpk.Game.Registries;

namespace Umpk.Client.Internal;

/// <summary>Resolves an <see cref="EntityTypeDefinition"/> registry entry for a network type id. Uses the synced entity-type registry when present; otherwise falls back to a synthetic generic entry so entity tracking works even before/without registry data. The synthetic entry carries a default hitbox.</summary>
/// <remarks>Pre-1.14 versions carry TWO disjoint spawn id spaces: <c>add_mob</c> (SpawnMob) numbers living entities and <c>add_entity</c> (SpawnObject) numbers objects, and the same number names different things in each (object 1 is a boat, mob 1 is a dropped item). Resolving one against the other's table therefore does not miss, it MISLABELS, which is the failure mode that made this area look healthy in field observations: a 1.8 boat spawn reported as <c>minecraft:item</c>. Callers pass <see cref="EntitySpawnSpace"/> so each spawn packet resolves against its own space; on 1.14+ the spaces merged and the parameter is inert.</remarks>
internal sealed class EntityTypeResolver
{
    private readonly Registry<EntityTypeDefinition> _fallback;

    public EntityTypeResolver()
    {
        _fallback = new RegistryBuilder<EntityTypeDefinition>(RegistryIds.EntityType)
            .Add(0, Identifier.Minecraft("unknown"), new EntityTypeDefinition(0.6f, 1.8f))
            .Build();
    }

    /// <summary>Resolves a spawn type id against the id space the spawn packet belongs to. An id the era's table does not know degrades to the <c>minecraft:unknown</c> placeholder rather than throwing: entity tracking must survive an unmapped type.</summary>
    public RegistryEntry<EntityTypeDefinition> Resolve(RegistryAccess? registries, int typeId, EntitySpawnSpace space)
    {
        if (registries is null)
            return _fallback[0];

        // The object space exists only on pre-1.14 versions. When it is present it is authoritative for object spawns, and there is deliberately NO fallback to the mob table: a hit there would be a wrong name, which is worse than an honest unknown.
        if (space == EntitySpawnSpace.Object && registries.LegacyObjectTypes is { } objectTypes)
            return objectTypes.TryGet(typeId, out RegistryEntry<EntityTypeDefinition> objectEntry)
                ? objectEntry
                : _fallback[0];

        return registries.EntityTypes.TryGet(typeId, out RegistryEntry<EntityTypeDefinition> entry)
            ? entry
            : _fallback[0];
    }

    /// <summary>Resolves a spawn whose packet carries no type id because the packet identity is the type: <c>add_player</c>, <c>add_experience_orb</c>, and pre-1.9 <c>add_painting</c>. Resolving by key is version-independent; an era whose registry lacks the key degrades to the placeholder.</summary>
    public RegistryEntry<EntityTypeDefinition> ResolveByKey(RegistryAccess? registries, Identifier key)
    {
        if (registries is not null && registries.EntityTypes.TryGet(key, out RegistryEntry<EntityTypeDefinition> entry))
            return entry;

        return _fallback[0];
    }
}

/// <summary>Which pre-1.14 spawn id space a type id came from. Inert on 1.14+, where the spaces merged.</summary>
internal enum EntitySpawnSpace
{
    /// <summary>The SpawnMob space (<c>add_mob</c>), and the single merged space on 1.14+.</summary>
    Living,

    /// <summary>The pre-1.14 SpawnObject space (<c>add_entity</c>).</summary>
    Object,
}
