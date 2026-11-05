using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Players;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Vanilla ignores updates for unknown players. These checks cover profile creation, add defaults and repeated adds in the single-action and action-bitset packet families.</summary>
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
        Assert.Empty(harness.State.TabList.Entries);
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
        Assert.Empty(harness.State.TabList.Entries);
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
    public async Task Unknown_Updates_Do_Not_Change_The_Modern_Add_Defaults()
    {
        var harness = new ApplierHarness(ModernVersion);

        await harness.ApplyAsync(new ClientboundPlayerInfoUpdatePacket(
            PlayerInfoActions.UpdateListed | PlayerInfoActions.UpdateListOrder
                | PlayerInfoActions.UpdateDisplayName,
            [Fields(PlayerId) with { Listed = true, ListOrder = 7, DisplayName = Component.Text("Ranked") }]));
        Assert.Empty(harness.State.TabList.Entries);
        await harness.ApplyAsync(new ClientboundPlayerInfoUpdatePacket(
            PlayerInfoActions.AddPlayer,
            [Fields(PlayerId) with { Name = "u7_b" }]));

        TabListEntry entry = Assert.Single(harness.State.TabList.Entries);
        Assert.Equal("u7_b", entry.Profile.Name);
        Assert.False(entry.Listed);
        Assert.Equal(GameMode.Survival, entry.GameMode);
        Assert.Equal(0, entry.Latency);
        Assert.Equal(0, entry.ListOrder);
        Assert.Null(entry.DisplayName);
    }

    [Fact]
    public async Task The_Legacy_Add_Still_Owns_The_Fields_It_Carries()
    {
        // Unknown updates are ignored; the legacy add supplies mode, latency and display name together.
        var harness = new ApplierHarness(LegacyVersion);

        await harness.ApplyAsync(new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.UpdateGameMode,
            [new LegacyPlayerListEntry(PlayerId, null, null, (int)GameMode.Spectator, 0, null)]));
        await harness.ApplyAsync(new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.UpdateDisplayName,
            [new LegacyPlayerListEntry(PlayerId, null, null, 0, 0, Component.Text("Spectating"))]));
        Assert.Empty(harness.State.TabList.Entries);
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
    public async Task A_Repeated_Add_Replaces_The_Legacy_Entry()
    {
        // The 1.14.4 handler constructs PlayerInfo on every add, including an unchanged profile.
        var harness = new ApplierHarness(LegacyVersion);

        await harness.ApplyAsync(Add());
        TabListEntry first = Assert.Single(harness.State.TabList.Entries);
        await harness.ApplyAsync(Add());
        TabListEntry second = Assert.Single(harness.State.TabList.Entries);

        Assert.NotSame(first, second);
        Assert.Equal(first.Profile.Name, second.Profile.Name);
        Assert.Equal(first.GameMode, second.GameMode);

        static ClientboundLegacyPlayerListItemPacket Add() => new(
            LegacyPlayerListAction.AddPlayer,
            [new LegacyPlayerListEntry(PlayerId, "u7_b", null, (int)GameMode.Creative, 42, null)]);
    }

    [Fact]
    public async Task Unknown_Latency_Is_Ignored_And_Known_Latency_Is_Applied()
    {
        var harness = new ApplierHarness(LegacyVersion);

        await harness.ApplyAsync(new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.UpdateLatency,
            [new LegacyPlayerListEntry(PlayerId, null, null, 0, 77, null)]));
        Assert.Empty(harness.State.TabList.Entries);
        await harness.ApplyAsync(new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.AddPlayer,
            [new LegacyPlayerListEntry(PlayerId, "u7_b", null, (int)GameMode.Survival, 42, null)]));

        TabListEntry entry = Assert.Single(harness.State.TabList.Entries);
        Assert.Equal("u7_b", entry.Profile.Name);
        Assert.Equal(42, entry.Latency);
        await harness.ApplyAsync(new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.UpdateLatency,
            [new LegacyPlayerListEntry(PlayerId, null, null, 0, 77, null)]));
        Assert.Same(entry, Assert.Single(harness.State.TabList.Entries));
        Assert.Equal(77, entry.Latency);
    }

    private static PlayerInfoEntry Fields(Guid id) => new(
        id, null, null, HasChatSession: false, ChatSession: null, GameMode.Undefined,
        Listed: false, Latency: 0, DisplayName: null, ListOrder: 0, ShowHat: false);
}
