using Umpk.Game.Players;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Ui;

/// <summary>Seeded round-trip tests for player-list, title, and boss-bar codecs.</summary>
public class PlayerListAndTitleCodecTests
{
    private static byte[] RandomBytes(Random rng, int count)
    {
        var b = new byte[count];
        rng.NextBytes(b);
        return b;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(77)]
    public void PlayerInfoUpdate_AllActions_RoundTrips(int seed)
    {
        var rng = new Random(seed);
        const PlayerInfoActions actions = PlayerInfoActions.AddPlayer | PlayerInfoActions.InitializeChat
            | PlayerInfoActions.UpdateGameMode | PlayerInfoActions.UpdateListed | PlayerInfoActions.UpdateLatency
            | PlayerInfoActions.UpdateDisplayName | PlayerInfoActions.UpdateListOrder | PlayerInfoActions.UpdateHat;

        var session = new RemoteChatSession(Guid.NewGuid(), rng.NextInt64(),
            RandomBytes(rng, 140), RandomBytes(rng, 256));
        var entry = new PlayerInfoEntry(
            Guid.NewGuid(), "Steve",
            [new GameProfileProperty("textures", "blob", "sig")],
            HasChatSession: true, session, GameMode.Creative, Listed: true, Latency: 55,
            Component.Text("StevePvP"), ListOrder: 3, ShowHat: true);
        var packet = new ClientboundPlayerInfoUpdatePacket(actions, [entry]);

        var decoded = CodecRoundTrip.Cycle(PlayerListCodecs.PlayerInfoUpdateV1_21_5, packet);
        Assert.Equal(actions, decoded.Actions);
        PlayerInfoEntry d = Assert.Single(decoded.Entries);
        Assert.Equal("Steve", d.Name);
        Assert.Equal(GameMode.Creative, d.GameMode);
        Assert.Equal(55, d.Latency);
        Assert.Equal(3, d.ListOrder);
        Assert.True(d.ShowHat);
        Assert.True(d.HasChatSession);
        Assert.Equal(session.SessionId, d.ChatSession!.SessionId);
        Assert.Equal(session.PublicKey, d.ChatSession.PublicKey);
        Assert.Equal(session.KeySignature, d.ChatSession.KeySignature);
    }

    [Fact]
    public void PlayerInfoUpdate_MinimalActionSet_RoundTrips()
    {
        var packet = new ClientboundPlayerInfoUpdatePacket(
            PlayerInfoActions.UpdateLatency,
            [new PlayerInfoEntry(Guid.NewGuid(), null, null, false, null, GameMode.Undefined, false, 120, null, 0, false)]);
        var decoded = CodecRoundTrip.Cycle(PlayerListCodecs.PlayerInfoUpdateV1_21_5, packet);
        Assert.Equal(120, Assert.Single(decoded.Entries).Latency);
    }

    [Fact]
    public void PlayerInfoUpdate_EmptyEntries_RoundTrips()
    {
        var packet = new ClientboundPlayerInfoUpdatePacket(PlayerInfoActions.UpdateListed, []);
        Assert.Empty(CodecRoundTrip.Cycle(PlayerListCodecs.PlayerInfoUpdateV1_21_5, packet).Entries);
    }

    [Fact]
    public void PlayerInfoUpdate_InitializeChatWithoutSession_RoundTrips()
    {
        var packet = new ClientboundPlayerInfoUpdatePacket(
            PlayerInfoActions.InitializeChat,
            [new PlayerInfoEntry(Guid.NewGuid(), null, null, false, null, GameMode.Undefined, false, 0, null, 0, false)]);
        Assert.False(Assert.Single(CodecRoundTrip.Cycle(PlayerListCodecs.PlayerInfoUpdateV1_21_5, packet).Entries).HasChatSession);
    }

    [Fact]
    public void PlayerInfoRemove_RoundTrips()
    {
        var packet = new ClientboundPlayerInfoRemovePacket([Guid.NewGuid(), Guid.NewGuid()]);
        Assert.Equal(2, CodecRoundTrip.Cycle(PlayerListCodecs.PlayerInfoRemoveV1_19_3, packet).ProfileIds.Count);
        Assert.Empty(CodecRoundTrip.Cycle(PlayerListCodecs.PlayerInfoRemoveV1_19_3, new ClientboundPlayerInfoRemovePacket([])).ProfileIds);
    }

    [Theory]
    [InlineData(LegacyPlayerListAction.AddPlayer)]
    [InlineData(LegacyPlayerListAction.UpdateGameMode)]
    [InlineData(LegacyPlayerListAction.UpdateLatency)]
    [InlineData(LegacyPlayerListAction.UpdateDisplayName)]
    [InlineData(LegacyPlayerListAction.RemovePlayer)]
    public void LegacyPlayerListItem_RoundTrips(LegacyPlayerListAction action)
    {
        var entry = new LegacyPlayerListEntry(
            Guid.NewGuid(),
            action == LegacyPlayerListAction.AddPlayer ? "Bob" : null,
            action == LegacyPlayerListAction.AddPlayer ? [new GameProfileProperty("textures", "v", null)] : null,
            2, 33,
            action is LegacyPlayerListAction.AddPlayer or LegacyPlayerListAction.UpdateDisplayName ? Component.Text("Bob") : null);
        var packet = new ClientboundLegacyPlayerListItemPacket(action, [entry]);
        var decoded = CodecRoundTrip.Cycle(PlayerListCodecs.LegacyPlayerListItemV1_8, packet);
        Assert.Equal(action, decoded.Action);
        Assert.Single(decoded.Entries);
    }

    [Fact]
    public void TabList_RoundTrips_BothWireLayouts()
    {
        var modern = new ClientboundTabListPacket(Component.Text("Header"), Component.Text("Footer"));
        var dm = CodecRoundTrip.Cycle(PlayerListCodecs.TabListV1_21_5, modern);
        Assert.Equal("Header", dm.Header.ToPlainText());
        Assert.Equal("Footer", dm.Footer.ToPlainText());

        var dl = CodecRoundTrip.Cycle(PlayerListCodecs.TabListV1_8, modern);
        Assert.Equal("Header", dl.Header.ToPlainText());
    }

    [Fact]
    public void TitleTexts_RoundTrip()
    {
        Assert.Equal("T", CodecRoundTrip.Cycle(TitleCodecs.SetTitleTextV1_21_5, new ClientboundSetTitleTextPacket(Component.Text("T"))).Text.ToPlainText());
        Assert.Equal("S", CodecRoundTrip.Cycle(TitleCodecs.SetSubtitleTextV1_21_5, new ClientboundSetSubtitleTextPacket(Component.Text("S"))).Text.ToPlainText());
        Assert.Equal("A", CodecRoundTrip.Cycle(TitleCodecs.SetActionBarTextV1_21_5, new ClientboundSetActionBarTextPacket(Component.Text("A"))).Text.ToPlainText());
    }

    [Fact]
    public void TitlesAnimation_And_ClearTitles_RoundTrip()
    {
        var anim = new ClientboundSetTitlesAnimationPacket(10, 70, 20);
        Assert.Equal(anim, CodecRoundTrip.Cycle(TitleCodecs.SetTitlesAnimationV1_17, anim));
        Assert.True(CodecRoundTrip.Cycle(TitleCodecs.ClearTitlesV1_17, new ClientboundClearTitlesPacket(true)).ResetTimes);
        Assert.False(CodecRoundTrip.Cycle(TitleCodecs.ClearTitlesV1_17, new ClientboundClearTitlesPacket(false)).ResetTimes);
    }

    [Theory]
    [InlineData(LegacyTitleAction.Title)]
    [InlineData(LegacyTitleAction.Subtitle)]
    [InlineData(LegacyTitleAction.Times)]
    [InlineData(LegacyTitleAction.Clear)]
    [InlineData(LegacyTitleAction.Reset)]
    public void LegacyTitle_RoundTrips(LegacyTitleAction action)
    {
        var packet = action switch
        {
            LegacyTitleAction.Title or LegacyTitleAction.Subtitle =>
                new ClientboundLegacyTitlePacket(action, Component.Text("hi"), 0, 0, 0),
            LegacyTitleAction.Times => new ClientboundLegacyTitlePacket(action, null, 5, 100, 5),
            _ => new ClientboundLegacyTitlePacket(action, null, 0, 0, 0),
        };
        var decoded = CodecRoundTrip.Cycle(TitleCodecs.LegacyTitleV1_8, packet);
        Assert.Equal(action, decoded.Action);
        if (action == LegacyTitleAction.Times)
            Assert.Equal(100, decoded.Stay);

    }

    [Theory]
    [InlineData(BossEventOperation.Add)]
    [InlineData(BossEventOperation.Remove)]
    [InlineData(BossEventOperation.UpdateProgress)]
    [InlineData(BossEventOperation.UpdateName)]
    [InlineData(BossEventOperation.UpdateStyle)]
    [InlineData(BossEventOperation.UpdateProperties)]
    public void BossEvent_RoundTrips(BossEventOperation op)
    {
        var id = Guid.NewGuid();
        var packet = new ClientboundBossEventPacket(
            id, op,
            op is BossEventOperation.Add or BossEventOperation.UpdateName ? Component.Text("Boss") : null,
            0.75f, BossBarColor.Purple, BossBarOverlay.Notched12,
            BossBarFlags.DarkenScreen | BossBarFlags.CreateWorldFog);

        var decoded = CodecRoundTrip.Cycle(BossBarCodecs.BossEventV1_21_5, packet);
        Assert.Equal(id, decoded.Id);
        Assert.Equal(op, decoded.Operation);
        switch (op)
        {
            case BossEventOperation.Add:
                Assert.Equal(0.75f, decoded.Progress);
                Assert.Equal(BossBarColor.Purple, decoded.Color);
                Assert.Equal(BossBarOverlay.Notched12, decoded.Overlay);
                Assert.Equal(BossBarFlags.DarkenScreen | BossBarFlags.CreateWorldFog, decoded.Flags);
                break;
            case BossEventOperation.UpdateProgress:
                Assert.Equal(0.75f, decoded.Progress);
                break;
            case BossEventOperation.UpdateStyle:
                Assert.Equal(BossBarColor.Purple, decoded.Color);
                break;
            case BossEventOperation.UpdateProperties:
                Assert.Equal(BossBarFlags.DarkenScreen | BossBarFlags.CreateWorldFog, decoded.Flags);
                break;
            default:
                break;
        }
    }
}
