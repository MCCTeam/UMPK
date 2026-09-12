using Umpk;
using Umpk.Client;
using Umpk.Client.Snapshots;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests.Snapshots;

/// <summary><see cref="TabListSnapshot"/>, <see cref="ScoreboardSnapshot"/>, <see cref="BossBarSnapshot"/>, <see cref="AdvancementsSnapshot"/> and <see cref="SessionSnapshot"/>: the UI-facing and session-facts side of the read-model. Every display component remains structured instead of being flattened to a plain string.</summary>
public sealed class SessionAndUiSnapshotTests
{
    // TabListSnapshot

    [Fact]
    public void TabList_ProjectsEntriesHeaderAndFooter()
    {
        var list = new TabList
        {
            Header = Component.Text("Welcome"),
            Footer = Component.Text("Goodbye"),
        };
        var profile = new GameProfile(Guid.NewGuid(), "Steve");
        list.Upsert(new TabListEntry(profile) { GameMode = GameMode.Survival, Latency = 42 });

        TabListSnapshot snapshot = TabListSnapshot.Project(list);

        TabListEntrySnapshot entry = Assert.Single(snapshot.Entries);
        Assert.Equal(profile.Id, entry.Uuid);
        Assert.Equal("Steve", entry.Name);
        Assert.Equal(GameMode.Survival, entry.GameMode);
        Assert.Equal(42, entry.Latency);
        Assert.Equal(Component.Text("Welcome"), snapshot.Header);
        Assert.Equal(Component.Text("Goodbye"), snapshot.Footer);
    }

    [Fact]
    public void TabListEntry_DisplayNameStaysAComponent()
    {
        var profile = new GameProfile(Guid.NewGuid(), "Steve");
        var displayName = new Component(new TextContent("~Steve~"), new Style { Italic = true });
        var entry = new TabListEntry(profile) { DisplayName = displayName };

        TabListEntrySnapshot snapshot = TabListEntrySnapshot.Project(entry);

        Assert.Equal(displayName, snapshot.DisplayName);
        Assert.True(snapshot.DisplayName!.Style.Italic);
    }

    [Fact]
    public void TabList_OrdersByListOrder_AndCarriesTheListedFlag()
    {
        var list = new TabList();
        var low = new TabListEntry(new GameProfile(Guid.NewGuid(), "Low")) { ListOrder = 1, Listed = false };
        var high = new TabListEntry(new GameProfile(Guid.NewGuid(), "High")) { ListOrder = 10, Listed = true };
        list.Upsert(low);
        list.Upsert(high);

        TabListSnapshot snapshot = TabListSnapshot.Project(list);

        Assert.Equal(["High", "Low"], snapshot.Entries.Select(e => e.Name));
        Assert.False(snapshot.Entries.Single(e => e.Name == "Low").Listed);
        Assert.True(snapshot.Entries.Single(e => e.Name == "High").Listed);
    }

    // ScoreboardSnapshot

    [Fact]
    public void Scoreboard_ObjectivesCarryTheirScores()
    {
        var board = new Scoreboard();
        board.PutObjective(new Objective("obj1", Component.Text("Kills"), ObjectiveRenderType.Integer));
        board.SetScore("obj1", "Steve", 5);
        board.SetScore("obj1", "Alex", 3);

        ScoreboardSnapshot snapshot = ScoreboardSnapshot.Project(board);

        ObjectiveSnapshot objective = Assert.Single(snapshot.Objectives);
        Assert.Equal("obj1", objective.Name);
        Assert.Equal(ObjectiveRenderType.Integer, objective.RenderType);
        Assert.Equal(5, objective.Scores["Steve"]);
        Assert.Equal(3, objective.Scores["Alex"]);
    }

    [Fact]
    public void Scoreboard_TeamsCarryPrefixAndSuffixAsComponents()
    {
        var board = new Scoreboard();
        var team = new Team("red")
        {
            Prefix = new Component(new TextContent("["), new Style { Color = TextColor.FromRgb(0xFF0000) }),
            Suffix = Component.Text("]"),
        };
        team.AddMember("Steve");
        board.PutTeam(team);

        ScoreboardSnapshot snapshot = ScoreboardSnapshot.Project(board);

        TeamSnapshot teamSnapshot = Assert.Single(snapshot.Teams);
        Assert.Equal("red", teamSnapshot.Name);
        Assert.Equal(team.Prefix, teamSnapshot.Prefix);
        Assert.Equal(0xFF0000, teamSnapshot.Prefix.Style.Color!.Value.Rgb);
        Assert.Equal(team.Suffix, teamSnapshot.Suffix);
        Assert.Contains("Steve", teamSnapshot.Members);
    }

    // BossBarSnapshot

    [Fact]
    public void BossBar_ProjectsColorOverlayAndFlags()
    {
        var bar = new BossBar(
            Guid.NewGuid(), Component.Text("Boss"), 0.5f,
            BossBarColor.Red, BossBarOverlay.Notched10, BossBarFlags.DarkenScreen | BossBarFlags.PlayBossMusic);

        BossBarSnapshot snapshot = BossBarSnapshot.Project(bar);

        Assert.Equal(BossBarColor.Red, snapshot.Color);
        Assert.Equal(BossBarOverlay.Notched10, snapshot.Overlay);
        Assert.Equal(BossBarFlags.DarkenScreen | BossBarFlags.PlayBossMusic, snapshot.Flags);
        Assert.Equal(0.5f, snapshot.Progress);
    }

    // AdvancementsSnapshot

    [Fact]
    public void Advancements_ReportCompletedCriteriaCounts()
    {
        var state = new AdvancementState();
        var id = Identifier.Parse("minecraft:story/root");
        var advancement = new Advancement(
            id, null,
            new AdvancementDisplay(Component.Text("Root"), Component.Text("The beginning"), 0, true, false, null),
            ["c1", "c2", "c3"], [["c1"], ["c2"], ["c3"]]);
        state.PutAdvancement(advancement);
        state.SetCriterionProgress(id, "c1", DateTimeOffset.UtcNow);
        state.SetCriterionProgress(id, "c2", DateTimeOffset.UtcNow);
        state.SetCriterionProgress(id, "c3", null);

        AdvancementsSnapshot snapshot = AdvancementsSnapshot.Project(state);

        AdvancementSnapshot entry = Assert.Single(snapshot.Entries);
        Assert.Equal(3, entry.CriteriaCount);
        Assert.Equal(2, entry.CriteriaCompleted);
    }

    /// <summary>A displayed advancement carries its frame through the projection; a non-displayed one reports null rather than the task frame. The frame exists only inside the optional display block. Generated recipe advancements can omit that block, so "no frame" is a real wire state and must not be reported as challenge/goal/task.</summary>
    [Fact]
    public void Advancements_CarryTheFrameAndReportNullWhenNotDisplayed()
    {
        var state = new AdvancementState();
        var challengeId = Identifier.Parse("minecraft:husbandry/balanced_diet");
        var recipeId = Identifier.Parse("minecraft:recipes/building_blocks/oak_stairs");
        state.PutAdvancement(new Advancement(
            challengeId, null,
            new AdvancementDisplay(Component.Text("A Balanced Diet"), Component.Text("Eat everything"), 1, true, false, null),
            ["apple"], [["apple"]]));
        state.PutAdvancement(new Advancement(recipeId, null, null, ["has_planks"], [["has_planks"]]));

        AdvancementsSnapshot snapshot = AdvancementsSnapshot.Project(state);

        AdvancementSnapshot challenge = snapshot.Entries.Single(e => e.Id == challengeId);
        AdvancementSnapshot recipe = snapshot.Entries.Single(e => e.Id == recipeId);
        Assert.Equal(1, challenge.Frame);
        Assert.Null(recipe.Frame);
    }

    /// <summary>H14: outside protocols 770-773 and 26.2, UMPK relays <c>UpdateAdvancements</c> verbatim without decoding its contents, so nothing is ever added to <see cref="AdvancementState"/>. The snapshot reports that as an honestly empty set: not a thrown error, and not a silently invented one either.</summary>
    [Fact]
    public void Advancements_ReportAnEmptySetOnBandsWhereContentsDoNotDecode()
    {
        var state = new AdvancementState();

        AdvancementsSnapshot snapshot = AdvancementsSnapshot.Project(state);

        Assert.Empty(snapshot.Entries);
        Assert.Null(snapshot.SelectedTab);
    }

    // ClientState.ObservedLatency

    [Fact]
    public void Latency_PrefersTheApplierTrackedValue()
    {
        var state = new ClientState(new ClientFeatures().Normalized());
        state.Self.Uuid = Guid.NewGuid();
        state.Server.ObservedLatency = 42;
        state.TabList.Upsert(new TabListEntry(new GameProfile(state.Self.Uuid, "Tester")) { Latency = 999 });

        Assert.Equal(42, state.ObservedLatency);
    }

    [Fact]
    public void Latency_FallsBackToTheTabListEntryForOurOwnUuid()
    {
        var state = new ClientState(new ClientFeatures().Normalized());
        state.Self.Uuid = Guid.NewGuid();
        state.TabList.Upsert(new TabListEntry(new GameProfile(state.Self.Uuid, "Tester")) { Latency = 77 });

        Assert.Equal(77, state.ObservedLatency);
    }

    [Fact]
    public void Latency_IsNullBeforeTheServerReportsOne()
    {
        var state = new ClientState(new ClientFeatures().Normalized());

        Assert.Null(state.ObservedLatency);
    }

    [Fact]
    public void Latency_IsNotTheKeepAliveTurnaround()
    {
        var clock = new ManualTimeProvider();
        var state = new ClientState(new ClientFeatures().Normalized());
        state.Server.ObservedLatency = 25;

        state.Server.RecordKeepAliveReceived(1, clock.GetTimestamp(), clock);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        state.Server.RecordKeepAliveAnswered(clock.GetTimestamp(), clock);

        SessionSnapshot session = SessionSnapshot.Project(state, session: null);

        // The two figures are set to deliberately different numbers, and the one that surfaces as ObservedLatencyMs must be the applier-tracked round trip, not the keep-alive turnaround.
        Assert.Equal(25, session.ObservedLatencyMs);
        Assert.Equal(500.0, session.KeepAliveTurnaround!.Value.TotalMilliseconds);
    }

    // SessionSnapshot

    [Fact]
    public void Session_ReportsEndpointAndVersion()
    {
        var state = new ClientState(new ClientFeatures().Normalized());
        var session = new SessionInfo
        {
            Profile = new GameProfile(Guid.NewGuid(), "Tester"),
            Endpoint = new ServerEndpoint("play.example.com", 25566),
            Version = JavaVersions.V1_21_11,
        };

        SessionSnapshot snapshot = SessionSnapshot.Project(state, session);

        Assert.Equal("play.example.com", snapshot.Host);
        Assert.Equal(25566, snapshot.Port);
        Assert.Equal(JavaVersions.V1_21_11.Version.Name, snapshot.VersionName);
        Assert.Equal(JavaVersions.V1_21_11.Version.Protocol, snapshot.Protocol);
    }

    [Fact]
    public void Session_ReportsNullTpsWhenTheServerStoppedBroadcasting()
    {
        var clock = new ManualTimeProvider();
        var state = new ClientState(new ClientFeatures().Normalized());
        state.Server.RecordGameTime(1000, clock.GetTimestamp(), clock);
        clock.Advance(TimeSpan.FromSeconds(1));
        state.Server.RecordGameTime(1020, clock.GetTimestamp(), clock);
        Assert.Equal(20.0, SessionSnapshot.Project(state, null).TpsEstimate!.Value, 3);

        // The server pauses: no further broadcasts arrive, and enough time passes that the sample expires.
        clock.Advance(ServerState.TickSampleLifetime + TimeSpan.FromSeconds(1));
        state.Server.ExpireStaleTickRate(clock.GetTimestamp(), clock);

        Assert.Null(SessionSnapshot.Project(state, null).TpsEstimate);
    }

    [Fact]
    public void Session_ReportsEmptyHostBeforeNegotiation()
    {
        var state = new ClientState(new ClientFeatures().Normalized());

        SessionSnapshot snapshot = SessionSnapshot.Project(state, session: null);

        Assert.Equal(string.Empty, snapshot.Host);
        Assert.Equal(0, snapshot.Port);
        Assert.Equal(string.Empty, snapshot.VersionName);
        Assert.Equal(0, snapshot.Protocol);
    }

    [Fact]
    public void Session_ReportsTheServerBrand()
    {
        var state = new ClientState(new ClientFeatures().Normalized());
        state.Server.Brand = "Paper";

        SessionSnapshot snapshot = SessionSnapshot.Project(state, session: null);

        Assert.Equal("Paper", snapshot.Brand);
    }

    [Fact]
    public void Session_KeepAliveIntervalIsNullBeforeTheSecondKeepAlive()
    {
        var clock = new ManualTimeProvider();
        var state = new ClientState(new ClientFeatures().Normalized());

        state.Server.RecordKeepAliveReceived(1, clock.GetTimestamp(), clock);
        Assert.Null(SessionSnapshot.Project(state, null).KeepAliveInterval);

        clock.Advance(TimeSpan.FromSeconds(15));
        state.Server.RecordKeepAliveReceived(2, clock.GetTimestamp(), clock);
        Assert.Equal(TimeSpan.FromSeconds(15), SessionSnapshot.Project(state, null).KeepAliveInterval);
    }

    /// <summary>The marshalling contract end to end: a snapshot taken through <see cref="ClientSnapshots.SessionAsync"/> reads every field from the SAME point in time on the loop, so a latency figure set alongside a decoy tab-list entry in one posted mutation comes back as the tracked value, not a torn mix.</summary>
    [Fact]
    public async Task Session_IsAConsistentLoopRead()
    {
        await using UmpkClient client = new UmpkClientBuilder()
            .UseVersion(JavaVersions.V1_21_11)
            .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
            .Build();

        await client.PostAsync(c =>
        {
            c.State.Server.ObservedLatency = 12;
            c.State.TabList.Upsert(new TabListEntry(new GameProfile(c.State.Self.Uuid, "Tester")) { Latency = 999 });
        });

        SessionSnapshot session = await client.Snapshots.SessionAsync();

        Assert.Equal(12, session.ObservedLatencyMs);
    }
}
