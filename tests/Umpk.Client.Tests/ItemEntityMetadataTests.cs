using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Items;
using Umpk.Game.Registries;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Generic dropped-item metadata reaches and updates the tracked entity surface.</summary>
public sealed class ItemEntityMetadataTests
{
    [Fact]
    public async Task CarriedItem_IsProjectedAndClearedByLaterMetadata()
    {
        const int protocol = 770;
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var harness = new ApplierHarness(version!, new ClientFeatures { Entities = true });
        RegistryEntry<EntityTypeDefinition> itemType = Registry.Direct(
            1,
            Identifier.Minecraft("item"),
            new EntityTypeDefinition(0.25f, 0.25f));
        var entity = new Entity(
            42,
            Guid.NewGuid(),
            itemType,
            JavaGameData.EntityMetadataKeys(protocol));
        harness.State.Entities.Add(entity);

        RegistryEntry<ItemDefinition> sugarCane = Registry.Direct(
            7,
            Identifier.Minecraft("sugar_cane"),
            new ItemDefinition(64));
        var stack = new ItemStack(sugarCane, 3);

        await harness.ApplyAsync(new ClientboundSetEntityDataPacket(
            42,
            new EntityMetadataList([new EntityDataEntry(8, MetadataValue.Slot(stack))], [])));

        Assert.Same(stack, entity.CarriedItem);
        Assert.True(entity.Metadata.TryGet(EntityMetadataKeys.CarriedItem, out IMetadataSlot? projected));
        Assert.Same(stack, projected);

        await harness.ApplyAsync(new ClientboundSetEntityDataPacket(
            42,
            new EntityMetadataList([new EntityDataEntry(8, MetadataValue.Slot(ItemStack.Empty))], [])));

        Assert.Null(entity.CarriedItem);
    }
}
