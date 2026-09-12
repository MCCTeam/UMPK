using Umpk.Nbt;

namespace Umpk.Text;

/// <summary>The action a <see cref="ClickEvent"/> performs. Values match vanilla serialized names across eras. <see cref="ShowDialog"/> and <see cref="Custom"/> exist only from 1.21.5 onward; the rest are present in every era.</summary>
public enum ClickEventAction
{
    /// <summary>Open a URL (<c>open_url</c>).</summary>
    OpenUrl,

    /// <summary>Open a file (<c>open_file</c>); client-only, never sent by servers.</summary>
    OpenFile,

    /// <summary>Run a command (<c>run_command</c>).</summary>
    RunCommand,

    /// <summary>Fill the chat box with a command (<c>suggest_command</c>).</summary>
    SuggestCommand,

    /// <summary>Turn to a book page (<c>change_page</c>).</summary>
    ChangePage,

    /// <summary>Copy text to the clipboard (<c>copy_to_clipboard</c>).</summary>
    CopyToClipboard,

    /// <summary>Open a registered dialog (<c>show_dialog</c>, 1.21.5+).</summary>
    ShowDialog,

    /// <summary>Emit a custom client event with an id and optional NBT payload (<c>custom</c>, 1.21.5+).</summary>
    Custom,
}

/// <summary>A click interaction attached to a component's <see cref="Style"/>. Two wire encodings exist: the pre-1.21.5 flat form (<c>{"action","value"}</c>) and the 1.21.5+ dispatched form where each action carries its own field (<c>open_url</c> -> <c>url</c>, <c>run_command</c>/<c>suggest_command</c> -> <c>command</c>, <c>change_page</c> -> integer <c>page</c>, <c>copy_to_clipboard</c>/<c>open_file</c> -> <c>value</c>/<c>path</c>, plus <c>show_dialog</c> and <c>custom</c>). This model is era-neutral: the string in <see cref="Value"/> holds the URL/command/page-text/dialog-id, and <see cref="Payload"/> holds the optional NBT for <see cref="ClickEventAction.Custom"/>.</summary>
public sealed record ClickEvent
{
    /// <summary>Creates a click event with a string value.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public ClickEvent(ClickEventAction action, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Action = action;
        Value = value;
        Payload = null;
    }

    /// <summary>Creates a <see cref="ClickEventAction.Custom"/> click event with an id and optional NBT payload.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> is null.</exception>
    public ClickEvent(string id, NbtTag? payload)
    {
        ArgumentNullException.ThrowIfNull(id);
        Action = ClickEventAction.Custom;
        Value = id;
        Payload = payload;
    }

    /// <summary>The action type.</summary>
    public ClickEventAction Action { get; }

    /// <summary>The action argument as text: the URL, command, page number as a string, dialog id, or the custom event id. For <see cref="ClickEventAction.ChangePage"/> this is the decimal page number.</summary>
    public string Value { get; }

    /// <summary>The optional NBT payload for <see cref="ClickEventAction.Custom"/>; null otherwise.</summary>
    public NbtTag? Payload { get; }

    /// <inheritdoc/>
    public bool Equals(ClickEvent? other) =>
        other is not null
        && Action == other.Action
        && string.Equals(Value, other.Value, StringComparison.Ordinal)
        && NbtEquals(Payload, other.Payload);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(Action, StringComparer.Ordinal.GetHashCode(Value), Payload);

    private static bool NbtEquals(NbtTag? a, NbtTag? b) =>
        (a is null && b is null) || (a is not null && a.Equals(b));
}
