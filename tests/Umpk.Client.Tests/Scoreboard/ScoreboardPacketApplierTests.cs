using Umpk.Client.Tests.Support;
using Umpk.Game.Scoreboard;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests.PacketApplication.Scoreboard;

/// <summary>Team, objective, display-slot, and score packets applied through their bound codecs.</summary>
public sealed class ScoreboardPacketApplierTests
{
    [Theory]
    [InlineData(107)]
    [InlineData(340)]
    [InlineData(404)]
    public async Task SetPlayerTeam_PopulatesScoreboard(int protocol)
    {
        ApplierHarness harness = await BoundPacketApplierHarness.JoinedAsync(protocol);
        var parameters = new TeamParameters(
            DisplayName: Umpk.Text.Component.Text("Red Team"),
            Prefix: Umpk.Text.Component.Text("["),
            Suffix: Umpk.Text.Component.Text("]"),
            NameTagVisibility: NameTagVisibility.HideForOtherTeams,
            CollisionRule: CollisionRule.PushOwnTeam,
            Color: 12,
            Options: 0x03);
        var packet = new ClientboundSetPlayerTeamPacket("red", TeamMethod.Add, parameters, ["alice", "bob"]);

        await BoundPacketApplierHarness.RoundTripAndApplyAsync(harness, protocol, "set_player_team", packet);

        Team team = Assert.Single(harness.State.Scoreboard.Teams);
        Assert.Equal("red", team.Name);
        Assert.Equal(CollisionRule.PushOwnTeam, team.CollisionRule);
        Assert.Equal(NameTagVisibility.HideForOtherTeams, team.NameTagVisibility);
        Assert.Equal(12, team.Color);
        Assert.Contains("alice", team.Members);
        Assert.Contains("bob", team.Members);
    }

    [Theory]
    [InlineData(107)]
    [InlineData(340)]
    [InlineData(404)]
    public async Task ObjectiveDisplayAndScore_PopulateScoreboard(int protocol)
    {
        ApplierHarness harness = await BoundPacketApplierHarness.JoinedAsync(protocol);

        await BoundPacketApplierHarness.RoundTripAndApplyAsync(harness, protocol, "set_objective",
            new ClientboundSetObjectivePacket(
                "kills",
                ScoreboardObjectiveMode.Add,
                Umpk.Text.Component.Text("Kill Count"),
                ObjectiveRenderType.Hearts,
                NumberFormat: null));
        await BoundPacketApplierHarness.RoundTripAndApplyAsync(harness, protocol, "set_display_objective",
            new ClientboundSetDisplayObjectivePacket(1, "kills"));
        await BoundPacketApplierHarness.RoundTripAndApplyAsync(harness, protocol, "set_score",
            new ClientboundLegacySetScorePacket("alice", LegacyScoreAction.Change, "kills", 42));

        Objective objective = Assert.Single(harness.State.Scoreboard.Objectives);
        Assert.Equal("kills", objective.Name);
        Assert.Equal("Kill Count", objective.DisplayName.ToPlainText());
        Assert.Equal(ObjectiveRenderType.Hearts, objective.RenderType);
        Assert.True(harness.State.Scoreboard.TryGetDisplay(DisplaySlot.Sidebar, out string? displayed));
        Assert.Equal("kills", displayed);
        Assert.True(harness.State.Scoreboard.TryGetScore("kills", "alice", out int score));
        Assert.Equal(42, score);
    }
}
