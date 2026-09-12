using Umpk.Text;

namespace Umpk.Game.Players;

/// <summary>The display metadata of an advancement, mirroring <c>DisplayInfo</c> on the <c>UpdateAdvancements</c> packet: title and description components, the frame type, and the display flags. The icon item is owned by the item module and kept as an opaque placeholder.</summary>
/// <param name="Title">The advancement title component.</param>
/// <param name="Description">The advancement description component.</param>
/// <param name="Frame">The frame type (task = 0, challenge = 1, goal = 2).</param>
/// <param name="ShowToast">Whether a toast is shown when the advancement is earned.</param>
/// <param name="Hidden">Whether the advancement is hidden until earned.</param>
/// <param name="Icon">The opaque icon item placeholder (owned by the item module), or null.</param>
public sealed record AdvancementDisplay(
    Component Title,
    Component Description,
    int Frame,
    bool ShowToast,
    bool Hidden,
    object? Icon);

/// <summary>One advancement definition from the <c>UpdateAdvancements</c> packet. Storage only: the parent id, optional display, the criterion names, and the requirement groups (the AND-of-ORs structure the packet carries). No progression logic lives here.</summary>
public sealed class Advancement
{
    /// <summary>Creates an advancement definition.</summary>
    /// <param name="id">The advancement id.</param>
    /// <param name="parentId">The parent advancement id, or null for a root.</param>
    /// <param name="display">The display metadata, or null when the advancement is not displayed.</param>
    /// <param name="criteria">The criterion names.</param>
    /// <param name="requirements">The requirement groups (each inner list is an OR group; all groups must be met).</param>
    /// <exception cref="ArgumentNullException"><paramref name="criteria"/> or <paramref name="requirements"/> is null.</exception>
    public Advancement(
        Identifier id,
        Identifier? parentId,
        AdvancementDisplay? display,
        IReadOnlyList<string> criteria,
        IReadOnlyList<IReadOnlyList<string>> requirements)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(requirements);
        Id = id;
        ParentId = parentId;
        Display = display;
        Criteria = criteria;
        Requirements = requirements;
    }

    /// <summary>The advancement id.</summary>
    public Identifier Id { get; }

    /// <summary>The parent advancement id, or null for a root.</summary>
    public Identifier? ParentId { get; }

    /// <summary>The display metadata, or null when not displayed.</summary>
    public AdvancementDisplay? Display { get; }

    /// <summary>The criterion names.</summary>
    public IReadOnlyList<string> Criteria { get; }

    /// <summary>The requirement groups (AND of ORs).</summary>
    public IReadOnlyList<IReadOnlyList<string>> Requirements { get; }
}
