using System.Globalization;
using Umpk.Nbt;
using Umpk.Text;
using Umpk.Text.Serialization;

namespace Umpk.Game.Dialogs;

/// <summary>
/// Reads the network-NBT dialog body carried by <c>minecraft:show_dialog</c> (1.21.6+) into the <see cref="Dialog"/> model.
/// <para>The read is deliberately tolerant: a dialog is server-authored data whose schema grows per version, so a missing or unexpected member falls back to its vanilla default rather than faulting the session. An unknown <c>type</c> still yields a usable dialog (its id is preserved) with whatever title, body, inputs and buttons it did carry.</para>
/// </summary>
public static class DialogNbt
{
    private const int DefaultBodyWidth = 200;
    private const int DefaultButtonWidth = 150;

    /// <summary>Reads a dialog from its NBT body, or null when the tag is not a compound.</summary>
    /// <param name="tag">The dialog body tag.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tag"/> is null.</exception>
    public static Dialog? Read(NbtTag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        if (tag is not NbtCompound root)
            return null;

        Identifier type = ReadIdentifier(root, "type") ?? new Identifier("umpk", "unknown");
        Component title = ReadComponent(root, "title") ?? Component.Text(string.Empty);
        Component? externalTitle = ReadComponent(root, "external_title");
        bool canCloseWithEscape = !root.ContainsKey("can_close_with_escape") || root.GetBool("can_close_with_escape");

        var body = new List<DialogBodyElement>();
        ReadElements(root, "body", body, ReadBodyElement);

        var inputs = new List<DialogInput>();
        ReadElements(root, "inputs", inputs, ReadInput);

        var buttons = new List<DialogButton>();

        // Per-type button layouts, normalised into one ordered list. notice: a single "action"; confirmation: "yes" then "no"; multi_action: "actions".
        if (ReadButton(root.GetCompound("action")) is { } notice)
            buttons.Add(notice);

        if (ReadButton(root.GetCompound("yes")) is { } yes)
            buttons.Add(yes);

        if (ReadButton(root.GetCompound("no")) is { } no)
            buttons.Add(no);

        if (root.GetList("actions") is { } actions)
            foreach (NbtTag element in actions)
                if (ReadButton(element as NbtCompound) is { } button)
                    buttons.Add(button);

        DialogButton? exit = ReadButton(root.GetCompound("exit_action"));
        return new Dialog(type, title, externalTitle, canCloseWithEscape, body, inputs, buttons, exit);
    }

    // A dialog list member may be either a list of compounds or a single inline compound (the vanilla codecs accept both for "body"), so both shapes are read.
    private static void ReadElements<T>(NbtCompound root, string key, List<T> into, Func<NbtCompound, T?> read)
        where T : class
    {
        if (root.GetList(key) is { } list)
        {
            foreach (NbtTag element in list)
                if (element is NbtCompound compound && read(compound) is { } value)
                    into.Add(value);

            return;
        }

        if (root.GetCompound(key) is { } single && read(single) is { } only)
            into.Add(only);

    }

    private static DialogBodyElement? ReadBodyElement(NbtCompound element)
    {
        Identifier type = ReadIdentifier(element, "type") ?? new Identifier("umpk", "unknown");
        int width = element.ContainsKey("width") ? element.GetInt("width") : DefaultBodyWidth;
        NbtCompound? item = element.GetCompound("item");

        // An item element carries its text under "description"; a plain message carries "contents".
        Component? contents = ReadComponent(element, "contents");
        if (contents is null && element.GetCompound("description") is { } description)
        {
            contents = ReadComponent(description, "contents");
            if (description.ContainsKey("width"))
                width = description.GetInt("width");

        }

        return new DialogBodyElement(type, contents, width, item);
    }

    private static DialogInput? ReadInput(NbtCompound element)
    {
        if (!element.TryGet("key", out NbtString? key))
            return null;

        Identifier type = ReadIdentifier(element, "type") ?? new Identifier("umpk", "unknown");
        DialogInputKind kind = type.Namespace == "minecraft"
            ? type.Path switch
            {
                "text" => DialogInputKind.Text,
                "boolean" => DialogInputKind.Boolean,
                "single_option" => DialogInputKind.SingleOption,
                "number_range" => DialogInputKind.NumberRange,
                _ => DialogInputKind.Unknown,
            }
            : DialogInputKind.Unknown;

        Component? label = ReadComponent(element, "label");
        var options = new List<DialogOption>();
        DialogNumberRange? range = null;
        string initial;

        switch (kind)
        {
            case DialogInputKind.Boolean:
                {
                    // The submitted value is on_true/on_false, not "true"/"false": a server can map the checkbox onto any pair of strings.
                    string onTrue = element.TryGet("on_true", out NbtString? t) ? t.Value : "true";
                    string onFalse = element.TryGet("on_false", out NbtString? f) ? f.Value : "false";
                    initial = element.GetBool("initial") ? onTrue : onFalse;
                    break;
                }

            case DialogInputKind.SingleOption:
                {
                    if (element.GetList("options") is { } list)
                        foreach (NbtTag entry in list)
                            if (entry is NbtCompound option && option.TryGet("id", out NbtString? id))
                                options.Add(new DialogOption(id.Value, ReadComponent(option, "display"), option.GetBool("initial")));

                    DialogOption? preselected = null;
                    foreach (DialogOption option in options)
                        if (option.IsInitial)
                        {
                            preselected = option;
                            break;
                        }

                    initial = preselected?.Id ?? string.Empty;
                    break;
                }

            case DialogInputKind.NumberRange:
                {
                    float start = element.GetFloat("start");
                    float end = element.GetFloat("end");
                    float? step = element.ContainsKey("step") ? element.GetFloat("step") : null;
                    float? seeded = element.ContainsKey("initial") ? element.GetFloat("initial") : null;
                    range = new DialogNumberRange(start, end, step, seeded);
                    initial = (seeded ?? start).ToString(CultureInfo.InvariantCulture);
                    break;
                }

            default:
                initial = element.TryGet("initial", out NbtString? text) ? text.Value : string.Empty;
                break;
        }

        return new DialogInput(key.Value, kind, type, label, initial, options, range);
    }

    private static DialogButton? ReadButton(NbtCompound? element)
    {
        if (element is null)
            return null;

        Component label = ReadComponent(element, "label") ?? Component.Text(string.Empty);
        Component? tooltip = ReadComponent(element, "tooltip");
        int width = element.ContainsKey("width") ? element.GetInt("width") : DefaultButtonWidth;
        return new DialogButton(label, tooltip, width, ReadAction(element.GetCompound("action")));
    }

    private static DialogAction? ReadAction(NbtCompound? element)
    {
        if (element is null)
            return null;

        Identifier type = ReadIdentifier(element, "type") ?? new Identifier("umpk", "unknown");
        (DialogActionKind kind, string? valueKey) = type.Namespace == "minecraft"
            ? type.Path switch
            {
                "open_url" => (DialogActionKind.OpenUrl, "url"),
                "run_command" => (DialogActionKind.RunCommand, "command"),
                "suggest_command" => (DialogActionKind.SuggestCommand, "command"),
                "change_page" => (DialogActionKind.ChangePage, "page"),
                "copy_to_clipboard" => (DialogActionKind.CopyToClipboard, "value"),
                "show_dialog" => (DialogActionKind.ShowDialog, "dialog"),
                "custom" => (DialogActionKind.Custom, null),
                "dynamic/run_command" => (DialogActionKind.DynamicRunCommand, "template"),
                "dynamic/custom" => (DialogActionKind.DynamicCustom, null),
                _ => (DialogActionKind.None, (string?)null),
            }
            : (DialogActionKind.None, null);

        string? value = null;
        if (valueKey is not null)
            if (element.TryGet(valueKey, out NbtString? text))
                value = text.Value;

            else if (element.TryGet(valueKey, out NbtNumeric? number))
                value = number.AsInt.ToString(CultureInfo.InvariantCulture);

        Identifier? id = ReadIdentifier(element, "id");
        NbtCompound? additions = element.GetCompound("additions") ?? element.GetCompound("payload");
        return new DialogAction(kind, type, value, id, additions);
    }

    private static Identifier? ReadIdentifier(NbtCompound element, string key) =>
        element.TryGet(key, out NbtString? text) && Identifier.TryParse(text.Value, out Identifier id) ? id : null;

    // A dialog body is always 1.21.6+ network NBT, so its components are read exactly the way vanilla reads them through NbtOps: a bare string tag is a literal, never re-parsed as JSON. The try/catch stays because a dialog is server-authored data whose shape can be wrong in ways that are not about strings (a malformed click event, say), and that must not fault the session.
    private static Component? ReadComponent(NbtCompound element, string key)
    {
        if (!element.TryGet(key, out NbtTag? tag))
            return null;

        try
        {
            return ComponentNbt.From(tag, ComponentWireEra.Modern);
        }
        catch (ComponentFormatException)
        {
            return null;
        }
    }
}
