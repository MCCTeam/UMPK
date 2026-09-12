using Umpk.Game.Items;

namespace Umpk.Game.Inventory;

/// <summary>The immutable input to <see cref="ClickSimulator"/>: the slot contents, the cursor stack, and the current state id. It is deliberately minimal, carrying no window id or menu metadata, so the simulator stays a pure function of container state and a click. The <see cref="ContainerView"/> read model wraps a snapshot with the window identity and layout.</summary>
public sealed class ContainerSnapshot
{
    private readonly ItemStack[] _slots;

    /// <summary>Builds a snapshot from slot contents, a cursor, and a state id.</summary>
    /// <param name="slots">The slot contents; index 0 is window slot 0. Empty stacks are allowed.</param>
    /// <param name="cursor">The carried (cursor) stack.</param>
    /// <param name="stateId">The container revision/state id (1.17.1+); 0 on older versions.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public ContainerSnapshot(IReadOnlyList<ItemStack> slots, ItemStack cursor, int stateId = 0)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(cursor);
        _slots = new ItemStack[slots.Count];
        for (int i = 0; i < slots.Count; i++)
            _slots[i] = slots[i] ?? ItemStack.Empty;

        Cursor = cursor;
        StateId = stateId;
    }

    /// <summary>The number of slots (excluding the cursor).</summary>
    public int SlotCount => _slots.Length;

    /// <summary>The carried (cursor) stack.</summary>
    public ItemStack Cursor { get; }

    /// <summary>The container state/revision id.</summary>
    public int StateId { get; }

    /// <summary>The contents of a slot; <see cref="ItemStack.Empty"/> for an empty or out-of-range slot.</summary>
    public ItemStack GetSlot(int index) =>
        index >= 0 && index < _slots.Length ? _slots[index] : ItemStack.Empty;

    /// <summary>Copies the slot contents into a new array (used by the simulator to build its scratch state).</summary>
    internal ItemStack[] CopySlots()
    {
        var copy = new ItemStack[_slots.Length];
        Array.Copy(_slots, copy, _slots.Length);
        return copy;
    }
}
