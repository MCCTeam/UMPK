using Umpk.Game.Registries;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary>Builds a populated <see cref="PacketCodecContext"/> for the item-family codec tests. Modern item stacks resolve their holder id against the item registry, and legacy (1.8) stacks resolve the <c>(id&lt;&lt;16)|damage</c> composite against the same registry; both are seeded here. Enchantments and attributes are seeded so the enchantment/attribute-modifier component codecs resolve too.</summary>
internal static class ItemTestRegistries
{
    /// <summary>A modern item network id present in the registry.</summary>
    public const int Stone = 1;

    /// <summary>A second modern item network id present in the registry.</summary>
    public const int DiamondSword = 2;

    /// <summary>A modern item network id for <c>minecraft:filled_map</c>. Filled maps exercise the <c>minecraft:map_id</c> component path used by inventory patches from 1.20.5 onward.</summary>
    public const int FilledMap = 3;

    /// <summary><c>minecraft:diamond</c> as protocol 485 (1.14.2) numbers it. Seeded because the merchant-offers era tests reconstruct a REAL 1.14.2 villager open byte for byte, and both ids in that frame are two-byte VarInts, which is what makes its payload exactly the 37 bytes measured live.</summary>
    public const int Diamond1142 = 529;

    /// <summary><c>minecraft:emerald</c> as protocol 485 (1.14.2) numbers it.</summary>
    public const int Emerald1142 = 759;

    /// <summary>A legacy base item id (1.8 numeric item id) present as a composite base entry.</summary>
    public const int LegacyBaseItemId = 267; // diamond sword in 1.8

    /// <summary>A legacy subtype item id (1.8 numeric id) with a non-zero damage subtype.</summary>
    public const int LegacyBlockItemId = 35; // wool

    /// <summary>The subtype damage for <see cref="LegacyBlockItemId"/> that is registered as a composite.</summary>
    public const int LegacyBlockSubtype = 14; // red wool

    /// <summary>A seeded enchantment network id.</summary>
    public const int Sharpness = 12;

    /// <summary>A seeded attribute network id.</summary>
    public const int AttackDamage = 3;

    /// <summary>The shared context all item-family codec tests use.</summary>
    public static PacketCodecContext Context { get; } = Build();

    private static int Composite(int itemId, int damage) => ((itemId & 0xFFFF) << 16) | (damage & 0xFFFF);

    private static PacketCodecContext Build()
    {
        var items = new RegistryBuilder<ItemDefinition>(RegistryIds.Item)
            .Add(Stone, Identifier.Minecraft("stone"), new ItemDefinition(64))
            .Add(DiamondSword, Identifier.Minecraft("diamond_sword"), new ItemDefinition(1))
            .Add(FilledMap, Identifier.Minecraft("filled_map"), new ItemDefinition(64))
            .Add(Diamond1142, Identifier.Minecraft("diamond"), new ItemDefinition(64))
            .Add(Emerald1142, Identifier.Minecraft("emerald"), new ItemDefinition(64))
            // Legacy composites: base item (damage 0) and a subtype block variant. Keyed under distinct identifiers so the (id<<16)|damage composite network ids stay one-to-one with keys.
            .Add(Composite(LegacyBaseItemId, 0), Identifier.Minecraft("legacy_diamond_sword"), new ItemDefinition(1))
            .Add(Composite(LegacyBlockItemId, LegacyBlockSubtype), Identifier.Minecraft("legacy_red_wool"), new ItemDefinition(64))
            .Build();

        var enchantments = new RegistryBuilder<EnchantmentDefinition>(RegistryIds.Enchantment)
            .Add(Sharpness, Identifier.Minecraft("sharpness"), new EnchantmentDefinition(5))
            .Build();

        var attributes = new RegistryBuilder<AttributeDefinition>(RegistryIds.Attribute)
            .Add(AttackDamage, Identifier.Minecraft("generic.attack_damage"), new AttributeDefinition(1, 0, 2048))
            .Build();

        var snapshot = new RegistrySnapshotBuilder()
            .Add(new RegistryBuilder<BlockDefinition>(RegistryIds.Block).Build())
            .Add(items)
            .Add(new RegistryBuilder<EntityTypeDefinition>(RegistryIds.EntityType).Build())
            .Add(new RegistryBuilder<DimensionTypeDefinition>(RegistryIds.DimensionType).Build())
            .Add(new RegistryBuilder<BiomeDefinition>(RegistryIds.Biome).Build())
            .Add(enchantments)
            .Add(new RegistryBuilder<MobEffectDefinition>(RegistryIds.MobEffect).Build())
            .Add(attributes)
            .Add(new RegistryBuilder<Umpk.Game.Inventory.MenuTypeDefinition>(RegistryIds.Menu).Build())
            .Build();

        return new PacketCodecContext(RegistryAccess.FromSnapshot(snapshot), IConnectionCodecState.Empty);
    }

    /// <summary>A bound item handle for a modern network id.</summary>
    public static RegistryEntry<ItemDefinition> Item(int networkId)
    {
        Assert(Context.Registries.Items.TryGet(networkId, out RegistryEntry<ItemDefinition> entry), networkId);
        return entry;
    }

    /// <summary>A bound item handle for a legacy composite key.</summary>
    public static RegistryEntry<ItemDefinition> LegacyItem(int itemId, int damage) => Item(Composite(itemId, damage));

    /// <summary>A bound enchantment handle.</summary>
    public static RegistryEntry<EnchantmentDefinition> Enchantment(int networkId)
    {
        Assert(Context.Registries.Enchantments.TryGet(networkId, out RegistryEntry<EnchantmentDefinition> entry), networkId);
        return entry;
    }

    /// <summary>A bound attribute handle.</summary>
    public static RegistryEntry<AttributeDefinition> Attribute(int networkId)
    {
        Assert(Context.Registries.Attributes.TryGet(networkId, out RegistryEntry<AttributeDefinition> entry), networkId);
        return entry;
    }

    private static void Assert(bool condition, int id)
    {
        if (!condition)
            throw new InvalidOperationException($"Test registry is missing network id {id}.");

    }
}
