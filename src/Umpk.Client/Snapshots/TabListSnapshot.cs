using Umpk.Game.Players;
using Umpk.Text;

namespace Umpk.Client.Snapshots;

/// <summary>One tab-list (player-info) entry.</summary>
/// <param name="Uuid">The player uuid.</param>
/// <param name="Name">The profile name.</param>
/// <param name="GameMode">The player's game mode.</param>
/// <param name="Latency">The measured latency in milliseconds, as last reported by the server.</param>
/// <param name="DisplayName">The display-name override, still a structured <see cref="Component"/>, or null when the entry falls back to <paramref name="Name"/>.</param>
/// <param name="Listed">Whether the entry is shown in the tab list (1.19.3+ "listed" flag).</param>
/// <param name="ListOrder">The list-order priority (1.21.2+); higher sorts first.</param>
public sealed record TabListEntrySnapshot(
    Guid Uuid, string Name, GameMode GameMode, int Latency, Component? DisplayName, bool Listed, int ListOrder)
{
    /// <summary>Projects a <see cref="TabListEntrySnapshot"/> from a live tab-list entry. Pure; no session loop involved.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is null.</exception>
    public static TabListEntrySnapshot Project(TabListEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new TabListEntrySnapshot(
            entry.Uuid, entry.Profile.Name, entry.GameMode, entry.Latency, entry.DisplayName, entry.Listed, entry.ListOrder);
    }
}

/// <summary>A tab-list snapshot with header and footer.</summary>
/// <param name="Entries">The entries, ordered the way vanilla's own tab-list overlay orders them: by <see cref="TabListEntrySnapshot.ListOrder"/> descending, spectators sorted after everyone else, then by <see cref="TabListEntrySnapshot.Name"/> case-insensitively. The team tie-break applies between the order and the name comparisons is not reproduced here: it would require cross-referencing <see cref="ScoreboardSnapshot"/>, which this record does not carry.</param>
/// <param name="Header">The tab-list header component, or null when unset.</param>
/// <param name="Footer">The tab-list footer component, or null when unset.</param>
public sealed record TabListSnapshot(IReadOnlyList<TabListEntrySnapshot> Entries, Component? Header, Component? Footer)
{
    /// <summary>Projects a <see cref="TabListSnapshot"/> from the live tab list. Pure; no session loop involved.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="list"/> is null.</exception>
    public static TabListSnapshot Project(TabList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        var entries = new List<TabListEntrySnapshot>(list.Count);
        foreach (TabListEntry entry in list.Entries)
            entries.Add(TabListEntrySnapshot.Project(entry));

        entries.Sort(CompareEntries);
        return new TabListSnapshot(entries, list.Header, list.Footer);
    }

    private static int CompareEntries(TabListEntrySnapshot a, TabListEntrySnapshot b)
    {
        int byOrder = b.ListOrder.CompareTo(a.ListOrder);
        if (byOrder != 0)
            return byOrder;

        int bySpectator = SpectatorRank(a.GameMode).CompareTo(SpectatorRank(b.GameMode));
        return bySpectator != 0 ? bySpectator : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static int SpectatorRank(GameMode mode) => mode == GameMode.Spectator ? 1 : 0;
}
