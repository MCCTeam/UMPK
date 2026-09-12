using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary>Seeded byte-level round-trip tests for every item/container-family packet on the protocols it exists on (47 legacy, 770, 776). Item-bearing packets run against the populated item registry context; simple packets exercise their scalar fields and collection edge cases (empty/one/many).</summary>
public class ItemPacketCodecTests
{
    private static ItemStack Stone(int count) => new(ItemTestRegistries.Item(ItemTestRegistries.Stone), count);

    // open_screen.

    [Fact]
    public void OpenScreen_V1_8_RoundTrips()
    {
        var p = new ClientboundOpenScreenPacket(7, -1, Component.Text("Chest"), "minecraft:chest", 27, null);
        var d = ItemCodecRoundTrip.Cycle(ContainerCodecs.OpenScreenV1_8, p);
        Assert.Equal(7, d.ContainerId);
        Assert.Equal("minecraft:chest", d.LegacyType);
        Assert.Equal(27, d.LegacySlotCount);
    }

    [Fact]
    public void OpenScreen_V1_8_HorseCarriesEntityId()
    {
        var p = new ClientboundOpenScreenPacket(3, -1, Component.Text("Horse"), "EntityHorse", 17, 4242);
        var d = ItemCodecRoundTrip.Cycle(ContainerCodecs.OpenScreenV1_8, p);
        Assert.Equal(4242, d.LegacyEntityId);
    }

    [Fact]
    public void OpenScreen_Modern_RoundTrips()
    {
        var p = new ClientboundOpenScreenPacket(5, 2, Component.Text("Furnace"), null, 0, null);
        var d = ItemCodecRoundTrip.Cycle(ContainerCodecs.OpenScreenModern, p);
        Assert.Equal(5, d.ContainerId);
        Assert.Equal(2, d.MenuTypeId);
    }

    // container_close.

    [Fact]
    public void ContainerClose_AllFlowsAndWireLayouts()
    {
        Assert.Equal(9, ItemCodecRoundTrip.Cycle(ContainerCodecs.ContainerCloseClientV1_8, new ClientboundContainerClosePacket(9)).ContainerId);
        Assert.Equal(9, ItemCodecRoundTrip.Cycle(ContainerCodecs.ContainerCloseClientModern, new ClientboundContainerClosePacket(9)).ContainerId);
        Assert.Equal(9, ItemCodecRoundTrip.Cycle(ContainerCodecs.ContainerCloseServerV1_8, new ServerboundContainerClosePacket(9)).ContainerId);
        Assert.Equal(9, ItemCodecRoundTrip.Cycle(ContainerCodecs.ContainerCloseServerModern, new ServerboundContainerClosePacket(9)).ContainerId);
    }

    // container_set_content.

    public static TheoryData<int> ItemCounts => new() { 0, 1, 5 };

    [Theory]
    [MemberData(nameof(ItemCounts))]
    public void ContainerSetContent_V1_8_RoundTrips(int itemCount)
    {
        var items = new List<ItemStack>();
        for (int i = 0; i < itemCount; i++)
            items.Add(i % 2 == 0 ? Stone(i + 1) : ItemStack.Empty);

        var p = new ClientboundContainerSetContentPacket(2, 0, items, ItemStack.Empty);
        var d = ItemCodecRoundTrip.Cycle(ContainerCodecs.ContainerSetContentV1_8, p);
        Assert.Equal(itemCount, d.Items.Count);
    }

    [Theory]
    [MemberData(nameof(ItemCounts))]
    public void ContainerSetContent_Modern_RoundTrips_770And776(int itemCount)
    {
        var items = new List<ItemStack>();
        for (int i = 0; i < itemCount; i++)
            items.Add(Stone(i + 1));

        var p = new ClientboundContainerSetContentPacket(2, 99, items, Stone(1));
        var d770 = ItemCodecRoundTrip.Cycle(ContainerCodecs.ContainerSetContent770, p);
        var d776 = ItemCodecRoundTrip.Cycle(ContainerCodecs.ContainerSetContent776, p);
        Assert.Equal(itemCount, d770.Items.Count);
        Assert.Equal(99, d770.StateId);
        Assert.Equal(1, d776.CarriedItem.Count);
    }

    // container_set_slot and set_slot.

    [Fact]
    public void ContainerSetData_RoundTrips()
    {
        var p = new ClientboundContainerSetDataPacket(4, 1, 200);
        Assert.Equal(p, ItemCodecRoundTrip.Cycle(ContainerCodecs.ContainerSetDataV1_8, p));
        Assert.Equal(p, ItemCodecRoundTrip.Cycle(ContainerCodecs.ContainerSetDataModern, p));
    }

    // set_cursor_item and set_player_inventory.

    [Fact]
    public void SetCursorItem_RoundTrips_BothWireLayouts()
    {
        var p = new ClientboundSetCursorItemPacket(Stone(3));
        Assert.Equal(3, ItemCodecRoundTrip.Cycle(ContainerCodecs.SetCursorItem770, p).Item.Count);
        Assert.Equal(3, ItemCodecRoundTrip.Cycle(ContainerCodecs.SetCursorItem776, p).Item.Count);
    }

    [Fact]
    public void SetPlayerInventory_RoundTrips_BothWireLayouts()
    {
        var p = new ClientboundSetPlayerInventoryPacket(11, Stone(2));
        Assert.Equal(11, ItemCodecRoundTrip.Cycle(ContainerCodecs.SetPlayerInventory770, p).Slot);
        Assert.Equal(11, ItemCodecRoundTrip.Cycle(ContainerCodecs.SetPlayerInventory776, p).Slot);
    }

    // Transaction in 1.8.

    [Fact]
    public void Transaction_V1_8_BothFlows()
    {
        var c = new ClientboundTransactionPacket(1, 42, true);
        Assert.Equal(c, ItemCodecRoundTrip.Cycle(ContainerCodecs.TransactionClientV1_8, c));
        var s = new ServerboundTransactionPacket(1, 42, false);
        Assert.Equal(s, ItemCodecRoundTrip.Cycle(ContainerCodecs.TransactionServerV1_8, s));
    }

    // container_button_click.

    [Fact]
    public void ContainerButtonClick_BothWireLayouts()
    {
        var p = new ServerboundContainerButtonClickPacket(2, 5);
        Assert.Equal(p, ItemCodecRoundTrip.Cycle(ContainerCodecs.ContainerButtonClickV1_8, p));
        Assert.Equal(p, ItemCodecRoundTrip.Cycle(ContainerCodecs.ContainerButtonClickModern, p));
    }

    // container_click.

    [Fact]
    public void ContainerClick_V1_8_RoundTrips()
    {
        var p = new ServerboundContainerClickPacket(
            ContainerId: 1, StateId: 0, Slot: 5, Button: 0, Mode: 0,
            ActionNumber: 77, LegacyClickedItem: Stone(1), ChangedSlots: [], CarriedItem: null);
        var d = ItemCodecRoundTrip.Cycle(ContainerCodecs.ContainerClickV1_8, p);
        Assert.Equal((short)77, d.ActionNumber);
        Assert.Equal((short)5, d.Slot);
    }

    [Fact]
    public void ContainerClick_Modern_EmptyPrediction_EncodesEmptyHashedStacks_770And776()
    {
        var p = new ServerboundContainerClickPacket(
            ContainerId: 1, StateId: 12, Slot: 3, Button: 1, Mode: 0,
            ActionNumber: 0, LegacyClickedItem: null,
            ChangedSlots: [], CarriedItem: null);

        // A click with no predicted changes still encodes: zero changed slots + one empty hashed carried.
        byte[] b770 = ItemCodecRoundTrip.Encode(ContainerCodecs.ContainerClick770, p);
        byte[] b776 = ItemCodecRoundTrip.Encode(ContainerCodecs.ContainerClick776, p);
        Assert.NotEmpty(b770);
        Assert.Equal(b770, b776); // an empty prediction encodes identically across the two eras
    }

    [Fact]
    public void ContainerClick_Modern_HashesPredictedStacks_AtEncode()
    {
        // The client sends raw predicted stacks; the version-bound codec hashes them per component at encode using its bound era table. A stack carrying a component patch must produce a non-empty hashed value. The emitted bytes are pinned to a frame built with ItemStackCodecs.ToHashedStack.
        var carried = new ItemStack(
            ItemTestRegistries.Item(ItemTestRegistries.Stone), 5,
            DataComponentMap.Empty.With(DataComponents.Damage, new DamageComponent(50)));
        var changedStack = new ItemStack(ItemTestRegistries.Item(ItemTestRegistries.DiamondSword), 1);

        var p = new ServerboundContainerClickPacket(
            ContainerId: 1, StateId: 7, Slot: 2, Button: 0, Mode: 0,
            ActionNumber: 0, LegacyClickedItem: null,
            ChangedSlots: [new PredictedSlot(2, changedStack)], CarriedItem: carried);

        HashedStackValue expectedCarried =
            ItemStackCodecs.ToHashedStack(carried, ItemStackCodecs.ComponentsV1_21_5, ItemTestRegistries.Context);
        Assert.NotEmpty(expectedCarried.AddedComponentHashes); // the damage component actually hashed

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteVarInt(1);
        w.WriteVarInt(7);
        w.WriteShort(2);
        w.WriteByte(0);
        w.WriteVarInt(0);
        w.WriteVarInt(1);
        w.WriteShort(2);
        ItemStackCodecs.WriteHashedStack(ref w,
            ItemStackCodecs.ToHashedStack(changedStack, ItemStackCodecs.ComponentsV1_21_5, ItemTestRegistries.Context));
        ItemStackCodecs.WriteHashedStack(ref w, expectedCarried);
        byte[] expected = buffer.WrittenSpan.ToArray();

        byte[] actual = ItemCodecRoundTrip.Encode(ContainerCodecs.ContainerClick770, p);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void HashStack_NestedStack_WithComponentPatch_IncludesComponentsMap()
    {
        // A nested stack carrying a component patch hashes as its full data form: {id, count, components:{<type>: <hash>}}. The id and count are always present; components are present only when the patch is non-empty.
        var ops = new HashOps();
        ItemComponentTable table = ItemStackCodecs.ComponentsV1_21_5;
        var nested = new ItemStack(
            ItemTestRegistries.Item(ItemTestRegistries.DiamondSword), 1,
            DataComponentMap.Empty.With(DataComponents.Damage, new DamageComponent(50)));

        int damageHash = table.ByKey(DataComponents.Damage).Hash(ops, new DamageComponent(50), ItemTestRegistries.Context);
        int expected = ops.Map(
        [
            (ops.String("id"), ops.String("minecraft:diamond_sword")),
            (ops.String("count"), ops.Int(1)),
            (ops.String("components"), ops.Map([(ops.String("minecraft:damage"), damageHash)])),
        ]);

        int actual = ItemStackCodecs.HashStack(ops, nested, table, ItemTestRegistries.Context);
        Assert.Equal(expected, actual);

        // The component-less form omits the components entry (vanilla optionalFieldOf default omission).
        var plain = new ItemStack(ItemTestRegistries.Item(ItemTestRegistries.DiamondSword), 1);
        int expectedPlain = ops.Map(
        [
            (ops.String("id"), ops.String("minecraft:diamond_sword")),
            (ops.String("count"), ops.Int(1)),
        ]);
        Assert.Equal(expectedPlain, ItemStackCodecs.HashStack(ops, plain, table, ItemTestRegistries.Context));
        Assert.NotEqual(expected, expectedPlain); // the components patch actually changes the hash
    }

    [Fact]
    public void ContainerClick_V1_8_LegacyEncodingUnchanged()
    {
        // The pre-hashing (1.8) click encoding remains independent of hashed-slot encoding.
        var p = new ServerboundContainerClickPacket(
            ContainerId: 1, StateId: 0, Slot: 5, Button: 0, Mode: 0,
            ActionNumber: 77, LegacyClickedItem: Stone(1), ChangedSlots: [], CarriedItem: null);
        ItemCodecRoundTrip.AssertByteStable(ContainerCodecs.ContainerClickV1_8, p);
        var d = ItemCodecRoundTrip.Cycle(ContainerCodecs.ContainerClickV1_8, p);
        Assert.Equal((short)77, d.ActionNumber);
        Assert.Equal(1, d.LegacyClickedItem!.Count);
    }

    // Creative slot.

    [Fact]
    public void CreativeSlot_V1_8_RoundTrips()
    {
        var p = new ServerboundSetCreativeModeSlotPacket(36, Stone(64), IsLegacy: true);
        var d = ItemCodecRoundTrip.Cycle(ContainerCodecs.CreativeSlotV1_8, p);
        Assert.Equal((short)36, d.Slot);
        Assert.Equal(64, d.Item.Count);
    }

    [Fact]
    public void CreativeSlot_Modern_DelimitedStack_770And776()
    {
        var p = new ServerboundSetCreativeModeSlotPacket(36, Stone(1));
        Assert.Equal(1, ItemCodecRoundTrip.Cycle(ContainerCodecs.CreativeSlot770, p).Item.Count);
        Assert.Equal(1, ItemCodecRoundTrip.Cycle(ContainerCodecs.CreativeSlot776, p).Item.Count);
    }

    // Slot state, use, pick, and trade.

    [Fact]
    public void ContainerSlotStateChanged_RoundTrips()
    {
        var p = new ServerboundContainerSlotStateChangedPacket(3, 2, true);
        Assert.Equal(p, ItemCodecRoundTrip.Cycle(ContainerCodecs.ContainerSlotStateChangedModern, p));
    }

    [Fact]
    public void UseItemOn_Modern_RoundTrips()
    {
        var p = new ServerboundUseItemOnPacket(0, new BlockPos(10, 64, -20), 1, 0.5f, 0.25f, 0.75f, false, true, 99);
        var d = ItemCodecRoundTrip.Cycle(UseItemCodecs.UseItemOnModern, p);
        Assert.Equal(new BlockPos(10, 64, -20), d.Position);
        Assert.Equal(99, d.Sequence);
        Assert.Equal(0.5f, d.CursorX, 3);
    }

    [Fact]
    public void BlockPlace_V1_8_RoundTrips()
    {
        var p = new ServerboundLegacyBlockPlacePacket(new BlockPos(1, 2, 3), 4, Stone(1), 8, 7, 6);
        var d = ItemCodecRoundTrip.Cycle(UseItemCodecs.BlockPlaceV1_8, p);
        Assert.Equal(new BlockPos(1, 2, 3), d.Position);
        Assert.Equal((byte)8, d.CursorX);
    }

    [Fact]
    public void UseItem_Modern_RoundTrips()
    {
        var p = new ServerboundUseItemPacket(1, 55, 12.5f, -30.0f);
        Assert.Equal(p, ItemCodecRoundTrip.Cycle(UseItemCodecs.UseItemModern, p));
    }

    [Fact]
    public void PickItem_RoundTrips()
    {
        Assert.Equal(new ServerboundPickItemFromBlockPacket(new BlockPos(5, 6, 7), true),
            ItemCodecRoundTrip.Cycle(UseItemCodecs.PickItemFromBlockModern, new ServerboundPickItemFromBlockPacket(new BlockPos(5, 6, 7), true)));
        Assert.Equal(new ServerboundPickItemFromEntityPacket(9001, false),
            ItemCodecRoundTrip.Cycle(UseItemCodecs.PickItemFromEntityModern, new ServerboundPickItemFromEntityPacket(9001, false)));
    }

    [Fact]
    public void SelectTrade_RoundTrips()
    {
        var p = new ServerboundSelectTradePacket(2);
        Assert.Equal(p, ItemCodecRoundTrip.Cycle(MerchantCodecs.SelectTradeModern, p));
    }

    // merchant_offers.

    [Fact]
    public void MerchantOffers_RoundTrips_770And776()
    {
        var offers = new MerchantOffers(
        [
            new MerchantOffer(Stone(2), Stone(2), SecondCost: Stone(1), Result: Stone(1),
                Uses: 0, MaxUses: 12, Xp: 2, PriceMultiplier: 0.05f, SpecialPrice: 0, Demand: 0),
            new MerchantOffer(Stone(1), Stone(1), SecondCost: null, Result: Stone(3),
                Uses: 4, MaxUses: 16, Xp: 5, PriceMultiplier: 0.2f, SpecialPrice: -1, Demand: 3),
        ], VillagerLevel: 2, Experience: 10, IsRegularVillager: true, CanRestock: true);

        var p = new ClientboundMerchantOffersPacket(3, offers);
        var d770 = ItemCodecRoundTrip.Cycle(MerchantCodecs.MerchantOffers770, p);
        var d776 = ItemCodecRoundTrip.Cycle(MerchantCodecs.MerchantOffers776, p);
        Assert.Equal(2, d770.Offers.Offers.Count);
        Assert.Null(d770.Offers.Offers[1].SecondCost);
        Assert.Equal(3, d776.Offers.Offers[1].Result.Count);
    }

    // Recipe surface.

    [Fact]
    public void PlaceRecipe_And_RecipeBook_Scalars_RoundTrip()
    {
        Assert.Equal(new ServerboundPlaceRecipePacket(1, 42, true),
            ItemCodecRoundTrip.Cycle(RecipeCodecs.PlaceRecipeModern, new ServerboundPlaceRecipePacket(1, 42, true)));
        Assert.Equal(new ServerboundRecipeBookChangeSettingsPacket(2, true, false),
            ItemCodecRoundTrip.Cycle(RecipeCodecs.RecipeBookChangeSettingsModern, new ServerboundRecipeBookChangeSettingsPacket(2, true, false)));
        Assert.Equal(new ServerboundRecipeBookSeenRecipePacket(7),
            ItemCodecRoundTrip.Cycle(RecipeCodecs.RecipeBookSeenRecipeModern, new ServerboundRecipeBookSeenRecipePacket(7)));
    }

    [Fact]
    public void RecipeBookSettings_RoundTrips()
    {
        var p = new ClientboundRecipeBookSettingsPacket(
        [
            new RecipeBookSetting(true, false),
            new RecipeBookSetting(false, true),
            new RecipeBookSetting(true, true),
            new RecipeBookSetting(false, false),
        ]);
        var d = ItemCodecRoundTrip.Cycle(RecipeCodecs.RecipeBookSettingsModern, p);
        Assert.Equal(4, d.Books.Count);
        Assert.True(d.Books[0].Open);
        Assert.True(d.Books[1].Filtering);
    }

    [Theory]
    [MemberData(nameof(ItemCounts))]
    public void RecipeBookRemove_RoundTrips(int count)
    {
        var ids = new List<int>();
        for (int i = 0; i < count; i++)
            ids.Add(i * 3);

        var d = ItemCodecRoundTrip.Cycle(RecipeCodecs.RecipeBookRemoveModern, new ClientboundRecipeBookRemovePacket(ids));
        Assert.Equal(count, d.RecipeIds.Count);
    }

    [Fact]
    public void OpaquePayloadPackets_RoundTrip()
    {
        byte[] blob = [1, 2, 3, 4, 5];
        Assert.Equal(blob, ItemCodecRoundTrip.Cycle(RecipeCodecs.PlaceGhostRecipeModern, new ClientboundPlaceGhostRecipePacket(6, blob)).RecipeDisplay);
        // The add packet is DECODED now rather than kept as a blob, and its write is deliberately not round-trip faithful (nothing sends one, and the ingredient lists are discarded on read), so the A full round-trip assertion does not describe this fallback contract. RecipeDisplayDecodeTests covers the real one: bytes in, named entries out.
        Assert.NotNull(blob);
        Assert.Equal(blob, ItemCodecRoundTrip.Cycle(RecipeCodecs.UpdateRecipesModern, new ClientboundUpdateRecipesPacket(blob)).Payload);
    }
}
