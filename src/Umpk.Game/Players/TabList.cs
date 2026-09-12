using System.Diagnostics.CodeAnalysis;
using Umpk.Text;

namespace Umpk.Game.Players;

/// <summary>The tab-list state for player-info updates and header/footer packets. Entries are keyed by player UUID; the header and footer components are stored here too.</summary>
public sealed class TabList
{
    private readonly Dictionary<Guid, TabListEntry> _entries = [];

    /// <summary>The current entries, keyed by player uuid.</summary>
    public IReadOnlyCollection<TabListEntry> Entries => _entries.Values;

    /// <summary>The number of entries.</summary>
    public int Count => _entries.Count;

    /// <summary>The tab-list header component, if set.</summary>
    public Component? Header { get; set; }

    /// <summary>The tab-list footer component, if set.</summary>
    public Component? Footer { get; set; }

    /// <summary>Adds a new entry or returns the existing one for the same uuid (the Add-Player action). The per-field update actions (game mode, latency, display name, listed) mutate the returned entry. Returns the entry now tracked for that uuid.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is null.</exception>
    public TabListEntry Upsert(TabListEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entries[entry.Uuid] = entry;
        return entry;
    }

    /// <summary>Gets the entry for a player uuid.</summary>
    public bool TryGet(Guid uuid, [NotNullWhen(true)] out TabListEntry? entry) => _entries.TryGetValue(uuid, out entry);

    /// <summary>Removes the entry for a player uuid (the Remove-Player action); returns true when present.</summary>
    public bool Remove(Guid uuid) => _entries.Remove(uuid);

    /// <summary>Removes every entry and clears the header and footer.</summary>
    public void Clear()
    {
        _entries.Clear();
        Header = null;
        Footer = null;
    }
}
