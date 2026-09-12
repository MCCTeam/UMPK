using Umpk.Game.Entities;
using Umpk.Game.Registries;

namespace Umpk.Game.Tests.Entities;

/// <summary>Shared fixtures for the entity tests: registry handles and a controllable key source.</summary>
internal static class EntityTestFixtures
{
    public static Registry<EntityTypeDefinition> EntityTypes { get; } =
        new RegistryBuilder<EntityTypeDefinition>(RegistryIds.EntityType, 4)
            .Add(0, Identifier.Minecraft("zombie"), new EntityTypeDefinition(0.6f, 1.95f))
            .Add(1, Identifier.Minecraft("boat"), new EntityTypeDefinition(1.375f, 0.5625f))
            .Add(2, Identifier.Minecraft("player"), new EntityTypeDefinition(0.6f, 1.8f))
            // A foreign-namespace type: the case a bare "zombie" needle must NOT reach.
            .Add(3, new Identifier("mod", "zombie"), new EntityTypeDefinition(0.6f, 1.95f))
            .Build();

    public static Registry<MobEffectDefinition> Effects { get; } =
        new RegistryBuilder<MobEffectDefinition>(RegistryIds.MobEffect, 2)
            .Add(1, Identifier.Minecraft("speed"), new MobEffectDefinition(MobEffectCategory.Beneficial))
            .Add(2, Identifier.Minecraft("slowness"), new MobEffectDefinition(MobEffectCategory.Harmful))
            .Build();

    public static Registry<AttributeDefinition> Attributes { get; } =
        new RegistryBuilder<AttributeDefinition>(RegistryIds.Attribute, 2)
            .Add(0, Identifier.Minecraft("generic.max_health"), new AttributeDefinition(20, 1, 1024))
            .Add(1, Identifier.Minecraft("generic.movement_speed"), new AttributeDefinition(0.1, 0, 1024))
            .Build();

    public static RegistryEntry<EntityTypeDefinition> Zombie => EntityTypes[0];

    public static RegistryEntry<EntityTypeDefinition> Boat => EntityTypes[1];

    public static RegistryEntry<EntityTypeDefinition> Player => EntityTypes[2];

    /// <summary>A modded <c>mod:zombie</c> type, same path as <see cref="Zombie"/> but a foreign namespace.</summary>
    public static RegistryEntry<EntityTypeDefinition> ModZombie => EntityTypes[3];

    public static Entity NewEntity(int id, RegistryEntry<EntityTypeDefinition> type, IMetadataKeySource? keySource = null) =>
        new(id, Guid.NewGuid(), type, keySource);
}

/// <summary>A key source backed by an explicit per-(type, key) index table for tier-2 tests.</summary>
internal sealed class FakeMetadataKeySource : IMetadataKeySource
{
    private readonly Dictionary<(int NetworkId, MetadataKey Key), int> _map = [];

    public FakeMetadataKeySource Map(RegistryEntry<EntityTypeDefinition> type, MetadataKey key, int index)
    {
        _map[(type.NetworkId, key)] = index;
        return this;
    }

    public bool TryResolveIndex(RegistryEntry<EntityTypeDefinition> entityType, MetadataKey key, out int index) =>
        _map.TryGetValue((entityType.NetworkId, key), out index);
}
