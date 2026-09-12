using System.Diagnostics.CodeAnalysis;

namespace Umpk.Game.Scoreboard;

/// <summary>The per-session scoreboard state (SetObjective, UpdateScore/SetScore, ResetScore, SetDisplayObjective, SetPlayerTeam). Holds objectives keyed by name, per-objective per-owner scores, the objective assigned to each display slot, and the teams keyed by name. This is the storage the client-package scoreboard handler drives; it carries no packet logic.</summary>
public sealed class Scoreboard
{
    private readonly Dictionary<string, Objective> _objectives = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, int>> _scores = new(StringComparer.Ordinal);
    private readonly Dictionary<DisplaySlot, string> _displays = [];
    private readonly Dictionary<string, Team> _teams = new(StringComparer.Ordinal);

    /// <summary>The objectives currently defined.</summary>
    public IReadOnlyCollection<Objective> Objectives => _objectives.Values;

    /// <summary>The teams currently defined.</summary>
    public IReadOnlyCollection<Team> Teams => _teams.Values;

    /// <summary>Adds or replaces an objective (the create/update operations).</summary>
    /// <exception cref="ArgumentNullException"><paramref name="objective"/> is null.</exception>
    public void PutObjective(Objective objective)
    {
        ArgumentNullException.ThrowIfNull(objective);
        _objectives[objective.Name] = objective;
    }

    /// <summary>Gets the objective with the given name.</summary>
    public bool TryGetObjective(string name, [NotNullWhen(true)] out Objective? objective)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _objectives.TryGetValue(name, out objective);
    }

    /// <summary>Removes an objective and every score under it, and clears any display slot pointing at it. Returns true when the objective existed.</summary>
    public bool RemoveObjective(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!_objectives.Remove(name))
            return false;

        _scores.Remove(name);
        foreach (DisplaySlot slot in _displays.Where(kv => string.Equals(kv.Value, name, StringComparison.Ordinal)).Select(kv => kv.Key).ToArray())
            _displays.Remove(slot);

        return true;
    }

    /// <summary>Sets <paramref name="owner"/>'s score under <paramref name="objectiveName"/> (UpdateScore/SetScore).</summary>
    /// <exception cref="ArgumentNullException">A string argument is null.</exception>
    public void SetScore(string objectiveName, string owner, int value)
    {
        ArgumentNullException.ThrowIfNull(objectiveName);
        ArgumentNullException.ThrowIfNull(owner);
        if (!_scores.TryGetValue(objectiveName, out Dictionary<string, int>? byOwner))
        {
            byOwner = new Dictionary<string, int>(StringComparer.Ordinal);
            _scores[objectiveName] = byOwner;
        }

        byOwner[owner] = value;
    }

    /// <summary>Gets <paramref name="owner"/>'s score under <paramref name="objectiveName"/>.</summary>
    public bool TryGetScore(string objectiveName, string owner, out int value)
    {
        ArgumentNullException.ThrowIfNull(objectiveName);
        ArgumentNullException.ThrowIfNull(owner);
        value = 0;
        return _scores.TryGetValue(objectiveName, out Dictionary<string, int>? byOwner)
            && byOwner.TryGetValue(owner, out value);
    }

    /// <summary>The owner-to-value scores under an objective (empty when the objective has none).</summary>
    public IReadOnlyDictionary<string, int> GetScores(string objectiveName)
    {
        ArgumentNullException.ThrowIfNull(objectiveName);
        return _scores.TryGetValue(objectiveName, out Dictionary<string, int>? byOwner)
            ? byOwner
            : EmptyScores;
    }

    /// <summary>Removes <paramref name="owner"/>'s score under a single objective (the pre-1.20.3 remove action). Returns true when a score was removed.</summary>
    public bool RemoveScore(string objectiveName, string owner)
    {
        ArgumentNullException.ThrowIfNull(objectiveName);
        ArgumentNullException.ThrowIfNull(owner);
        return _scores.TryGetValue(objectiveName, out Dictionary<string, int>? byOwner) && byOwner.Remove(owner);
    }

    /// <summary>Removes <paramref name="owner"/>'s score from every objective (the 1.20.3+ <c>ResetScore</c> with no objective). Returns true when at least one score was removed.</summary>
    public bool ResetScores(string owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        bool any = false;
        foreach (Dictionary<string, int> byOwner in _scores.Values)
            any |= byOwner.Remove(owner);

        return any;
    }

    /// <summary>Assigns an objective to a display slot, or clears it when <paramref name="objectiveName"/> is null (SetDisplayObjective).</summary>
    public void SetDisplay(DisplaySlot slot, string? objectiveName)
    {
        if (objectiveName is null)
            _displays.Remove(slot);

        else
            _displays[slot] = objectiveName;

    }

    /// <summary>Gets the objective name shown in a display slot.</summary>
    public bool TryGetDisplay(DisplaySlot slot, [NotNullWhen(true)] out string? objectiveName) =>
        _displays.TryGetValue(slot, out objectiveName);

    /// <summary>Adds or replaces a team (the create operation).</summary>
    /// <exception cref="ArgumentNullException"><paramref name="team"/> is null.</exception>
    public void PutTeam(Team team)
    {
        ArgumentNullException.ThrowIfNull(team);
        _teams[team.Name] = team;
    }

    /// <summary>Gets the team with the given name.</summary>
    public bool TryGetTeam(string name, [NotNullWhen(true)] out Team? team)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _teams.TryGetValue(name, out team);
    }

    /// <summary>Removes a team (the remove operation); returns true when it existed.</summary>
    public bool RemoveTeam(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _teams.Remove(name);
    }

    /// <summary>Removes every objective, score, display assignment and team. The scoreboard belongs to the current level, so replacing the level must replace this state too. The server then resends surviving entries, so a caller that keeps the old contents keeps every objective and team deleted in the meantime.</summary>
    public void Clear()
    {
        _objectives.Clear();
        _scores.Clear();
        _displays.Clear();
        _teams.Clear();
    }

    /// <summary>Finds the team an entry belongs to, if any (vanilla entries belong to at most one team).</summary>
    public bool TryGetMemberTeam(string entry, [NotNullWhen(true)] out Team? team)
    {
        ArgumentNullException.ThrowIfNull(entry);
        foreach (Team candidate in _teams.Values)
            if (candidate.HasMember(entry))
            {
                team = candidate;
                return true;
            }

        team = null;
        return false;
    }

    private static readonly IReadOnlyDictionary<string, int> EmptyScores =
        new Dictionary<string, int>(StringComparer.Ordinal);
}
