namespace Umpk.Game.Scoreboard;

/// <summary>A scoreboard display position, matching the vanilla display-slot ids used by <c>SetDisplayObjective</c> (list = 0, sidebar = 1, below-name = 2, and the 16 team-colored sidebar slots 3-18). Only the three primary slots are named; team-colored slots are represented by <see cref="SidebarTeam"/> plus a color index carried alongside.</summary>
public enum DisplaySlot
{
    /// <summary>The tab-list sidebar (player list).</summary>
    List = 0,

    /// <summary>The main sidebar.</summary>
    Sidebar = 1,

    /// <summary>Below the player name tag.</summary>
    BelowName = 2,

    /// <summary>A team-color-specific sidebar slot (the concrete color is a separate index, 0-15).</summary>
    SidebarTeam = 3,
}
