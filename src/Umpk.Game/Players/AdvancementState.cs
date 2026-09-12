using System.Diagnostics.CodeAnalysis;

namespace Umpk.Game.Players;

/// <summary>The per-session advancement state: the known advancement definitions, per-advancement criterion progress (criterion name -&gt; obtained UTC timestamp, or null when not yet obtained), the selected tab, and the 1.21.11+ <c>showAdvancements</c> flag. Storage only, no logic: the client-package handler applies the packet's add/remove/progress payloads here.</summary>
public sealed class AdvancementState
{
    private readonly Dictionary<Identifier, Advancement> _advancements = [];
    private readonly Dictionary<Identifier, Dictionary<string, DateTimeOffset?>> _progress = [];

    /// <summary>The known advancement definitions.</summary>
    public IReadOnlyCollection<Advancement> Advancements => _advancements.Values;

    /// <summary>The currently selected advancement tab, if any (SelectAdvancementsTab).</summary>
    public Identifier? SelectedTab { get; set; }

    /// <summary>Whether the advancements screen is shown (the 1.21.11+ flag; true on older versions).</summary>
    public bool ShowAdvancements { get; set; } = true;

    /// <summary>Adds or replaces an advancement definition.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="advancement"/> is null.</exception>
    public void PutAdvancement(Advancement advancement)
    {
        ArgumentNullException.ThrowIfNull(advancement);
        _advancements[advancement.Id] = advancement;
    }

    /// <summary>Gets an advancement definition by id.</summary>
    public bool TryGetAdvancement(Identifier id, [NotNullWhen(true)] out Advancement? advancement) =>
        _advancements.TryGetValue(id, out advancement);

    /// <summary>Removes an advancement definition and its progress; returns true when present.</summary>
    public bool RemoveAdvancement(Identifier id)
    {
        _progress.Remove(id);
        return _advancements.Remove(id);
    }

    /// <summary>Sets the obtained timestamp for a criterion (null clears it), the shape the packet's progress map carries per advancement.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="criterion"/> is null.</exception>
    public void SetCriterionProgress(Identifier advancementId, string criterion, DateTimeOffset? obtainedAt)
    {
        ArgumentNullException.ThrowIfNull(criterion);
        if (!_progress.TryGetValue(advancementId, out Dictionary<string, DateTimeOffset?>? criteria))
        {
            criteria = [];
            _progress[advancementId] = criteria;
        }

        criteria[criterion] = obtainedAt;
    }

    /// <summary>Gets the per-criterion progress for an advancement (empty when none is tracked).</summary>
    public IReadOnlyDictionary<string, DateTimeOffset?> GetProgress(Identifier advancementId) =>
        _progress.TryGetValue(advancementId, out Dictionary<string, DateTimeOffset?>? criteria) ? criteria : EmptyProgress;

    /// <summary>Removes every advancement and all progress.</summary>
    public void Clear()
    {
        _advancements.Clear();
        _progress.Clear();
        SelectedTab = null;
    }

    private static readonly IReadOnlyDictionary<string, DateTimeOffset?> EmptyProgress =
        new Dictionary<string, DateTimeOffset?>();
}
