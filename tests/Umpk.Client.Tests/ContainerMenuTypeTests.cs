using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Inventory;
using Umpk.Game.Registries;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>The surface a consumer reads to learn WHAT KIND of container is open, instead of inferring it from the slot count and the presence of properties (2 slots = enchanting, 5 = hopper, 10 = crafting, ...). Both eras resolve into one handle: the numeric menu id from 1.14, the window-type string before that.</summary>
public sealed class ContainerMenuTypeTests
{
    private static ApplierHarness Harness(JavaVersion version)
    {
        var harness = new ApplierHarness(version);
        harness.State.Registries = JavaGameData.Registries(version.Version.Protocol);
        return harness;
    }

    [Fact]
    public async Task ModernOpenScreen_ExposesResolvedMenuType()
    {
        ApplierHarness harness = Harness(JavaVersions.V1_21_5);

        // Menu id 13 is minecraft:furnace on 1.21.5. The heuristic this replaces would have had to guess "furnace" from a 3-slot window with properties.
        Registry<MenuTypeDefinition> menus = harness.State.Registries!.MenuTypes;
        Assert.True(menus.TryGet(Identifier.Minecraft("furnace"), out RegistryEntry<MenuTypeDefinition> furnace));

        await harness.ApplyAsync(new ClientboundOpenScreenPacket(
            ContainerId: 3,
            MenuTypeId: furnace.NetworkId,
            Title: Component.Text("Furnace"),
            LegacyType: null,
            LegacySlotCount: 0,
            LegacyEntityId: null));

        InventoryState inv = harness.State.Inventory;
        Assert.NotNull(inv.OpenContainerMenuType);
        Assert.Equal(Identifier.Minecraft("furnace"), inv.OpenContainerMenuType!.Value.Id);
        Assert.Equal(furnace.NetworkId, inv.OpenContainerMenuTypeId);
        Assert.Null(inv.OpenContainerLegacyWindowType);
    }

    [Theory]
    [InlineData(47, "minecraft:chest", "chest")]
    [InlineData(107, "minecraft:furnace", "furnace")]
    [InlineData(340, "minecraft:brewing_stand", "brewing_stand")]
    [InlineData(404, "minecraft:enchanting_table", "enchanting_table")]
    public async Task LegacyOpenScreen_ResolvesMenuTypeFromWindowTypeString(int protocol, string windowType, string expected)
    {
        // Before 1.14 there is no numeric menu id at all, so the string is the only key. These protocols additionally decoded nothing here until the open_screen marker was removed.
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        ApplierHarness harness = Harness(version);

        await harness.ApplyAsync(new ClientboundOpenScreenPacket(
            ContainerId: 5,
            MenuTypeId: -1,
            Title: Component.Text("Container"),
            LegacyType: windowType,
            LegacySlotCount: 27,
            LegacyEntityId: null));

        InventoryState inv = harness.State.Inventory;
        Assert.NotNull(inv.OpenContainerMenuType);
        Assert.Equal(Identifier.Minecraft(expected), inv.OpenContainerMenuType!.Value.Id);
        Assert.Equal(windowType, inv.OpenContainerLegacyWindowType);
        Assert.Equal(-1, inv.OpenContainerMenuTypeId);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(340)]
    [InlineData(404)]
    public async Task LegacyHorseWindow_ResolvesDespiteNotBeingAnIdentifier(int protocol)
    {
        // "EntityHorse" cannot be parsed as an identifier, so it resolves by the wire string the definition carries. Without that fallback the horse window would be the one kind that never resolves.
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        ApplierHarness harness = Harness(version);

        await harness.ApplyAsync(new ClientboundOpenScreenPacket(
            ContainerId: 6,
            MenuTypeId: -1,
            Title: Component.Text("Horse"),
            LegacyType: "EntityHorse",
            LegacySlotCount: 17,
            LegacyEntityId: 4242));

        InventoryState inv = harness.State.Inventory;
        Assert.NotNull(inv.OpenContainerMenuType);
        Assert.Equal(new Identifier("umpk", "entity_horse"), inv.OpenContainerMenuType!.Value.Id);
        Assert.Equal("EntityHorse", inv.OpenContainerLegacyWindowType);
    }

    [Fact]
    public async Task UnknownWindowType_LeavesTypeNullButKeepsTheRawString()
    {
        // A window kind we cannot name must resolve to null rather than to some nearby entry: naming a container wrongly is worse than admitting it is unknown, and the raw string stays available.
        ApplierHarness harness = Harness(JavaVersions.V1_8);

        await harness.ApplyAsync(new ClientboundOpenScreenPacket(
            ContainerId: 7,
            MenuTypeId: -1,
            Title: Component.Text("Custom"),
            LegacyType: "someplugin:custom_window",
            LegacySlotCount: 9,
            LegacyEntityId: null));

        InventoryState inv = harness.State.Inventory;
        Assert.Null(inv.OpenContainerMenuType);
        Assert.Equal("someplugin:custom_window", inv.OpenContainerLegacyWindowType);
        Assert.True(inv.HasOpenContainer);
    }

    [Fact]
    public async Task CloseContainer_ClearsTheResolvedMenuType()
    {
        ApplierHarness harness = Harness(JavaVersions.V1_21_5);
        Registry<MenuTypeDefinition> menus = harness.State.Registries!.MenuTypes;
        Assert.True(menus.TryGet(Identifier.Minecraft("hopper"), out RegistryEntry<MenuTypeDefinition> hopper));

        await harness.ApplyAsync(new ClientboundOpenScreenPacket(
            8, hopper.NetworkId, Component.Text("Hopper"), null, 0, null));
        Assert.NotNull(harness.State.Inventory.OpenContainerMenuType);

        await harness.ApplyAsync(new ClientboundContainerClosePacket(8));

        InventoryState inv = harness.State.Inventory;
        Assert.Null(inv.OpenContainerMenuType);
        Assert.Null(inv.OpenContainerLegacyWindowType);
        Assert.Equal(-1, inv.OpenContainerMenuTypeId);
    }
}
