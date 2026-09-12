using Umpk.Game.Items;
using Umpk.Game.Registries;

namespace Umpk.Game.Tests.Items;

/// <summary>Shared helpers for building item registries and stacks in the Items/Inventory tests.</summary>
internal static class ItemTestData
{
    public static Registry<ItemDefinition> Items { get; } =
        new RegistryBuilder<ItemDefinition>(RegistryIds.Item, 8)
            .Add(0, Identifier.Minecraft("air"), new ItemDefinition(64))
            .Add(1, Identifier.Minecraft("stone"), new ItemDefinition(64))
            .Add(2, Identifier.Minecraft("dirt"), new ItemDefinition(64))
            .Add(3, Identifier.Minecraft("diamond_sword"), new ItemDefinition(1))
            .Add(4, Identifier.Minecraft("ender_pearl"), new ItemDefinition(16))
            .Add(5, Identifier.Minecraft("oak_planks"), new ItemDefinition(64))
            .Add(6, Identifier.Minecraft("apple"), new ItemDefinition(64))
            .Add(7, Identifier.Minecraft("bread"), new ItemDefinition(64))
            .Build();

    public static Registry<EnchantmentDefinition> Enchantments { get; } =
        new RegistryBuilder<EnchantmentDefinition>(RegistryIds.Enchantment, 4)
            .Add(0, Identifier.Minecraft("sharpness"), new EnchantmentDefinition(5))
            .Add(1, Identifier.Minecraft("unbreaking"), new EnchantmentDefinition(3))
            .Add(2, Identifier.Minecraft("protection"), new EnchantmentDefinition(4))
            .Add(3, Identifier.Minecraft("efficiency"), new EnchantmentDefinition(5))
            .Build();

    public static RegistryEntry<ItemDefinition> Item(string path) =>
        Items.TryGet(Identifier.Minecraft(path), out var entry)
            ? entry
            : throw new ArgumentException($"unknown test item '{path}'", nameof(path));

    public static ItemStack Stack(string path, int count) => new(Item(path), count);

    public static RegistryEntry<EnchantmentDefinition> Enchantment(string path) =>
        Enchantments.TryGet(Identifier.Minecraft(path), out var entry)
            ? entry
            : throw new ArgumentException($"unknown test enchantment '{path}'", nameof(path));
}
