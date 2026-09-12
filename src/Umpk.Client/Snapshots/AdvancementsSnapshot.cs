using Umpk.Game.Players;
using Umpk.Text;

namespace Umpk.Client.Snapshots;

/// <summary>One advancement and its completion progress.</summary>
/// <param name="Id">The advancement id.</param>
/// <param name="ParentId">The parent advancement id, or null for a root.</param>
/// <param name="Title">The advancement title component, or null when the advancement is not displayed.</param>
/// <param name="Description">The advancement description component, or null when the advancement is not displayed.</param>
/// <param name="Frame">
/// The frame type (task = 0, challenge = 1, goal = 2), or null when the advancement is not displayed.
/// <para>Nullable on purpose. The frame lives inside the optional display block, which generated recipe advancements omit entirely. Such an advancement genuinely has no frame, so defaulting to 0 would publish "task" as though the server had said it. The codec's task default applies only when an existing display block omits its <c>frame</c> field.</para>
/// </param>
/// <param name="CriteriaCount">The total number of criteria.</param>
/// <param name="CriteriaCompleted">The number of criteria obtained so far.</param>
public sealed record AdvancementSnapshot(
    Identifier Id,
    Identifier? ParentId,
    Component? Title,
    Component? Description,
    int? Frame,
    int CriteriaCount,
    int CriteriaCompleted);

/// <summary>An advancements snapshot: the selected tab and every known advancement with its progress.</summary>
/// <param name="SelectedTab">The currently selected advancement tab, or null.</param>
/// <param name="Entries">The known advancements. Structured advancement contents decode only on protocols 770-773 and 26.2; on every other band UMPK relays the <c>UpdateAdvancements</c> packet verbatim and this stays empty: an honestly empty set, not a thrown error or a silently wrong one.</param>
public sealed record AdvancementsSnapshot(Identifier? SelectedTab, IReadOnlyList<AdvancementSnapshot> Entries)
{
    /// <summary>Projects an <see cref="AdvancementsSnapshot"/> from live tracked state. Pure; no session loop involved.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
    public static AdvancementsSnapshot Project(AdvancementState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var entries = new List<AdvancementSnapshot>(state.Advancements.Count);
        foreach (Advancement advancement in state.Advancements)
        {
            IReadOnlyDictionary<string, DateTimeOffset?> progress = state.GetProgress(advancement.Id);
            int completed = 0;
            foreach (KeyValuePair<string, DateTimeOffset?> criterion in progress)
                if (criterion.Value is not null)
                    completed++;

            entries.Add(new AdvancementSnapshot(
                advancement.Id,
                advancement.ParentId,
                advancement.Display?.Title,
                advancement.Display?.Description,
                advancement.Display?.Frame,
                advancement.Criteria.Count,
                completed));
        }

        return new AdvancementsSnapshot(state.SelectedTab, entries);
    }
}
