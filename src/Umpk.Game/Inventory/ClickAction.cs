namespace Umpk.Game.Inventory;

/// <summary>The mouse button a pickup, drag, or throw uses. The protocol codec owns its wire encoding.</summary>
public enum MouseButton
{
    /// <summary>Left mouse button (vanilla button 0 / primary).</summary>
    Left = 0,

    /// <summary>Right mouse button (vanilla button 1 / secondary).</summary>
    Right = 1,

    /// <summary>Middle mouse button (vanilla button 2; drag/clone).</summary>
    Middle = 2,
}

/// <summary>The stage of a quick-craft (drag) gesture (vanilla QUICK_CRAFT header 0/1/2).</summary>
public enum DragStage
{
    /// <summary>Begin the drag; picks the drag type from the button and clears the slot set (header 0).</summary>
    Start,

    /// <summary>Add a slot to the drag set (header 1).</summary>
    Add,

    /// <summary>Finish the drag and distribute the cursor across the collected slots (header 2).</summary>
    End,
}

/// <summary>A semantic container click. This is the pure-model input to <see cref="ClickSimulator"/>; the codec maps each case to its <c>(mode, button)</c> pair.</summary>
public abstract record ClickAction
{
    private ClickAction()
    {
    }

    /// <summary>PICKUP: place/pick up a stack at a slot with the left or right button (vanilla mode 0).</summary>
    /// <param name="Slot">The clicked slot, or -999 for a click outside the window (cursor throw).</param>
    /// <param name="Button">Left or right.</param>
    public sealed record Pickup(int Slot, MouseButton Button) : ClickAction;

    /// <summary>QUICK_MOVE (shift-click): move a stack to its target range (vanilla mode 1).</summary>
    /// <param name="Slot">The source slot.</param>
    /// <param name="Button">Left or right; vanilla treats both identically for quick-move.</param>
    public sealed record QuickMove(int Slot, MouseButton Button) : ClickAction;

    /// <summary>SWAP: swap a slot with a hotbar slot (0..8) or the offhand (vanilla mode 2).</summary>
    /// <param name="Slot">The target slot.</param>
    /// <param name="HotbarButton">The hotbar index 0..8, or 40 for the offhand.</param>
    public sealed record Swap(int Slot, int HotbarButton) : ClickAction;

    /// <summary>CLONE: creative middle-click clone of a slot into the cursor (vanilla mode 3).</summary>
    /// <param name="Slot">The cloned slot.</param>
    public sealed record CloneSlot(int Slot) : ClickAction;

    /// <summary>THROW: drop one item (button 0) or the whole stack (button 1) from a slot (vanilla mode 4).</summary>
    /// <param name="Slot">The slot to drop from.</param>
    /// <param name="WholeStack">True to drop the entire stack (button 1), false to drop one (button 0).</param>
    public sealed record Throw(int Slot, bool WholeStack) : ClickAction;

    /// <summary>QUICK_CRAFT (drag): a single stage of a left/right/middle drag gesture (vanilla mode 5). The wire carries three stages (start/add/end); the codec layer emits one packet per stage. Prediction only happens on <see cref="DragStage.End"/>, which carries the full accumulated slot set (vanilla shows only a preview until release, and the container state changes at release). The simulator is pure, so the caller threads the collected <see cref="Slots"/> into the end action rather than the simulator retaining cross-call state.</summary>
    /// <param name="Stage">The gesture stage.</param>
    /// <param name="Button">Which drag button (Left even distribution, Right one-each, Middle creative full).</param>
    /// <param name="Slot">The slot added on an <see cref="DragStage.Add"/> stage; ignored otherwise.</param>
    /// <param name="Slots">On <see cref="DragStage.End"/>, the ordered set of slots the gesture collected.</param>
    public sealed record Drag(DragStage Stage, MouseButton Button, int Slot = -999, IReadOnlyList<int>? Slots = null) : ClickAction;

    /// <summary>PICKUP_ALL (double-click): gather matching items into the cursor (vanilla mode 6). Button 0 gathers scanning the window forward; button 1 scans backward from the last slot, which changes which slots are drained when the cursor fills up.</summary>
    /// <param name="Slot">The double-clicked slot whose cursor gathers matches.</param>
    /// <param name="Button">Left (button 0, forward pass) or right (button 1, reverse pass).</param>
    public sealed record PickupAll(int Slot, MouseButton Button = MouseButton.Left) : ClickAction;
}
