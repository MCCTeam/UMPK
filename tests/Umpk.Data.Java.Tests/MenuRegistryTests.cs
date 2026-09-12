using Umpk.Game.Inventory;
using Umpk.Game.Registries;
using Xunit;

namespace Umpk.Data.Java.Tests;

/// <summary>Pins the runtime <c>minecraft:menu</c> registry that <see cref="JavaGameData"/> builds from the generated per-version menu table.</summary>
/// <remarks>These tests require non-empty values on every band and cover both legacy string keys and modern numeric registry ids.</remarks>
public sealed class MenuRegistryTests
{
    /// <summary>The first protocol (1.14) with a real numeric menu registry.</summary>
    private const int FirstMenuRegistryProtocol = 477;

    public static TheoryData<int> AllProtocols
    {
        get
        {
            var data = new TheoryData<int>();
            foreach (int protocol in JavaVersions.All.Select(v => v.Version.Protocol).Distinct())
                data.Add(protocol);

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AllProtocols))]
    public void EveryProtocol_HasAPopulatedMenuRegistry(int protocol)
    {
        Registry<MenuTypeDefinition> menus = JavaGameData.Registries(protocol).MenuTypes;
        Assert.True(menus.Count > 0, $"protocol {protocol}: menu registry is empty.");
    }

    [Theory]
    // Pre-1.14: string window types. Chest and the generic container exist on the whole band.
    [InlineData(47, "chest")]
    [InlineData(47, "furnace")]
    [InlineData(47, "container")]
    [InlineData(107, "chest")]
    [InlineData(210, "brewing_stand")]
    [InlineData(340, "chest")]
    [InlineData(393, "enchanting_table")]
    [InlineData(404, "chest")]
    // 1.14+: numeric registry ids.
    [InlineData(477, "furnace")]
    [InlineData(477, "generic_9x3")]
    [InlineData(770, "furnace")]
    [InlineData(770, "smithing")]
    [InlineData(776, "generic_9x6")]
    public void KnownMenuKind_ResolvesByIdentifier(int protocol, string name)
    {
        Registry<MenuTypeDefinition> menus = JavaGameData.Registries(protocol).MenuTypes;
        Assert.True(
            menus.TryGet(Identifier.Minecraft(name), out RegistryEntry<MenuTypeDefinition> entry),
            $"protocol {protocol}: minecraft:{name} is not in the menu registry.");
        Assert.Equal(Identifier.Minecraft(name), entry.Id);
    }

    [Theory]
    [InlineData(315)] // 1.11, the version shulker boxes arrive in
    [InlineData(340)] // 1.12.2
    [InlineData(404)] // 1.13.2
    public void ShulkerBox_IsPresentFrom1_11(int protocol)
    {
        Registry<MenuTypeDefinition> menus = JavaGameData.Registries(protocol).MenuTypes;
        Assert.True(menus.TryGet(Identifier.Minecraft("shulker_box"), out _));
    }

    [Theory]
    [InlineData(47)]  // 1.8
    [InlineData(107)] // 1.9
    [InlineData(210)] // 1.10
    public void ShulkerBox_IsAbsentBefore1_11(int protocol)
    {
        // Shulker boxes are 1.11 content. Claiming one on an earlier protocol would invent a window.
        Registry<MenuTypeDefinition> menus = JavaGameData.Registries(protocol).MenuTypes;
        Assert.False(menus.TryGet(Identifier.Minecraft("shulker_box"), out _));
    }

    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(340)]
    [InlineData(404)]
    public void LegacyEntries_CarryTheirWireString(int protocol)
    {
        Registry<MenuTypeDefinition> menus = JavaGameData.Registries(protocol).MenuTypes;

        // Every pre-1.14 entry must name the string open_screen actually carries, because the numeric id is synthetic on that era and resolves nothing.
        foreach (RegistryEntry<MenuTypeDefinition> entry in menus)
            Assert.False(
                string.IsNullOrEmpty(entry.Value.LegacyWindowType),
                $"protocol {protocol}: {entry.Id} has no legacy window type.");

        Assert.True(menus.TryGet(Identifier.Minecraft("chest"), out RegistryEntry<MenuTypeDefinition> chest));
        Assert.Equal("minecraft:chest", chest.Value.LegacyWindowType);

        // The horse window is the one type whose wire spelling is not a well-formed identifier, so it keys under umpk: and keeps "EntityHorse" as its wire string.
        Assert.True(menus.TryGet(new Identifier("umpk", "entity_horse"), out RegistryEntry<MenuTypeDefinition> horse));
        Assert.Equal("EntityHorse", horse.Value.LegacyWindowType);
    }

    [Theory]
    [InlineData(477)]
    [InlineData(770)]
    [InlineData(776)]
    public void ModernEntries_CarryNoLegacyWireString(int protocol)
    {
        // From 1.14 the wire carries the number, so a legacy string here would be a fiction.
        Registry<MenuTypeDefinition> menus = JavaGameData.Registries(protocol).MenuTypes;
        foreach (RegistryEntry<MenuTypeDefinition> entry in menus)
            Assert.Null(entry.Value.LegacyWindowType);

    }

    [Theory]
    [MemberData(nameof(AllProtocols))]
    public void MenuIds_AreDenseFromZero(int protocol)
    {
        // Both eras number densely; pre-1.14 uses an index assigned in sorted order.
        Registry<MenuTypeDefinition> menus = JavaGameData.Registries(protocol).MenuTypes;
        for (int id = 0; id < menus.Count; id++)
            Assert.True(menus.TryGet(id, out _), $"protocol {protocol}: menu id {id} missing.");

    }

    [Fact]
    public void ModernRegistry_MatchesTheKnownVanillaIdOrder()
    {
        // The first registry ids are the six generic chest sizes followed by generic_3x3.
        Registry<MenuTypeDefinition> menus = JavaGameData.Registries(FirstMenuRegistryProtocol).MenuTypes;

        Assert.True(menus.TryGet(0, out RegistryEntry<MenuTypeDefinition> first));
        Assert.Equal(Identifier.Minecraft("generic_9x1"), first.Id);

        Assert.True(menus.TryGet(5, out RegistryEntry<MenuTypeDefinition> sixRows));
        Assert.Equal(Identifier.Minecraft("generic_9x6"), sixRows.Id);

        Assert.True(menus.TryGet(6, out RegistryEntry<MenuTypeDefinition> threeByThree));
        Assert.Equal(Identifier.Minecraft("generic_3x3"), threeByThree.Id);
    }
}
