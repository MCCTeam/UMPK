using Umpk.Game.Items;

namespace Umpk.Game.Inventory;

/// <summary>One predicted slot change a client reports with its click packet.</summary>
/// <param name="Slot">The window slot index.</param>
/// <param name="Item">The predicted new contents (may be <see cref="ItemStack.Empty"/>).</param>
public readonly record struct SlotChange(int Slot, ItemStack Item);

/// <summary>The output of <see cref="ClickSimulator.Apply"/>. It carries the predicted next container state, the new cursor, the changed-slot list a client sends alongside the click, and whether the click touched a server-managed output slot (in which case the real result is authoritative and the prediction is best-effort). The simulator is deterministic and does no I/O.</summary>
public sealed class ClickResult
{
    internal ClickResult(
        ContainerSnapshot state,
        IReadOnlyList<SlotChange> changedSlots,
        bool touchedServerAuthoritativeSlot,
        IReadOnlyList<ItemStack> droppedItems)
    {
        State = state;
        ChangedSlots = changedSlots;
        TouchedServerAuthoritativeSlot = touchedServerAuthoritativeSlot;
        DroppedItems = droppedItems;
    }

    /// <summary>The predicted container state after the click (slots, cursor, unchanged state id).</summary>
    public ContainerSnapshot State { get; }

    /// <summary>The new cursor stack (a convenience mirror of <c>State.Cursor</c>).</summary>
    public ItemStack Cursor => State.Cursor;

    /// <summary>The slots whose contents changed, as the client reports them with the click.</summary>
    public IReadOnlyList<SlotChange> ChangedSlots { get; }

    /// <summary>True when the click read from or wrote to a server-managed output slot (crafting result, anvil output, ...). The prediction of such slots is best-effort; the authoritative contents arrive in a following server packet and the caller reconciles.</summary>
    public bool TouchedServerAuthoritativeSlot { get; }

    /// <summary>The stacks the click dropped into the world (throw/drag remainder overflow), in order.</summary>
    public IReadOnlyList<ItemStack> DroppedItems { get; }
}
