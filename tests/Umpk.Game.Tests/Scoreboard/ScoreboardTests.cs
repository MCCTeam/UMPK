using Umpk.Game.Scoreboard;
using Umpk.Text;
using Xunit;

namespace Umpk.Game.Tests.Scoreboard;

public sealed class ScoreboardTests
{
    [Fact]
    public void Objective_Lifecycle()
    {
        var board = new Umpk.Game.Scoreboard.Scoreboard();
        board.PutObjective(new Objective("kills", Component.Text("Kills"), ObjectiveRenderType.Integer));

        Assert.True(board.TryGetObjective("kills", out Objective? obj));
        Assert.Equal("Kills", obj.DisplayName.ToPlainText());

        // update-in-place
        obj.DisplayName = Component.Text("Total Kills");
        obj.RenderType = ObjectiveRenderType.Hearts;
        Assert.True(board.TryGetObjective("kills", out Objective? updated));
        Assert.Equal("Total Kills", updated.DisplayName.ToPlainText());
        Assert.Equal(ObjectiveRenderType.Hearts, updated.RenderType);

        Assert.True(board.RemoveObjective("kills"));
        Assert.False(board.TryGetObjective("kills", out _));
    }

    [Fact]
    public void Score_SetGet_And_Remove()
    {
        var board = new Umpk.Game.Scoreboard.Scoreboard();
        board.SetScore("kills", "alice", 5);
        board.SetScore("kills", "bob", 3);
        board.SetScore("kills", "alice", 7); // overwrite

        Assert.True(board.TryGetScore("kills", "alice", out int a));
        Assert.Equal(7, a);
        Assert.Equal(2, board.GetScores("kills").Count);

        Assert.True(board.RemoveScore("kills", "bob"));
        Assert.False(board.TryGetScore("kills", "bob", out _));
    }

    [Fact]
    public void ResetScores_Removes_Owner_From_All_Objectives()
    {
        var board = new Umpk.Game.Scoreboard.Scoreboard();
        board.SetScore("kills", "alice", 5);
        board.SetScore("deaths", "alice", 2);
        board.SetScore("kills", "bob", 1);

        Assert.True(board.ResetScores("alice"));
        Assert.False(board.TryGetScore("kills", "alice", out _));
        Assert.False(board.TryGetScore("deaths", "alice", out _));
        Assert.True(board.TryGetScore("kills", "bob", out _));
    }

    [Fact]
    public void RemoveObjective_Drops_Scores_And_Display()
    {
        var board = new Umpk.Game.Scoreboard.Scoreboard();
        board.PutObjective(new Objective("kills", Component.Text("K"), ObjectiveRenderType.Integer));
        board.SetScore("kills", "alice", 5);
        board.SetDisplay(DisplaySlot.Sidebar, "kills");

        board.RemoveObjective("kills");

        Assert.False(board.TryGetScore("kills", "alice", out _));
        Assert.False(board.TryGetDisplay(DisplaySlot.Sidebar, out _));
    }

    [Fact]
    public void Display_Slot_Assign_And_Clear()
    {
        var board = new Umpk.Game.Scoreboard.Scoreboard();
        board.SetDisplay(DisplaySlot.Sidebar, "kills");
        Assert.True(board.TryGetDisplay(DisplaySlot.Sidebar, out string? name));
        Assert.Equal("kills", name);

        board.SetDisplay(DisplaySlot.Sidebar, null);
        Assert.False(board.TryGetDisplay(DisplaySlot.Sidebar, out _));
    }

    [Fact]
    public void Team_Lifecycle_And_Membership()
    {
        var board = new Umpk.Game.Scoreboard.Scoreboard();
        var team = new Team("red")
        {
            DisplayName = Component.Text("Red"),
            Prefix = Component.Text("[R] "),
            Flags = TeamFlags.AllowFriendlyFire | TeamFlags.CanSeeFriendlyInvisibles,
            CollisionRule = CollisionRule.PushOwnTeam,
            NameTagVisibility = NameTagVisibility.HideForOtherTeams,
            Color = 2,
        };
        board.PutTeam(team);

        Assert.True(team.AllowFriendlyFire);
        Assert.True(team.CanSeeFriendlyInvisibles);

        Assert.True(team.AddMember("alice"));
        Assert.False(team.AddMember("alice"));
        Assert.True(board.TryGetMemberTeam("alice", out Team? found));
        Assert.Equal("red", found.Name);

        Assert.True(team.RemoveMember("alice"));
        Assert.False(board.TryGetMemberTeam("alice", out _));

        Assert.True(board.RemoveTeam("red"));
        Assert.False(board.TryGetTeam("red", out _));
    }
}
