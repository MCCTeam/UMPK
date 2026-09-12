using Microsoft.Extensions.Logging;
using Umpk.Client.Internal;
using Umpk.Game.Dialogs;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Actions;

/// <summary>
/// Responding to a server dialog (1.21.6+).
/// <para>A dialog response travels as <c>minecraft:custom_click_action</c>: the pressed button's action id plus a payload compound. The payload is the action's fixed <c>additions</c> with the dialog's input values written over the top, keyed by each input's <see cref="DialogInput.Key"/>, which is the shape a server reads its dialog answers from.</para>
/// <para>Every send is gated on the negotiated version actually carrying the packet, in the phase the connection is actually in. A version that cannot carry the answer raises <see cref="ActionNotSupportedException"/> rather than completing silently; ask <see cref="ClientActionCapabilities.CanSubmitDialog"/> to branch instead. Pressing a button that carries no custom-click action, or cancelling a dialog with no exit action, still sends nothing and still succeeds: those are "nothing to send", not "cannot send".</para>
/// </summary>
public sealed class DialogActions
{
    private static readonly Identifier CustomClickActionId = Identifier.Minecraft("custom_click_action");

    private readonly IPacketSink _sink;
    private readonly ClientSessionServices _services;
    private readonly ChatActions _chat;

    internal DialogActions(IPacketSink sink, ClientSessionServices services, ChatActions chat)
    {
        _sink = sink;
        _services = services;
        _chat = chat;
    }

    /// <summary>
    /// Presses the button at <paramref name="buttonIndex"/> of the shown dialog, submitting the dialog's input values. The dialog closes client-side on a press, as it does in vanilla.
    /// <para>The returned <see cref="DialogClickOutcome"/> says what the press actually DID, which a bare success flag cannot: only the custom-click kinds put anything on the wire through the dialog packet, and an unsupported action must not be reported as sent. A <c>run_command</c> button is performed here rather than dropped, because running the command is what a vanilla client does when that button is pressed.</para>
    /// </summary>
    /// <param name="buttonIndex">The zero-based index into the shown dialog's button list.</param>
    /// <param name="values">Input values keyed by input key; null submits each input's initial value.</param>
    /// <param name="ct">The cancellation token.</param>
    public async Task<DialogClickOutcome> ClickAsync(
        int buttonIndex, IReadOnlyDictionary<string, string>? values = null, CancellationToken ct = default)
    {
        IReadOnlyList<DialogButton>? buttons = _services.State.Dialogs.Current?.Buttons;
        DialogButton? button = buttons is not null && buttonIndex >= 0 && buttonIndex < buttons.Count
            ? buttons[buttonIndex]
            : null;

        if (button is null)
            return DialogClickOutcome.NoButton;

        // A custom-click identity is the only thing this client's dialog response can carry, so this branch is the only one that sends custom_click_action. It also clears the local dialog state.
        if (button.Action?.Id is not null)
        {
            await SubmitAsync(button, values, ct).ConfigureAwait(false);
            return DialogClickOutcome.ActionSent;
        }

        // Every remaining kind is a CLIENT-side action; SubmitAsync only closes the dialog for them. Close first (vanilla closes the screen, then acts), then perform the one this client can honestly do.
        await SubmitAsync(button, values, ct).ConfigureAwait(false);

        DialogClickOutcome outcome = ClassifyClientSideAction(button.Action);
        if (outcome == DialogClickOutcome.CommandSent)
            await _chat.SendCommandAsync(button.Action!.Value!, ct).ConfigureAwait(false);

        return outcome;
    }

    /// <summary>What a press of a button with no custom-click identity amounts to. <see cref="DialogClickOutcome.ClosedOnly"/> is a button that genuinely only closes the dialog, <see cref="DialogClickOutcome.CommandSent"/> carrying a command is the one client-side action this client performs (a vanilla client sends exactly that command), and every other kind is a client-side effect a headless client has no equivalent for: a URL to open, a chat box to prefill, a clipboard to write, a book page to turn, another dialog to show, or the templated <c>dynamic/run_command</c> form whose macro expansion this build does not implement. Those close the dialog and reach nobody, which is what a caller has to be told.</summary>
    /// <param name="action">The button's action, or null for a button with no action at all.</param>
    public static DialogClickOutcome ClassifyClientSideAction(DialogAction? action)
    {
        DialogActionKind kind = action?.Kind ?? DialogActionKind.None;
        if (kind == DialogActionKind.RunCommand && !string.IsNullOrWhiteSpace(action?.Value))
            return DialogClickOutcome.CommandSent;

        return kind == DialogActionKind.None ? DialogClickOutcome.ClosedOnly : DialogClickOutcome.ActionNotPerformed;
    }

    /// <summary>Submits the shown dialog by pressing <paramref name="button"/>, sending the button's custom-click action with the dialog's input values. The local dialog state is cleared, since pressing a button closes the dialog client-side.</summary>
    /// <param name="button">The button to press; its action must be a custom or dynamic/custom kind.</param>
    /// <param name="values">The input values to submit, keyed by input key. Null submits every input's <see cref="DialogInput.InitialValue"/>, which is what an untouched dialog would send.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="button"/> is null.</exception>
    public Task SubmitAsync(DialogButton button, IReadOnlyDictionary<string, string>? values = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(button);

        if (button.Action is not { } action || action.Id is not { } id)
        {
            // A button with no custom-click identity only closes the dialog client-side.
            _services.State.Dialogs.Clear();
            return Task.CompletedTask;
        }

        return SendAsync(id, action.Additions, values, ct);
    }

    /// <summary>Submits the shown dialog against an action id directly, for a caller that already knows the id (for instance one replaying a recorded response). The local dialog state is cleared.</summary>
    /// <param name="actionId">The custom-click action id.</param>
    /// <param name="values">The input values to submit, keyed by input key; null submits the dialog's defaults.</param>
    /// <param name="ct">The cancellation token.</param>
    public Task SubmitActionAsync(Identifier actionId, IReadOnlyDictionary<string, string>? values = null, CancellationToken ct = default) =>
        SendAsync(actionId, additions: null, values, ct);

    /// <summary>Cancels the shown dialog the way escape does: it triggers the dialog's exit action when it declares one, then clears the local dialog state. A dialog with no exit action sends nothing.</summary>
    /// <param name="ct">The cancellation token.</param>
    public Task CancelAsync(CancellationToken ct = default)
    {
        DialogAction? exit = _services.State.Dialogs.Current?.ExitAction?.Action;
        if (exit?.Id is not { } id)
        {
            _services.State.Dialogs.Clear();
            return Task.CompletedTask;
        }

        return SendAsync(id, exit.Additions, values: null, ct);
    }

    // custom_click_action is a common packet: it is registered in the configuration phase as well as in play, under its own wire id, and a server can gate the end of configuration on the answer. The response therefore has to be sent in the phase the connection is actually in; the two carry the same bytes but are separate packet identities, so the wrong one has no wire id in the live phase.
    private Task SendAsync(Identifier actionId, NbtCompound? additions, IReadOnlyDictionary<string, string>? values, CancellationToken ct)
    {
        bool configuring = _services.CurrentPhase() == ProtocolPhase.Configuration;
        if (!_services.Capabilities.CanSubmitDialog)
        {
            // Deliberately NOT a silent completed task: a dialog answer that never leaves is indistinguishable from one the server ignored, and a configuration-phase dialog can be gating the end of configuration.
            throw new ActionNotSupportedException(
                nameof(SubmitAsync),
                CustomClickActionId,
                _services.Version.Version.Protocol,
                $"Dialogs arrived at 1.21.6; the {(configuring ? "configuration" : "play")} phase has no sendable custom_click_action here.");
        }

        NbtCompound payload = BuildPayload(_services.State.Dialogs.Current, additions, values);
        _services.State.Dialogs.Clear();
        object packet = configuring
            ? new ServerboundConfigCustomClickActionPacket(actionId, payload)
            : new ServerboundCustomClickActionPacket(actionId, payload);
        return _sink.SendAsync(packet, ct).AsTask();
    }

    // Fixed additions first so an explicit input value of the same key wins, which is the precedence a server expects: additions are the button's constants, inputs are the user's answers.
    private static NbtCompound BuildPayload(Dialog? dialog, NbtCompound? additions, IReadOnlyDictionary<string, string>? values)
    {
        var payload = new NbtCompound();
        if (additions is not null)
            foreach (KeyValuePair<string, NbtTag> member in additions)
                payload.Put(member.Key, member.Value.Copy());

        if (dialog is not null)
            foreach (DialogInput input in dialog.Inputs)
            {
                string value = values is not null && values.TryGetValue(input.Key, out string? supplied)
                    ? supplied
                    : input.InitialValue;
                payload.PutString(input.Key, value);
            }

        if (values is null)
            return payload;

        // Values for keys the dialog did not declare (or when no dialog body was resolvable) still go through, so a caller driving a registry-referenced dialog is not blocked.
        foreach (KeyValuePair<string, string> entry in values)
            payload.PutString(entry.Key, entry.Value);

        return payload;
    }
}
