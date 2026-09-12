using System.Diagnostics.CodeAnalysis;

namespace Umpk.Game.Players;

/// <summary>The per-session boss-bar state: the set of active boss bars keyed by uuid. The client-package boss-event handler adds, updates, and removes bars here.</summary>
public sealed class BossBarState
{
    private readonly Dictionary<Guid, BossBar> _bars = [];

    /// <summary>The active boss bars.</summary>
    public IReadOnlyCollection<BossBar> Bars => _bars.Values;

    /// <summary>The number of active bars.</summary>
    public int Count => _bars.Count;

    /// <summary>Adds or replaces a boss bar (the add operation).</summary>
    /// <exception cref="ArgumentNullException"><paramref name="bar"/> is null.</exception>
    public void Add(BossBar bar)
    {
        ArgumentNullException.ThrowIfNull(bar);
        _bars[bar.Uuid] = bar;
    }

    /// <summary>Gets the boss bar with the given uuid.</summary>
    public bool TryGet(Guid uuid, [NotNullWhen(true)] out BossBar? bar) => _bars.TryGetValue(uuid, out bar);

    /// <summary>Removes the boss bar with the given uuid (the remove operation); returns true when present.</summary>
    public bool Remove(Guid uuid) => _bars.Remove(uuid);

    /// <summary>Removes every boss bar.</summary>
    public void Clear() => _bars.Clear();
}
