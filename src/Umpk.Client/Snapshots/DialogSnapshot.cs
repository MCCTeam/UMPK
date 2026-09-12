using Umpk.Client.State;
using Umpk.Game.Dialogs;

namespace Umpk.Client.Snapshots;

/// <summary>
/// An immutable snapshot of the dialog the server is currently showing (1.21.6+).
/// <para>The server may send either an inline dialog body or a reference into the dialog registry. An inline body projects <see cref="Dialog"/> non-null; a registry reference carries only an index, and the dialog registry is not modelled, so <see cref="RegistryId"/> is set and <see cref="Dialog"/> stays null. That is "a dialog is open that cannot be rendered", not "no dialog": <see cref="Project"/> returns non-null in both cases and null only when no dialog is showing at all.</para>
/// </summary>
/// <param name="Dialog">The dialog's parsed body, or null when the server referenced a registered dialog UMPK does not model.</param>
/// <param name="RegistryId">The registry index the server referenced instead of sending a body, or null for an inline dialog.</param>
public sealed record DialogSnapshot(Dialog? Dialog, int? RegistryId)
{
    /// <summary>Projects a <see cref="DialogSnapshot"/> from live tracked state, or null when no dialog is showing. Pure; no session loop involved. <see cref="Game.Dialogs.Dialog"/> is already immutable, so it is carried through rather than re-projected.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
    public static DialogSnapshot? Project(DialogState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.IsShowing ? new DialogSnapshot(state.Current, state.CurrentRegistryId) : null;
    }
}
