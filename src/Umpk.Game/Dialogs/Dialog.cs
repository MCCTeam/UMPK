using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Game.Dialogs;

/// <summary>The control kind of a dialog input, mapped from its <c>type</c> id.</summary>
public enum DialogInputKind
{
    /// <summary>An input whose <c>type</c> is not one of the modelled kinds.</summary>
    Unknown = 0,

    /// <summary>A free-text field (<c>minecraft:text</c>).</summary>
    Text = 1,

    /// <summary>A checkbox (<c>minecraft:boolean</c>).</summary>
    Boolean = 2,

    /// <summary>A one-of-many chooser (<c>minecraft:single_option</c>).</summary>
    SingleOption = 3,

    /// <summary>A numeric slider (<c>minecraft:number_range</c>).</summary>
    NumberRange = 4,
}

/// <summary>What a dialog button does when it is pressed, mapped from the action's <c>type</c> id.</summary>
public enum DialogActionKind
{
    /// <summary>No action, or an action whose <c>type</c> is not one of the modelled kinds.</summary>
    None = 0,

    /// <summary>Open a URL (<c>minecraft:open_url</c>).</summary>
    OpenUrl = 1,

    /// <summary>Run a chat command (<c>minecraft:run_command</c>).</summary>
    RunCommand = 2,

    /// <summary>Prefill the chat box (<c>minecraft:suggest_command</c>).</summary>
    SuggestCommand = 3,

    /// <summary>Turn to a book page (<c>minecraft:change_page</c>).</summary>
    ChangePage = 4,

    /// <summary>Copy text to the clipboard (<c>minecraft:copy_to_clipboard</c>).</summary>
    CopyToClipboard = 5,

    /// <summary>Open another dialog (<c>minecraft:show_dialog</c>).</summary>
    ShowDialog = 6,

    /// <summary>Send a custom-click action with a fixed payload (<c>minecraft:custom</c>).</summary>
    Custom = 7,

    /// <summary>Run a command built from a template plus the input values (<c>minecraft:dynamic/run_command</c>).</summary>
    DynamicRunCommand = 8,

    /// <summary>Send a custom-click action carrying the input values (<c>minecraft:dynamic/custom</c>).</summary>
    DynamicCustom = 9,
}

/// <summary>One element of a dialog body. Only the shape a consumer needs to render text is modelled; an item element keeps its raw compound so nothing is silently dropped.</summary>
/// <param name="Type">The element's <c>type</c> id (e.g. <c>minecraft:plain_message</c>).</param>
/// <param name="Contents">The message text, when the element carries one.</param>
/// <param name="Width">The element's declared width in pixels.</param>
/// <param name="Item">The raw item compound for an <c>minecraft:item</c> element, otherwise null.</param>
public sealed record DialogBodyElement(Identifier Type, Component? Contents, int Width, NbtCompound? Item = null);

/// <summary>One choice of a <see cref="DialogInputKind.SingleOption"/> input.</summary>
/// <param name="Id">The value submitted when this option is chosen.</param>
/// <param name="Display">The label shown for the option, when it differs from the id.</param>
/// <param name="IsInitial">Whether this option is preselected.</param>
public sealed record DialogOption(string Id, Component? Display, bool IsInitial);

/// <summary>The bounds of a <see cref="DialogInputKind.NumberRange"/> input.</summary>
/// <param name="Start">The lowest value.</param>
/// <param name="End">The highest value.</param>
/// <param name="Step">The step between values, when the server constrained one.</param>
/// <param name="Initial">The preselected value, when the server set one.</param>
public sealed record DialogNumberRange(float Start, float End, float? Step, float? Initial);

/// <summary>One input control of a dialog. <see cref="InitialValue"/> is the string the vanilla client would submit for this control before the user touches it, so a consumer can build a complete response without re-deriving per-kind defaults: a boolean resolves through its <c>on_true</c>/<c>on_false</c> strings, a single option through its preselected option id, and a number range through its initial (or start) value.</summary>
/// <param name="Key">The payload key this control writes to.</param>
/// <param name="Kind">The control kind.</param>
/// <param name="Type">The control's raw <c>type</c> id, kept so an unmodelled kind is still identifiable.</param>
/// <param name="Label">The control's label, when it has one.</param>
/// <param name="InitialValue">The value submitted when the control is left untouched.</param>
/// <param name="Options">The choices of a single-option control; empty otherwise.</param>
/// <param name="Range">The bounds of a number-range control; null otherwise.</param>
public sealed record DialogInput(
    string Key,
    DialogInputKind Kind,
    Identifier Type,
    Component? Label,
    string InitialValue,
    IReadOnlyList<DialogOption> Options,
    DialogNumberRange? Range);

/// <summary>What a dialog button triggers. <see cref="Value"/> carries the single string argument of the simple kinds (url, command, page, clipboard text, dialog id); <see cref="Id"/> and <see cref="Additions"/> carry the custom-click identity and its fixed payload members.</summary>
/// <param name="Kind">The action kind.</param>
/// <param name="Type">The action's raw <c>type</c> id, kept so an unmodelled kind is still identifiable.</param>
/// <param name="Value">The action's string argument, when its kind takes one.</param>
/// <param name="Id">The custom-click action id, for the custom and dynamic/custom kinds.</param>
/// <param name="Additions">Fixed payload members merged under the submitted input values.</param>
public sealed record DialogAction(
    DialogActionKind Kind,
    Identifier Type,
    string? Value,
    Identifier? Id,
    NbtCompound? Additions);

/// <summary>One pressable button of a dialog.</summary>
/// <param name="Label">The button label.</param>
/// <param name="Tooltip">The hover tooltip, when set.</param>
/// <param name="Width">The declared button width in pixels.</param>
/// <param name="Action">What pressing the button does; null for a button that only closes the dialog.</param>
public sealed record DialogButton(Component Label, Component? Tooltip, int Width, DialogAction? Action);

/// <summary>A server dialog as shown to the client (1.21.6+). The per-type button layouts are normalised into one <see cref="Buttons"/> list in wire order so a consumer renders every dialog type the same way: a notice contributes its single action button, a confirmation its yes then no, and a multi-action its action list. <see cref="ExitAction"/> is the button escape triggers, when the dialog declares one.</summary>
/// <param name="Type">The dialog's <c>type</c> id (e.g. <c>minecraft:multi_action</c>).</param>
/// <param name="Title">The dialog title.</param>
/// <param name="ExternalTitle">The shorter title used outside the dialog screen, when set.</param>
/// <param name="CanCloseWithEscape">Whether escape closes the dialog.</param>
/// <param name="Body">The body elements in order.</param>
/// <param name="Inputs">The input controls in order.</param>
/// <param name="Buttons">The pressable buttons in order.</param>
/// <param name="ExitAction">The button escape triggers, when declared.</param>
public sealed record Dialog(
    Identifier Type,
    Component Title,
    Component? ExternalTitle,
    bool CanCloseWithEscape,
    IReadOnlyList<DialogBodyElement> Body,
    IReadOnlyList<DialogInput> Inputs,
    IReadOnlyList<DialogButton> Buttons,
    DialogButton? ExitAction);
