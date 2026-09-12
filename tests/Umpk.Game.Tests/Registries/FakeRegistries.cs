using Umpk.Game.Inventory;
using Umpk.Game.Registries;

namespace Umpk.Game.Tests.Registries;

/// <summary>Builds minimal but complete registry snapshots for tests.</summary>
internal static class FakeRegistries
{
    public static RegistrySnapshot FullSnapshot()
    {
        return new RegistrySnapshotBuilder()
            .Add(new RegistryBuilder<BlockDefinition>(RegistryIds.Block, 1)
                .Add(0, Identifier.Minecraft("air"), new BlockDefinition(0, 0, 0)).Build())
            .Add(new RegistryBuilder<ItemDefinition>(RegistryIds.Item, 1)
                .Add(0, Identifier.Minecraft("air"), new ItemDefinition(64)).Build())
            .Add(new RegistryBuilder<EntityTypeDefinition>(RegistryIds.EntityType, 1)
                .Add(0, Identifier.Minecraft("zombie"), new EntityTypeDefinition(0.6f, 1.95f)).Build())
            .Add(new RegistryBuilder<DimensionTypeDefinition>(RegistryIds.DimensionType, 1)
                .Add(0, Identifier.Minecraft("overworld"), new DimensionTypeDefinition(-64, 384, true)).Build())
            .Add(new RegistryBuilder<BiomeDefinition>(RegistryIds.Biome, 1)
                .Add(0, Identifier.Minecraft("plains"), new BiomeDefinition()).Build())
            .Add(new RegistryBuilder<EnchantmentDefinition>(RegistryIds.Enchantment, 1)
                .Add(0, Identifier.Minecraft("sharpness"), new EnchantmentDefinition(5)).Build())
            .Add(new RegistryBuilder<MobEffectDefinition>(RegistryIds.MobEffect, 1)
                .Add(0, Identifier.Minecraft("speed"), new MobEffectDefinition(MobEffectCategory.Beneficial)).Build())
            .Add(new RegistryBuilder<AttributeDefinition>(RegistryIds.Attribute, 1)
                .Add(0, Identifier.Minecraft("generic.max_health"), new AttributeDefinition(20, 1, 1024)).Build())
            .Add(new RegistryBuilder<MenuTypeDefinition>(RegistryIds.Menu, 1)
                .Add(0, Identifier.Minecraft("generic_9x3"), new MenuTypeDefinition()).Build())
            .Build();
    }
}
