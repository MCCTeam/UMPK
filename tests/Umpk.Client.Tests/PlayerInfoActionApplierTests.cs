using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Players;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Tab-list applier tests across the two player-info eras. The legacy single <c>player_info</c> packet (protocols 47-760) and the modern <c>player_info_update</c> / <c>player_info_remove</c> pair (761+) must land on exactly the same <see cref="TabList"/> state, so a consumer reading the tab list sees one model regardless of the server version.</summary>
public sealed class PlayerInfoActionApplierTests
{
    private static readonly Guid PlayerId = new("11111111-2222-3333-4444-555555555555");

    // A 1.9-era version exercising the legacy tab-list codec and applier.
    private static JavaVersion LegacyVersion => JavaVersions.V1_9;

    private static JavaVersion ModernVersion => JavaVersions.V1_21_5;

    private static TabListEntry Single(ApplierHarness harness) => Assert.Single(harness.State.TabList.Entries);

    /// <summary>The core equivalence: ADD -> UPDATE_LATENCY -> UPDATE_DISPLAY_NAME -> REMOVE on the legacy wire must walk the tab list through the same states as the equivalent modern packet sequence.</summary>
    [Fact]
    public async Task Legacy_AddLatencyDisplayNameRemove_MatchesModernSequence()
    {
        var legacy = new ApplierHarness(LegacyVersion);
        var modern = new ApplierHarness(ModernVersion);

        // 1. Add.
        await legacy.ApplyAsync(new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.AddPlayer,
            [new LegacyPlayerListEntry(
                PlayerId,
                "Steve",
                [new GameProfileProperty("textures", "blob", "sig")],
                (int)GameMode.Creative,
                55,
                Component.Text("StevePvP"))]));
        await modern.ApplyAsync(new ClientboundPlayerInfoUpdatePacket(
            PlayerInfoActions.AddPlayer | PlayerInfoActions.UpdateGameMode
                | PlayerInfoActions.UpdateLatency | PlayerInfoActions.UpdateDisplayName,
            [new PlayerInfoEntry(
                PlayerId,
                "Steve",
                [new GameProfileProperty("textures", "blob", "sig")],
                HasChatSession: false,
                ChatSession: null,
                GameMode.Creative,
                Listed: true,
                Latency: 55,
                Component.Text("StevePvP"),
                ListOrder: 0,
                ShowHat: true)]));

        AssertSameEntry(legacy, modern);
        TabListEntry added = Single(legacy);
        Assert.Equal("Steve", added.Profile.Name);
        Assert.Equal(GameMode.Creative, added.GameMode);
        Assert.Equal(55, added.Latency);
        Assert.Equal("StevePvP", added.DisplayName!.ToPlainText());

        // 2. Update latency.
        await legacy.ApplyAsync(new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.UpdateLatency,
            [new LegacyPlayerListEntry(PlayerId, null, null, 0, 137, null)]));
        await modern.ApplyAsync(new ClientboundPlayerInfoUpdatePacket(
            PlayerInfoActions.UpdateLatency,
            [ModernFields(PlayerId) with { Latency = 137 }]));

        AssertSameEntry(legacy, modern);
        Assert.Equal(137, Single(legacy).Latency);
        // The update must not clobber the fields it does not carry.
        Assert.Equal(GameMode.Creative, Single(legacy).GameMode);
        Assert.Equal("Steve", Single(legacy).Profile.Name);

        // 3. Update display name.
        await legacy.ApplyAsync(new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.UpdateDisplayName,
            [new LegacyPlayerListEntry(PlayerId, null, null, 0, 0, Component.Text("[Admin] Steve"))]));
        await modern.ApplyAsync(new ClientboundPlayerInfoUpdatePacket(
            PlayerInfoActions.UpdateDisplayName,
            [ModernFields(PlayerId) with { DisplayName = Component.Text("[Admin] Steve") }]));

        AssertSameEntry(legacy, modern);
        Assert.Equal("[Admin] Steve", Single(legacy).DisplayName!.ToPlainText());
        Assert.Equal(137, Single(legacy).Latency);

        // 4. Remove.
        await legacy.ApplyAsync(new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.RemovePlayer,
            [new LegacyPlayerListEntry(PlayerId, null, null, 0, 0, null)]));
        await modern.ApplyAsync(new ClientboundPlayerInfoRemovePacket([PlayerId]));

        Assert.Empty(legacy.State.TabList.Entries);
        Assert.Empty(modern.State.TabList.Entries);
        Assert.False(legacy.State.TabList.TryGet(PlayerId, out _));
    }

    [Fact]
    public async Task Legacy_Add_PopulatesTabList_WhichWasPreviouslyEmpty()
    {
        var harness = new ApplierHarness(LegacyVersion);

        Assert.Empty(harness.State.TabList.Entries);

        await harness.ApplyAsync(new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.AddPlayer,
            [
                new LegacyPlayerListEntry(Guid.NewGuid(), "Alice", [], 0, 12, null),
                new LegacyPlayerListEntry(Guid.NewGuid(), "Bob", [], 1, 34, null),
            ]));

        Assert.Equal(2, harness.State.TabList.Count);
        Assert.Contains(harness.State.TabList.Entries, e => e.Profile.Name == "Alice");
        Assert.Contains(harness.State.TabList.Entries, e => e.Profile.Name == "Bob");
    }

    [Fact]
    public async Task Legacy_Add_CarriesSkinProperties_OntoTheProfile()
    {
        var harness = new ApplierHarness(LegacyVersion);

        await harness.ApplyAsync(new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.AddPlayer,
            [new LegacyPlayerListEntry(
                PlayerId, "Steve", [new GameProfileProperty("textures", "base64blob", "mojangsig")], 0, 0, null)]));

        ProfileProperty property = Assert.Single(Single(harness).Profile.Properties);
        Assert.Equal("textures", property.Name);
        Assert.Equal("base64blob", property.Value);
        Assert.Equal("mojangsig", property.Signature);
    }

    [Fact]
    public async Task Legacy_UpdateGameMode_Applies()
    {
        var harness = new ApplierHarness(LegacyVersion);
        await harness.ApplyAsync(new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.AddPlayer,
            [new LegacyPlayerListEntry(PlayerId, "Steve", [], (int)GameMode.Survival, 0, null)]));

        await harness.ApplyAsync(new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.UpdateGameMode,
            [new LegacyPlayerListEntry(PlayerId, null, null, (int)GameMode.Spectator, 0, null)]));

        Assert.Equal(GameMode.Spectator, Single(harness).GameMode);
        Assert.Equal("Steve", Single(harness).Profile.Name);
    }

    [Fact]
    public async Task Legacy_UpdateDisplayName_WithNull_ClearsTheOverride()
    {
        var harness = new ApplierHarness(LegacyVersion);
        await harness.ApplyAsync(new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.AddPlayer,
            [new LegacyPlayerListEntry(PlayerId, "Steve", [], 0, 0, Component.Text("[VIP] Steve"))]));
        Assert.NotNull(Single(harness).DisplayName);

        await harness.ApplyAsync(new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.UpdateDisplayName,
            [new LegacyPlayerListEntry(PlayerId, null, null, 0, 0, null)]));

        Assert.Null(Single(harness).DisplayName);
        Assert.Equal("Steve", Single(harness).Profile.Name);
    }

    /// <summary>The self entry: the local player's own latency arrives through the same player-info path as every other player, so a legacy-era session can read its own ping off the tab list.</summary>
    [Fact]
    public async Task Legacy_SelfEntryLatency_IsReadable()
    {
        var harness = new ApplierHarness(LegacyVersion);
        await harness.ApplyAsync(new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.AddPlayer,
            [new LegacyPlayerListEntry(PlayerId, "Self", [], 0, 0, null)]));

        await harness.ApplyAsync(new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.UpdateLatency,
            [new LegacyPlayerListEntry(PlayerId, null, null, 0, 91, null)]));

        Assert.True(harness.State.TabList.TryGet(PlayerId, out TabListEntry? self));
        Assert.Equal(91, self.Latency);
    }

    /// <summary>The header/footer packet keeps working on the legacy era and is independent of the entries.</summary>
    [Fact]
    public async Task Legacy_HeaderFooter_StillApplies_AndSurvivesEntryChurn()
    {
        var harness = new ApplierHarness(LegacyVersion);
        bool raised = false;
        harness.Events.Subscribe<TabListHeaderFooterChanged>(_ => raised = true);

        await harness.ApplyAsync(new ClientboundTabListPacket(Component.Text("Header"), Component.Text("Footer")));

        Assert.True(raised);
        Assert.Equal("Header", harness.State.TabList.Header!.ToPlainText());
        Assert.Equal("Footer", harness.State.TabList.Footer!.ToPlainText());

        await harness.ApplyAsync(new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.AddPlayer,
            [new LegacyPlayerListEntry(PlayerId, "Steve", [], 0, 0, null)]));
        await harness.ApplyAsync(new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.RemovePlayer,
            [new LegacyPlayerListEntry(PlayerId, null, null, 0, 0, null)]));

        Assert.Equal("Header", harness.State.TabList.Header!.ToPlainText());
        Assert.Equal("Footer", harness.State.TabList.Footer!.ToPlainText());
    }

    /// <summary>The header/footer half of the tab list, across the bands whose <c>minecraft:tab_list</c> wiring was wrong: it was a marker on 107-404 and bound to the network-NBT codec on 477-763, so the header and footer never reached the state on any of those versions. Every era decodes into the same <see cref="ClientboundTabListPacket"/> record, so a consumer must see one surface: this pins that each era lands identical <c>TabList.Header</c>/<c>TabList.Footer</c> state and raises the same event as the modern path. The values are non-empty so "unset" cannot pass for "set".</summary>
    [Theory]
    [InlineData(107)]
    [InlineData(393)]
    [InlineData(735)]
    [InlineData(763)]
    [InlineData(765)]
    public async Task HeaderFooter_LandsSameStateOnEveryWireLayout(int protocol)
    {
        JavaVersion era = protocol switch
        {
            107 => JavaVersions.V1_9,
            393 => JavaVersions.V1_13,
            735 => JavaVersions.V1_16,
            763 => JavaVersions.V1_20,
            _ => JavaVersions.V1_20_3,
        };

        var legacy = new ApplierHarness(era);
        var modern = new ApplierHarness(ModernVersion);

        bool published = false;
        legacy.Events.Subscribe<TabListHeaderFooterChanged>(_ => published = true);

        var packet = new ClientboundTabListPacket(Component.Text("Welcome"), Component.Text("42 online"));
        await legacy.ApplyAsync(packet);
        await modern.ApplyAsync(packet);

        Assert.Equal("Welcome", legacy.State.TabList.Header!.ToPlainText());
        Assert.Equal("42 online", legacy.State.TabList.Footer!.ToPlainText());
        Assert.Equal(modern.State.TabList.Header, legacy.State.TabList.Header);
        Assert.Equal(modern.State.TabList.Footer, legacy.State.TabList.Footer);
        Assert.True(published);
    }

    [Fact]
    public async Task Legacy_RemoveOfUnknownPlayer_IsANoOp()
    {
        var harness = new ApplierHarness(LegacyVersion);

        await harness.ApplyAsync(new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.RemovePlayer,
            [new LegacyPlayerListEntry(Guid.NewGuid(), null, null, 0, 0, null)]));

        Assert.Empty(harness.State.TabList.Entries);
    }

    /// <summary>The modern path is unchanged: the 1.19.3+ split pair still drives every field it always did, including the fields the legacy wire has no notion of (listed, list order, show hat).</summary>
    [Fact]
    public async Task Modern_PlayerInfoUpdate_Path_Unchanged()
    {
        var harness = new ApplierHarness(ModernVersion);

        await harness.ApplyAsync(new ClientboundPlayerInfoUpdatePacket(
            PlayerInfoActions.AddPlayer | PlayerInfoActions.UpdateGameMode | PlayerInfoActions.UpdateListed
                | PlayerInfoActions.UpdateLatency | PlayerInfoActions.UpdateDisplayName
                | PlayerInfoActions.UpdateListOrder | PlayerInfoActions.UpdateHat,
            [new PlayerInfoEntry(
                PlayerId, "Steve", [new GameProfileProperty("textures", "blob", "sig")],
                HasChatSession: false, ChatSession: null, GameMode.Adventure, Listed: false, Latency: 21,
                Component.Text("Renamed"), ListOrder: 5, ShowHat: false)]));

        TabListEntry entry = Single(harness);
        Assert.Equal("Steve", entry.Profile.Name);
        Assert.Equal(GameMode.Adventure, entry.GameMode);
        Assert.False(entry.Listed);
        Assert.Equal(21, entry.Latency);
        Assert.Equal("Renamed", entry.DisplayName!.ToPlainText());
        Assert.Equal(5, entry.ListOrder);
        Assert.False(entry.ShowHat);

        // A later partial update touches only the actions it declares.
        await harness.ApplyAsync(new ClientboundPlayerInfoUpdatePacket(
            PlayerInfoActions.UpdateLatency,
            [ModernFields(PlayerId) with { Latency = 999 }]));

        entry = Single(harness);
        Assert.Equal(999, entry.Latency);
        Assert.Equal(GameMode.Adventure, entry.GameMode);
        Assert.Equal(5, entry.ListOrder);
        Assert.False(entry.ShowHat);
        Assert.False(entry.Listed);

        await harness.ApplyAsync(new ClientboundPlayerInfoRemovePacket([PlayerId]));
        Assert.Empty(harness.State.TabList.Entries);
    }

    /// <summary>The legacy era leaves the 1.19.3+ only fields at their defaults, which is what the vanilla pre-1.19.3 client renders: every entry listed, no list-order priority, hat shown.</summary>
    [Fact]
    public async Task Legacy_Entry_UsesModernDefaults_ForFieldsTheWireLacks()
    {
        var harness = new ApplierHarness(LegacyVersion);

        await harness.ApplyAsync(new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.AddPlayer,
            [new LegacyPlayerListEntry(PlayerId, "Steve", [], 0, 0, null)]));

        TabListEntry entry = Single(harness);
        Assert.True(entry.Listed);
        Assert.Equal(0, entry.ListOrder);
        Assert.True(entry.ShowHat);
    }

    // A modern entry carrying only the uuid; the `with` expressions above set the one field under test, mirroring how a real partial player-info-update leaves unnamed fields at their defaults.
    private static PlayerInfoEntry ModernFields(Guid id) => new(
        id, null, null, HasChatSession: false, ChatSession: null, GameMode.Undefined,
        Listed: false, Latency: 0, DisplayName: null, ListOrder: 0, ShowHat: false);

    private static void AssertSameEntry(ApplierHarness legacy, ApplierHarness modern)
    {
        Assert.Equal(modern.State.TabList.Count, legacy.State.TabList.Count);
        foreach (TabListEntry expected in modern.State.TabList.Entries)
        {
            Assert.True(legacy.State.TabList.TryGet(expected.Uuid, out TabListEntry? actual));
            Assert.Equal(expected.Profile.Name, actual.Profile.Name);
            Assert.Equal(expected.Profile.Id, actual.Profile.Id);
            Assert.Equal(expected.GameMode, actual.GameMode);
            Assert.Equal(expected.Latency, actual.Latency);
            Assert.Equal(expected.DisplayName?.ToPlainText(), actual.DisplayName?.ToPlainText());
            Assert.Equal(expected.Listed, actual.Listed);
            Assert.Equal(
                expected.Profile.Properties.Select(static p => (p.Name, p.Value, p.Signature)),
                actual.Profile.Properties.Select(static p => (p.Name, p.Value, p.Signature)));
        }
    }
}
