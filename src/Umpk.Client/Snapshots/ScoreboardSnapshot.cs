using Umpk.Game.Scoreboard;
using Umpk.Text;

namespace Umpk.Client.Snapshots;

/// <summary>One scoreboard objective and the scores tracked under it.</summary>
/// <param name="Name">The stable internal name (the wire key).</param>
/// <param name="DisplayName">The rendered display component.</param>
/// <param name="RenderType">How scores render.</param>
/// <param name="Scores">The owner-to-value scores under this objective.</param>
public sealed record ObjectiveSnapshot(
    string Name, Component DisplayName, ObjectiveRenderType RenderType, IReadOnlyDictionary<string, int> Scores);

/// <summary>One scoreboard team.</summary>
/// <param name="Name">The stable internal name (the wire key).</param>
/// <param name="DisplayName">The team display component.</param>
/// <param name="Prefix">The prefix prepended to member names, still a structured component.</param>
/// <param name="Suffix">The suffix appended to member names, still a structured component.</param>
/// <param name="Color">The name-tag color, a vanilla <c>ChatFormatting</c> id (0-15 named, 21 reset/none).</param>
/// <param name="Members">The current members (player names or stringified entity uuids).</param>
public sealed record TeamSnapshot(
    string Name, Component DisplayName, Component Prefix, Component Suffix, int Color, IReadOnlyList<string> Members);

/// <summary>A scoreboard snapshot: every objective and every team.</summary>
public sealed record ScoreboardSnapshot(IReadOnlyList<ObjectiveSnapshot> Objectives, IReadOnlyList<TeamSnapshot> Teams)
{
    /// <summary>Projects a <see cref="ScoreboardSnapshot"/> from the live scoreboard. Pure; no session loop involved.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="board"/> is null.</exception>
    public static ScoreboardSnapshot Project(Scoreboard board)
    {
        ArgumentNullException.ThrowIfNull(board);

        var objectives = new List<ObjectiveSnapshot>(board.Objectives.Count);
        foreach (Objective objective in board.Objectives)
            objectives.Add(new ObjectiveSnapshot(
                objective.Name, objective.DisplayName, objective.RenderType,
                new Dictionary<string, int>(board.GetScores(objective.Name), StringComparer.Ordinal)));

        var teams = new List<TeamSnapshot>(board.Teams.Count);
        foreach (Team team in board.Teams)
            teams.Add(new TeamSnapshot(team.Name, team.DisplayName, team.Prefix, team.Suffix, team.Color, [.. team.Members]));

        return new ScoreboardSnapshot(objectives, teams);
    }
}
