using Umpk.Game.Items;
using Umpk.Game.Registries;

namespace Umpk.Client.Tests.Support;

/// <summary>Builds bound <see cref="ItemStack"/>s from a tiny fixed item registry for inventory tests.</summary>
internal static class TestItems
{
    private static readonly Registry<ItemDefinition> Items = new RegistryBuilder<ItemDefinition>(RegistryIds.Item)
        .Add(1, Identifier.Minecraft("stone"), new ItemDefinition(64))
        .Add(2, Identifier.Minecraft("diamond_sword"), new ItemDefinition(1))
        .Build();

    public static ItemStack Stone(int count = 1) => new(Get(1), count);

    public static ItemStack DiamondSword(int count = 1) => new(Get(2), count);

    private static RegistryEntry<ItemDefinition> Get(int networkId)
    {
        Items.TryGet(networkId, out RegistryEntry<ItemDefinition> entry);
        return entry;
    }
}
