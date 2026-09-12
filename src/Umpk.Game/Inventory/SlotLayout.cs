namespace Umpk.Game.Inventory;

/// <summary>Per-container-type slot semantics from per-version menu data. It carries the slot count, the role of each slot, and the shift-click target ranges per source slot. Instances are immutable and produced by an <see cref="ISlotLayoutSource"/> keyed by menu type.</summary>
public sealed class SlotLayout
{
    /// <summary>The default per-slot stack limit of 64.</summary>
    public const int DefaultSlotStackLimit = 64;

    private readonly SlotRole[] _roles;
    private readonly IReadOnlyList<SlotRange>[] _quickMoveTargets;
    private readonly int[]? _slotStackLimits;

    /// <summary>Builds a layout from a per-slot role array and a per-slot quick-move target table.</summary>
    /// <param name="roles">The role of each slot; its length is the slot count.</param>
    /// <param name="quickMoveTargets">For each slot index, the ordered target ranges a shift-click from that slot tries, in order. A slot with no entry (or an empty list) is not shift-movable.</param>
    /// <param name="slotStackLimits">Optional per-slot stack limits. Null means every slot uses <see cref="DefaultSlotStackLimit"/>; menus with capped slots (beacon payment = 1, brewing ingredient/bottle slots) supply the array.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentException">The arrays disagree on slot count.</exception>
    public SlotLayout(
        IReadOnlyList<SlotRole> roles,
        IReadOnlyList<IReadOnlyList<SlotRange>> quickMoveTargets,
        IReadOnlyList<int>? slotStackLimits = null)
    {
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(quickMoveTargets);
        if (roles.Count != quickMoveTargets.Count)
            throw new ArgumentException("Role and quick-move-target arrays must have the same slot count.", nameof(quickMoveTargets));

        if (slotStackLimits is not null && slotStackLimits.Count != roles.Count)
            throw new ArgumentException("Slot stack limit array must have the same slot count as the roles.", nameof(slotStackLimits));

        _roles = [.. roles];
        _quickMoveTargets = [.. quickMoveTargets];
        _slotStackLimits = slotStackLimits is null ? null : [.. slotStackLimits];
    }

    /// <summary>The number of slots in the window (excluding the cursor).</summary>
    public int SlotCount => _roles.Length;

    /// <summary>The role of a slot.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is out of range.</exception>
    public SlotRole RoleOf(int slot)
    {
        if (slot < 0 || slot >= _roles.Length)
            throw new ArgumentOutOfRangeException(nameof(slot));

        return _roles[slot];
    }

    /// <summary>True when a slot is a server-managed output/result slot (crafting result, anvil output, ...).</summary>
    public bool IsOutputSlot(int slot) => slot >= 0 && slot < _roles.Length && _roles[slot] == SlotRole.Output;

    /// <summary>The ordered shift-click target ranges for a source slot; empty when the slot is not shift-movable.</summary>
    public IReadOnlyList<SlotRange> QuickMoveTargets(int slot) =>
        slot >= 0 && slot < _quickMoveTargets.Length ? _quickMoveTargets[slot] : [];

    /// <summary>The per-slot stack limit; item-specific max stack sizes are applied on top by the simulator. Out-of-range slots report the default.</summary>
    public int SlotStackLimit(int slot) =>
        _slotStackLimits is not null && slot >= 0 && slot < _slotStackLimits.Length
            ? _slotStackLimits[slot]
            : DefaultSlotStackLimit;
}
