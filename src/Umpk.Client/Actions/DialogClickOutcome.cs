namespace Umpk.Client.Actions;

/// <summary>What pressing a dialog button actually DID. A bare success flag cannot say this: only the custom-click kinds travel on the dialog response packet. Unsupported actions must not be reported as sent.</summary>
public enum DialogClickOutcome
{
    /// <summary>No dialog is open, or the index named no button. Nothing was sent and nothing was closed.</summary>
    NoButton = 0,

    /// <summary>The button's custom-click action and the input values were sent to the server.</summary>
    ActionSent = 1,

    /// <summary>The button's <c>run_command</c> command was sent, the way a vanilla client sends it.</summary>
    CommandSent = 2,

    /// <summary>The button declares no action at all, so the press only closed the dialog, as in vanilla.</summary>
    ClosedOnly = 3,

    /// <summary>The button declares a client-side action this client does not perform (open a url, suggest a command, copy to the clipboard, turn a page, open another dialog, or a templated <c>dynamic/run_command</c>). The dialog closed and NOTHING reached the server.</summary>
    ActionNotPerformed = 4,
}
