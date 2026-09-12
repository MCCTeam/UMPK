using Umpk.Game.Dialogs;

namespace Umpk.Client.State;

/// <summary>
/// The dialog the server is currently showing (1.21.6+), or nothing.
/// <para>The server may send either an inline dialog body or a reference into the dialog registry. An inline body is parsed into <see cref="Current"/>; a registry reference only carries an index, and the dialog registry is not modelled yet, so <see cref="CurrentRegistryId"/> is set and <see cref="Current"/> stays null. <see cref="IsShowing"/> is true in both cases, so a consumer can tell "a dialog is open that I cannot render" apart from "no dialog".</para>
/// <para>Storage only, no logic: the client-package applier writes here. On a version without dialogs nothing ever writes, so the state stays empty.</para>
/// </summary>
public sealed class DialogState
{
    /// <summary>The parsed dialog, or null when none is shown or the server sent a registry reference.</summary>
    public Dialog? Current { get; private set; }

    /// <summary>The registry index of the shown dialog, when the server sent a reference instead of a body.</summary>
    public int? CurrentRegistryId { get; private set; }

    /// <summary>Whether a dialog is open, whether or not its body could be resolved.</summary>
    public bool IsShowing => Current is not null || CurrentRegistryId is not null;

    internal void Show(Dialog? dialog, int? registryId)
    {
        Current = dialog;
        CurrentRegistryId = registryId;
    }

    internal void Clear()
    {
        Current = null;
        CurrentRegistryId = null;
    }
}
