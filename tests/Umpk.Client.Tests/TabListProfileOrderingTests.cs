using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Players;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>The add action is the only one carrying a name and properties, and the server does not guarantee it reaches the client before the other actions for the same player. On a 1.14.4 server, join-time <c>UPDATE_GAME_MODE</c> may arrive first and create a placeholder. A later <c>ADD_PLAYER</c> must fill its name and properties so tab-list display and entity-profile backfill have complete data.</summary>
public sealed class TabListProfileOrderingTests
{
    private static readonly Guid PlayerId = new("6215dc2d-9c4a-3137-b7d5-06c4b08a2acc");

    private static JavaVersion LegacyVersion => JavaVersions.V1_14_4;

    private static JavaVersion ModernVersion => JavaVersions.V1_21_5;

    [Fact]
    public async Task Legacy_Add_After_An_Update_Installs_The_Real_Profile()
    {
        var harness = new ApplierHarness(LegacyVersion);

        await harness.ApplyAsync(new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.UpdateGameMode,
            [new LegacyPlayerListEntry(PlayerId, null, null, (int)GameMode.Creative, 0, null)]));
        await harness.ApplyAsync(new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.AddPlayer,
            [new LegacyPlayerListEntry(
                PlayerId,
                "u7_b",
                [new GameProfileProperty("textures", "blob", "sig")],
                (int)GameMode.Creative,
                42,
                null)]));

        TabListEntry entry = Assert.Single(harness.State.TabList.Entries);
        Assert.Equal("u7_b", entry.Profile.Name);
        Assert.Equal("textures", Assert.Single(entry.Profile.Properties).Name);
    }

    [Fact]
    public async Task Modern_Add_After_An_Update_Installs_The_Real_Profile()
    {
        var harness = new ApplierHarness(ModernVersion);

        await harness.ApplyAsync(new ClientboundPlayerInfoUpdatePacket(
            PlayerInfoActions.UpdateLatency,
            [Fields(PlayerId) with { Latency = 120 }]));
        await harness.ApplyAsync(new ClientboundPlayerInfoUpdatePacket(
            PlayerInfoActions.AddPlayer,
            [Fields(PlayerId) with
            {
                Name = "u7_b",
                Properties = [new GameProfileProperty("textures", "blob", "sig")],
            }]));

        TabListEntry entry = Assert.Single(harness.State.TabList.Entries);
        Assert.Equal("u7_b", entry.Profile.Name);
        Assert.Equal("textures", Assert.Single(entry.Profile.Properties).Name);
    }

    [Fact]
    public async Task An_Update_That_Lost_The_Race_Is_Not_Discarded_By_The_Add()
    {
        // Replacing the profile means building a new entry, so a field the early update set and the add does not carry has to be carried across rather than reset to its default. On the modern wire the add action carries only the name and properties, so "listed" and the list order are the fields at risk. (On the legacy wire the add carries game mode, latency and display name itself, so those are legitimately the add's values; that is asserted below.)
        var harness = new ApplierHarness(ModernVersion);

        await harness.ApplyAsync(new ClientboundPlayerInfoUpdatePacket(
            PlayerInfoActions.UpdateListed | PlayerInfoActions.UpdateListOrder
                | PlayerInfoActions.UpdateDisplayName,
            [Fields(PlayerId) with { Listed = true, ListOrder = 7, DisplayName = Component.Text("Ranked") }]));
        await harness.ApplyAsync(new ClientboundPlayerInfoUpdatePacket(
            PlayerInfoActions.AddPlayer,
            [Fields(PlayerId) with { Name = "u7_b" }]));

        TabListEntry entry = Assert.Single(harness.State.TabList.Entries);
        Assert.Equal("u7_b", entry.Profile.Name);
        Assert.True(entry.Listed);
        Assert.Equal(7, entry.ListOrder);
        Assert.Equal("Ranked", entry.DisplayName?.ToPlainText());
    }

    [Fact]
    public async Task The_Legacy_Add_Still_Owns_The_Fields_It_Carries()
    {
        // The legacy add carries game mode, latency and the display-name override, so it overwrites whatever an earlier update set, including clearing the override back to the profile name.
        var harness = new ApplierHarness(LegacyVersion);

        await harness.ApplyAsync(new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.UpdateGameMode,
            [new LegacyPlayerListEntry(PlayerId, null, null, (int)GameMode.Spectator, 0, null)]));
        await harness.ApplyAsync(new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.UpdateDisplayName,
            [new LegacyPlayerListEntry(PlayerId, null, null, 0, 0, Component.Text("Spectating"))]));
        await harness.ApplyAsync(new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.AddPlayer,
            [new LegacyPlayerListEntry(PlayerId, "u7_b", null, (int)GameMode.Creative, 42, null)]));

        TabListEntry entry = Assert.Single(harness.State.TabList.Entries);
        Assert.Equal("u7_b", entry.Profile.Name);
        Assert.Equal(GameMode.Creative, entry.GameMode);
        Assert.Equal(42, entry.Latency);
        Assert.Null(entry.DisplayName);
    }

    [Fact]
    public async Task A_Repeated_Add_Does_Not_Churn_The_Entry()
    {
        // Servers re-send the add for an already-known player; that must not replace an equal profile, or every re-add would drop reference identity and invalidate consumers holding the entry.
        var harness = new ApplierHarness(LegacyVersion);

        await harness.ApplyAsync(Add());
        TabListEntry first = Assert.Single(harness.State.TabList.Entries);
        await harness.ApplyAsync(Add());
        TabListEntry second = Assert.Single(harness.State.TabList.Entries);

        Assert.Same(first, second);

        static ClientboundLegacyPlayerListItemPacket Add() => new(
            LegacyPlayerListAction.AddPlayer,
            [new LegacyPlayerListEntry(PlayerId, "u7_b", null, (int)GameMode.Creative, 42, null)]);
    }

    [Fact]
    public async Task An_Update_Only_Entry_Still_Keeps_Its_Placeholder_Until_An_Add_Arrives()
    {
        // A non-add action must not invent a name, and must not be treated as profile-carrying.
        var harness = new ApplierHarness(LegacyVersion);

        await harness.ApplyAsync(new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.UpdateLatency,
            [new LegacyPlayerListEntry(PlayerId, null, null, 0, 77, null)]));

        TabListEntry entry = Assert.Single(harness.State.TabList.Entries);
        Assert.Equal(string.Empty, entry.Profile.Name);
        Assert.Equal(77, entry.Latency);
    }

    private static PlayerInfoEntry Fields(Guid id) => new(
        id, null, null, HasChatSession: false, ChatSession: null, GameMode.Undefined,
        Listed: false, Latency: 0, DisplayName: null, ListOrder: 0, ShowHat: false);
}
