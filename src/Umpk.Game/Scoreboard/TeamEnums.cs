namespace Umpk.Game.Scoreboard;

/// <summary>A team's name-tag visibility rule, matching the wire strings (<c>always</c>, <c>never</c>, <c>hideForOtherTeams</c>, <c>hideForOwnTeam</c>).</summary>
public enum NameTagVisibility
{
    /// <summary>Name tags are always visible.</summary>
    Always,

    /// <summary>Name tags are never visible.</summary>
    Never,

    /// <summary>Name tags are hidden from members of other teams.</summary>
    HideForOtherTeams,

    /// <summary>Name tags are hidden from members of the same team.</summary>
    HideForOwnTeam,
}

/// <summary>A team's collision rule, matching the wire strings (<c>always</c>, <c>never</c>, <c>pushOtherTeams</c>, <c>pushOwnTeam</c>).</summary>
public enum CollisionRule
{
    /// <summary>Members always collide with others.</summary>
    Always,

    /// <summary>Members never collide with others.</summary>
    Never,

    /// <summary>Members push only members of other teams.</summary>
    PushOtherTeams,

    /// <summary>Members push only members of the same team.</summary>
    PushOwnTeam,
}

/// <summary>The team option flag bits (friendly fire = 1, see friendly invisibles = 2) from the <c>SetPlayerTeam</c> packet.</summary>
[Flags]
public enum TeamFlags : byte
{
    /// <summary>No flags set.</summary>
    None = 0,

    /// <summary>Members can damage teammates.</summary>
    AllowFriendlyFire = 1,

    /// <summary>Members can see invisible teammates.</summary>
    CanSeeFriendlyInvisibles = 2,
}
