using Umpk.Text;

namespace Umpk.Game.Scoreboard;

/// <summary>A scoreboard team: its internal name, the display/prefix/suffix components, its option flags and rules, its name-tag color, and its member set. The <c>SetPlayerTeam</c> packet's create/update/add-member/remove-member operations map onto the mutators here; membership is a set of entry strings (player names or entity uuids as strings).</summary>
public sealed class Team
{
    private readonly HashSet<string> _members = new(StringComparer.Ordinal);

    /// <summary>Creates a team with its internal name; other fields default and are set by the create operation.</summary>
    /// <param name="name">The stable internal team name (the wire key).</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public Team(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        DisplayName = Component.Text(name);
        Prefix = Component.Empty;
        Suffix = Component.Empty;
    }

    /// <summary>The stable internal name; the identity used across team packets.</summary>
    public string Name { get; }

    /// <summary>The team display name.</summary>
    public Component DisplayName { get; set; }

    /// <summary>The prefix prepended to member names.</summary>
    public Component Prefix { get; set; }

    /// <summary>The suffix appended to member names.</summary>
    public Component Suffix { get; set; }

    /// <summary>The option flags (friendly fire, see friendly invisibles).</summary>
    public TeamFlags Flags { get; set; }

    /// <summary>The name-tag visibility rule.</summary>
    public NameTagVisibility NameTagVisibility { get; set; } = NameTagVisibility.Always;

    /// <summary>The collision rule.</summary>
    public CollisionRule CollisionRule { get; set; } = CollisionRule.Always;

    /// <summary>The team color id (0-15 for the sixteen named colors, 21 for reset/none). Kept as the raw id because the wire value includes non-color formats.</summary>
    public int Color { get; set; } = 21;

    /// <summary>Whether teammates can damage each other.</summary>
    public bool AllowFriendlyFire => (Flags & TeamFlags.AllowFriendlyFire) != 0;

    /// <summary>Whether teammates can see each other while invisible.</summary>
    public bool CanSeeFriendlyInvisibles => (Flags & TeamFlags.CanSeeFriendlyInvisibles) != 0;

    /// <summary>The current members (player names or stringified entity uuids).</summary>
    public IReadOnlyCollection<string> Members => _members;

    /// <summary>Adds a member; returns true when it was newly added.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is null.</exception>
    public bool AddMember(string entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return _members.Add(entry);
    }

    /// <summary>Removes a member; returns true when it was present.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is null.</exception>
    public bool RemoveMember(string entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return _members.Remove(entry);
    }

    /// <summary>True when the given entry belongs to this team.</summary>
    public bool HasMember(string entry) => _members.Contains(entry);
}
