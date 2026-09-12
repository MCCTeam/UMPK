using Umpk;
using Umpk.Client;
using Umpk.Client.Snapshots;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Game.Registries;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests.Snapshots;

/// <summary><see cref="PlayerInventorySnapshot"/>, <see cref="OpenContainerSnapshot"/> and <see cref="RecipeBookSnapshot"/> form the off-loop inventory read-model. Item stacks and the container title retain their domain types instead of collapsing to strings.</summary>
public sealed class InventorySnapshotTests
{
    // PlayerInventorySnapshot.Project

    [Fact]
    public void PlayerInventory_ProjectsAllFortySixSlots()
    {
        var state = new ClientState(new ClientFeatures().Normalized());
        var items = new ItemStack[46];
        for (int i = 0; i < items.Length; i++)
            items[i] = i % 2 == 0 ? TestItems.Stone(i + 1) : ItemStack.Empty;

        state.Inventory.ReplaceContents(InventoryState.PlayerWindowId, items);

        PlayerInventorySnapshot snapshot = PlayerInventorySnapshot.Project(state);

        Assert.Equal(46, snapshot.Slots.Count);
        for (int i = 0; i < items.Length; i++)
            Assert.Equal(items[i], snapshot.Slots[i]);

    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(8)]
    public void PlayerInventory_HeldItem_ResolvesHotbarIndex(int heldSlot)
    {
        var state = new ClientState(new ClientFeatures().Normalized());
        var items = new ItemStack[46];
        Array.Fill(items, ItemStack.Empty);
        ItemStack sword = TestItems.DiamondSword();
        items[36 + heldSlot] = sword;
        state.Inventory.ReplaceContents(InventoryState.PlayerWindowId, items);
        state.Self.HeldSlot = heldSlot;

        PlayerInventorySnapshot snapshot = PlayerInventorySnapshot.Project(state);

        Assert.Equal(heldSlot, snapshot.HeldSlot);
        Assert.Equal(sword, snapshot.HeldItem);
    }

    [Fact]
    public void PlayerInventory_HeldItem_IsEmpty_WhenIndexOutOfRange()
    {
        var state = new ClientState(new ClientFeatures().Normalized());

        // A legacy player window (5 slots: no hotbar at all) puts 36 + HeldSlot past the end.
        state.Inventory.ReplaceContents(InventoryState.PlayerWindowId, [ItemStack.Empty, ItemStack.Empty, ItemStack.Empty, ItemStack.Empty, ItemStack.Empty]);
        state.Self.HeldSlot = 0;

        PlayerInventorySnapshot snapshot = PlayerInventorySnapshot.Project(state);

        Assert.Equal(ItemStack.Empty, snapshot.HeldItem);
    }

    [Fact]
    public void PlayerInventory_CarriesAuthoritativeStateId()
    {
        var state = new ClientState(new ClientFeatures().Normalized());
        state.Inventory.StateId = 42;

        PlayerInventorySnapshot snapshot = PlayerInventorySnapshot.Project(state);

        Assert.Equal(42, snapshot.StateId);
    }

    // OpenContainerSnapshot.Project

    [Fact]
    public void OpenContainer_IsNull_WhenOnlyPlayerInventoryIsOpen()
    {
        var state = new ClientState(new ClientFeatures().Normalized());

        Assert.Null(OpenContainerSnapshot.Project(state));
    }

    [Fact]
    public void OpenContainer_ProjectsSlotsPropertiesAndCursor()
    {
        var state = new ClientState(new ClientFeatures().Normalized());
        state.Inventory.OpenContainer(3, 9, title: Component.Text("Chest"));
        ItemStack stone = TestItems.Stone(5);
        state.Inventory.SetSlot(3, 0, stone);
        state.Inventory.SetProperty(0, 200);
        state.Inventory.Cursor = TestItems.DiamondSword();
        state.Inventory.StateId = 7;

        OpenContainerSnapshot? snapshot = OpenContainerSnapshot.Project(state);

        Assert.NotNull(snapshot);
        Assert.Equal(3, snapshot!.WindowId);
        Assert.Equal(9, snapshot.Slots.Count);
        Assert.Equal(stone, snapshot.Slots[0]);
        Assert.Equal(200, snapshot.Properties[0]);
        Assert.Equal(TestItems.DiamondSword(), snapshot.Cursor);
        Assert.Equal(7, snapshot.StateId);
    }

    [Fact]
    public void OpenContainer_Title_StaysAsComponent()
    {
        var state = new ClientState(new ClientFeatures().Normalized());
        Component title = Component.Text("A Rich Title");
        state.Inventory.OpenContainer(1, 9, title: title);

        OpenContainerSnapshot? snapshot = OpenContainerSnapshot.Project(state);

        Assert.NotNull(snapshot);
        Assert.Equal(title, snapshot!.Title);
    }

    [Theory]
    [InlineData(47, "minecraft:chest", "chest")] // 1.8: pre-menu-registry era, named by the legacy window-type string.
    [InlineData(763, "furnace", "furnace")] // 1.20.1: numeric minecraft:menu registry id era.
    public async Task OpenContainer_ResolvesSemanticMenuType_OnBothWireLayouts(int protocol, string sourceName, string expectedKind)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var harness = new ApplierHarness(version!);
        harness.State.Registries = JavaGameData.Registries(protocol);

        if (protocol < 393)
            await harness.ApplyAsync(new ClientboundOpenScreenPacket(
                ContainerId: 4, MenuTypeId: -1, Title: Component.Text("Container"),
                LegacyType: sourceName, LegacySlotCount: 27, LegacyEntityId: null));

        else
        {
            Registry<MenuTypeDefinition> menus = harness.State.Registries!.MenuTypes;
            Assert.True(menus.TryGet(Identifier.Minecraft(sourceName), out RegistryEntry<MenuTypeDefinition> entry));
            await harness.ApplyAsync(new ClientboundOpenScreenPacket(
                ContainerId: 4, MenuTypeId: entry.NetworkId, Title: Component.Text("Container"),
                LegacyType: null, LegacySlotCount: 0, LegacyEntityId: null));
        }

        OpenContainerSnapshot? snapshot = OpenContainerSnapshot.Project(harness.State);

        Assert.NotNull(snapshot);
        Assert.Equal(Identifier.Minecraft(expectedKind), snapshot!.MenuType);
    }

    [Fact]
    public async Task OpenContainer_CarriesLegacyWindowType_WhenTheMenuCannotBeNamed()
    {
        var harness = new ApplierHarness(JavaVersions.V1_8);
        harness.State.Registries = JavaGameData.Registries(JavaVersions.V1_8.Version.Protocol);

        await harness.ApplyAsync(new ClientboundOpenScreenPacket(
            ContainerId: 7, MenuTypeId: -1, Title: Component.Text("Custom"),
            LegacyType: "someplugin:custom_window", LegacySlotCount: 9, LegacyEntityId: null));

        OpenContainerSnapshot? snapshot = OpenContainerSnapshot.Project(harness.State);

        Assert.NotNull(snapshot);
        Assert.Null(snapshot!.MenuType);
        Assert.Equal("someplugin:custom_window", snapshot.LegacyWindowType);
    }

    // RecipeBookSnapshot.Project

    [Fact]
    public void RecipeBook_ProjectsBothNamingForms()
    {
        var recipes = new RecipeState();
        var packet = new ClientboundRecipePacket(
            RecipeBookState.Init,
            [new RecipeBookSetting(true, false), new RecipeBookSetting(false, true), new RecipeBookSetting(false, false), new RecipeBookSetting(false, false)],
            [Identifier.Minecraft("torch"), Identifier.Minecraft("furnace")],
            [],
            [10, 20],
            []);

        recipes.ApplyLegacyUnlock(packet);

        RecipeBookSnapshot snapshot = RecipeBookSnapshot.Project(recipes);

        Assert.Contains(Identifier.Minecraft("torch"), snapshot.Recipes);
        Assert.Contains(Identifier.Minecraft("furnace"), snapshot.Recipes);
        Assert.Contains(10, snapshot.RecipeIds);
        Assert.Contains(20, snapshot.RecipeIds);
        Assert.True(snapshot.Books[0].Open);
        Assert.False(snapshot.Books[0].Filtering);
        Assert.True(snapshot.Books[1].Filtering);
        Assert.Equal(recipes.BookRevision, snapshot.Revision);
    }

    [Fact]
    public void RecipeBook_OpaqueAdditions_ReportsUndecodedCount()
    {
        var recipes = new RecipeState();

        // The 1.21.2+ display tree IS decoded now, so OpaqueAdditions counts only what the decoder could not read. Two failed additions, which is the path this snapshot field exists for.
        recipes.ApplyBookAdd([], undecoded: 1, replace: true);
        recipes.ApplyBookAdd([], undecoded: 1, replace: false);

        RecipeBookSnapshot snapshot = RecipeBookSnapshot.Project(recipes);

        Assert.Equal(2, snapshot.OpaqueAdditions);

        // The honesty case this field exists for: the additions are real, but the lists that would name them stay empty, so a caller must read OpaqueAdditions rather than treat the empty lists as "nothing unlocked".
        Assert.Empty(snapshot.Recipes);
        Assert.Empty(snapshot.RecipeIds);
    }
}
