using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Protocol.Java.Codecs;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

public sealed class NestedComponentHashAvailabilityTests
{
    [Theory]
    [InlineData("declared-unmodeled")]
    [InlineData("absent-key")]
    public void UnavailableNestedValueOmitsWholeOuterHashAndPreservesKnownSibling(string kind)
    {
        var registries = JavaGameData.Registries(770);
        var context = new PacketCodecContext(registries, IConnectionCodecState.Empty);
        ItemComponentTable table = ItemComponentTable.V1_21_5();
        ItemStack nested = WithUnavailableValue(new ItemStack(registries.Items[Identifier.Minecraft("stone")], 1), kind, table);
        ItemStack outer = new ItemStack(registries.Items[Identifier.Minecraft("stone")], 1)
            .With(DataComponents.Container, new ContainerComponent([new ContainerSlotEntry(0, nested)]))
            .With(DataComponents.MapId, new MapIdComponent(29));

        HashedStackValue hashed = ItemStackCodecs.ToHashedStack(outer, table, context);

        // A hash of the container with the unknown nested field removed would assert a value that was never modeled.
        Assert.DoesNotContain(hashed.AddedComponentHashes, entry => entry.ComponentId == table.WireId(DataComponents.Container));
        (int componentId, int hash) = Assert.Single(hashed.AddedComponentHashes);
        Assert.Equal(table.WireId(DataComponents.MapId), componentId);
        Assert.Equal(new HashOps().Int(29), hash);
        Assert.Equal(outer.Item.NetworkId, hashed.ItemId);
        Assert.Equal(1, hashed.Count);
        Assert.Empty(hashed.RemovedComponentIds);
    }

    [Fact]
    public void ExistingTopLevelUnavailableValueIsOmittedWithoutDroppingKnownSibling()
    {
        var registries = JavaGameData.Registries(770);
        var context = new PacketCodecContext(registries, IConnectionCodecState.Empty);
        ItemComponentTable table = ItemComponentTable.V1_21_5();
        foreach (string kind in new[] { "declared-unmodeled", "absent-key" })
        {
            ItemStack stack = WithUnavailableValue(new ItemStack(registries.Items[Identifier.Minecraft("stone")], 1), kind, table)
                .With(DataComponents.MapId, new MapIdComponent(29));

            HashedStackValue hashed = ItemStackCodecs.ToHashedStack(stack, table, context);

            (int componentId, int hash) = Assert.Single(hashed.AddedComponentHashes);
            Assert.Equal(table.WireId(DataComponents.MapId), componentId);
            Assert.Equal(new HashOps().Int(29), hash);
            Assert.Equal(stack.Item.NetworkId, hashed.ItemId);
            Assert.Equal(1, hashed.Count);
            Assert.Empty(hashed.RemovedComponentIds);
        }
    }

    private static ItemStack WithUnavailableValue(ItemStack stack, string kind, ItemComponentTable table)
    {
        Identifier id = Identifier.Minecraft(kind == "declared-unmodeled" ? "weapon" : "sulfur_cube_content");
        DataComponentType<UnmodeledComponent> key = DataComponents.Unmodeled(id);
        Assert.Equal(kind == "declared-unmodeled", table.Contains(key));
        // Public model inputs exercise the current hash API. The absent-era key is deliberately not claimed to be a valid770 wire payload.
        return stack.With(key, new UnmodeledComponent(id, [1, 0, 0, 0, 0]));
    }
}
