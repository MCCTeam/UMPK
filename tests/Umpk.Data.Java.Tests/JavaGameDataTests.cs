using Umpk;
using Umpk.Game.Registries;
using Umpk.Protocol.Java;
using Xunit;

namespace Umpk.Data.Java.Tests;

/// <summary>Pins the runtime item registry that <see cref="JavaGameData"/> builds from the generated per-version tables: the flat name table on 1.13+ and the legacy composite table before that. This is the registry a session installs into the codec context so clientbound item stacks resolve their ids.</summary>
public sealed class JavaGameDataTests
{
    [Theory]
    [InlineData(770)] // 1.21.5 (hashed slots)
    [InlineData(767)] // 1.21.1 (components)
    [InlineData(404)] // 1.13.2 (present-flag varint id; the item-format flip)
    [InlineData(393)] // 1.13 (short id; first flattened protocol)
    public void FlattenedProtocol_Items_ResolveStoneAndAir(int protocol)
    {
        Registry<ItemDefinition> items = JavaGameData.Registries(protocol).Items;

        Assert.True(items.Count > 0, $"protocol {protocol}: expected a populated item registry.");

        // Index 0 is always minecraft:air; index 1 is minecraft:stone across the flattened era.
        Assert.True(items.TryGet(0, out RegistryEntry<ItemDefinition> air), $"protocol {protocol}: item id 0 missing.");
        Assert.Equal(Identifier.Minecraft("air"), air.Id);

        Assert.True(items.TryGet(1, out RegistryEntry<ItemDefinition> stone), $"protocol {protocol}: item id 1 missing.");
        Assert.Equal(Identifier.Minecraft("stone"), stone.Id);
    }

    [Fact]
    public void Registries_AreCachedPerProtocol()
    {
        Assert.Same(JavaGameData.Registries(770), JavaGameData.Registries(770));
    }

    // Pre-flattening (1.8-1.12.2) items are keyed by the (item_id << 16) | damage composite, which the positional name table cannot express, so those versions build from the generated LegacyItemDefs table instead. Every key below is a BASE composite (damage 0). The pre-flattening tables carry one row per item id, named with the era's own registry key; a damage variant is not a separate item, it is the same item plus a damage component resolved by ItemStackCodecs.ResolveLegacyItem.
    [Theory]
    [InlineData(47, 3, 0, "dirt")]        // 1.12.2/1.8 join-inventory case
    [InlineData(47, 297, 0, "bread")]     // 1.8 give-bread case
    [InlineData(47, 1, 0, "stone")]
    [InlineData(47, 35, 0, "wool")]       // block item: 1.8's own name, not the flattened white_wool
    [InlineData(47, 5, 0, "planks")]      // the id `creativegive minecraft:planks` could not find on 1.8
    [InlineData(47, 165, 0, "slime")]     // slime_block is the 1.13 rename
    [InlineData(47, 326, 0, "water_bucket")] // remains distinct from the block registry entry with the same name
    [InlineData(340, 3, 0, "dirt")]
    [InlineData(340, 297, 0, "bread")]
    [InlineData(340, 263, 0, "coal")]
    [InlineData(340, 35, 0, "wool")]      // the same identifier on 1.12.2 as on 1.8
    [InlineData(340, 5, 0, "planks")]
    [InlineData(340, 165, 0, "slime")]
    public void PreFlattening_Items_ResolveByCompositeKey(int protocol, int itemId, int damage, string name)
    {
        Registry<ItemDefinition> items = JavaGameData.Registries(protocol).Items;

        Assert.True(items.Count > 0, $"protocol {protocol}: expected a populated legacy item registry.");

        int composite = (itemId << 16) | damage;
        Assert.True(items.TryGet(composite, out RegistryEntry<ItemDefinition> item),
            $"protocol {protocol}: composite key {composite} (id {itemId}, damage {damage}) missing.");
        Assert.Equal(Identifier.Minecraft(name), item.Id);
    }

    [Fact]
    public void EveryPreFlatteningProtocol_HasAPopulatedItemRegistry()
    {
        // The whole pre-flattening band, not just the two versions the live sessions ran on.
        int[] preFlattening = [47, 107, 108, 109, 110, 210, 315, 316, 335, 338, 340];
        foreach (int protocol in preFlattening)
        {
            Registry<ItemDefinition> items = JavaGameData.Registries(protocol).Items;
            Assert.True(items.Count > 0, $"protocol {protocol}: legacy item registry is empty.");
            // Air is composite key 0 on every legacy dataset (the DataGen validator enforces it).
            Assert.True(items.TryGet(0, out RegistryEntry<ItemDefinition> air), $"protocol {protocol}: air missing.");
            Assert.Equal(Identifier.Minecraft("air"), air.Id);
        }
    }

    [Fact]
    public void PreFlattening_UnmappedCompositeKey_IsAbsentRatherThanPlaceholder()
    {
        // The registry itself never fabricates entries; degrading an unmapped id to minecraft:unknown is the decoder's policy (ItemStackCodecs.ResolveLegacyItem), not the registry's.
        Registry<ItemDefinition> items = JavaGameData.Registries(47).Items;
        Assert.False(items.TryGet(4000 << 16, out _));
        Assert.False(items.ContainsKey(Identifier.Minecraft("unknown")));
    }

    // Block and entity-type registries are populated from the generated block-definition and entity tables for every era. Without this, every tracked entity resolved to minecraft:unknown and World.GetBlock threw "Registry 'minecraft:block' has no entry with network id 0".
    [Theory]
    [InlineData(776)] // 26.2 (modern flat)
    [InlineData(754)] // 1.16.5 (mid flat)
    [InlineData(47)]  // 1.8 (legacy id:meta / dual entity id space -> SpawnMob space)
    public void Blocks_ArePopulatedAndResolveAirAtStateZero(int protocol)
    {
        Registry<BlockDefinition> blocks = JavaGameData.Registries(protocol).Blocks;

        Assert.True(blocks.Count > 0, $"protocol {protocol}: expected a populated block registry.");

        // Block network id 0 is minecraft:air across every era (legacy material "air" defaults to the minecraft namespace). This is the id RegistryBlockDataSource resolves state 0 to.
        Assert.True(blocks.TryGet(0, out RegistryEntry<BlockDefinition> air), $"protocol {protocol}: block id 0 missing.");
        Assert.Equal(Identifier.Minecraft("air"), air.Id);
        Assert.True(air.Value.OwnsState(0), $"protocol {protocol}: air should own block-state 0.");
    }

    [Theory]
    [InlineData(776, 30, 151)]  // 26.2: cow=30, zombie=151
    [InlineData(754, 11, 102)]  // 1.16.5: cow=11, zombie=102
    [InlineData(47, 92, 54)]    // 1.8 SpawnMob space: cow=92, zombie=54
    public void EntityTypes_ResolveKnownIdsToRealIdentifiers(int protocol, int cowId, int zombieId)
    {
        Registry<EntityTypeDefinition> entityTypes = JavaGameData.Registries(protocol).EntityTypes;

        Assert.True(entityTypes.Count > 0, $"protocol {protocol}: expected a populated entity-type registry.");

        Assert.True(entityTypes.TryGet(cowId, out RegistryEntry<EntityTypeDefinition> cow), $"protocol {protocol}: cow id {cowId} missing.");
        Assert.Equal(Identifier.Minecraft("cow"), cow.Id);
        Assert.NotEqual(Identifier.Minecraft("unknown"), cow.Id);

        Assert.True(entityTypes.TryGet(zombieId, out RegistryEntry<EntityTypeDefinition> zombie), $"protocol {protocol}: zombie id {zombieId} missing.");
        Assert.Equal(Identifier.Minecraft("zombie"), zombie.Id);
    }

    /// <summary>Every supported protocol resolves every per-protocol table. The tables answer an unsupported protocol by throwing, so this walk is what says the 49 that exist are complete: a version added without its arm fails here instead of returning an empty span that reads as "no blocks".</summary>
    /// <remarks>The item-name table is reached through <see cref="JavaGameData.Registries"/> on the flattened protocols only, because the legacy protocols resolve items from the composite table instead. That is the one table this walk cannot force on all 49, and it does not matter for a new version: every protocol added from here on is flattened.</remarks>
    [Fact]
    public void EverySupportedProtocol_ResolvesEveryTable()
    {
        foreach (JavaVersion version in JavaVersions.All)
        {
            int protocol = version.Version.Protocol;
            RegistryAccess registries = JavaGameData.Registries(protocol);
            Assert.True(registries.Blocks.Count > 0, $"protocol {protocol}: no blocks.");
            Assert.True(registries.Items.Count > 0, $"protocol {protocol}: no items.");
            Assert.True(registries.EntityTypes.Count > 0, $"protocol {protocol}: no entity types.");

            JavaGameData.SoundName(protocol, 0);
            Assert.NotNull(JavaGameData.BlockShapes(protocol));
            Assert.NotNull(JavaGameData.BlockPushData(protocol));
        }
    }

    /// <summary>The legacy composite item table is the one dispatcher that keeps a default, because <c>Descriptor.LegacyItemDefs</c> is emitted for the pre-flattening versions alone: it answers empty on a flattened protocol rather than refusing it. Pinned through the registry it feeds, since the table itself is private.</summary>
    [Theory]
    [InlineData(340, true)]   // 1.12.2, the last pre-flattening protocol
    [InlineData(393, false)]  // 1.13, the first flattened one
    public void TheLegacyItemTable_AnswersEmptyOnAFlattenedProtocol(int protocol, bool composite)
    {
        Registry<ItemDefinition> items = JavaGameData.Registries(protocol).Items;

        // A composite key is (id << 16) | damage, so stone is 1 << 16 before the flattening and the flat index 1 after it. Both eras populate the same registry, keyed by their own wire id.
        Assert.True(items.TryGet(composite ? 1 << 16 : 1, out RegistryEntry<ItemDefinition> stone));
        Assert.Equal(Identifier.Minecraft("stone"), stone.Id);
    }
}
