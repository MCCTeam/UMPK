using Umpk.Client.Actions;

namespace Umpk.Client;

/// <summary>The client-initiated action surface. Each sub-surface returns tasks that complete when the action's protocol exchange concludes. All actions are safe to call from any thread; they marshal onto the session loop where required.</summary>
public sealed class ClientActions
{
    internal ClientActions(
        ChatActions chat,
        MovementActions movement,
        InteractionActions interaction,
        InventoryActions inventory,
        SessionActions session,
        DialogActions dialog,
        ClientActionCapabilities capabilities)
    {
        Chat = chat;
        Movement = movement;
        Interaction = interaction;
        Inventory = inventory;
        Session = session;
        Dialog = dialog;
        Capabilities = capabilities;
    }

    /// <summary>Which version-optional actions the negotiated version can actually perform. Ask this before an action that documents <see cref="ActionNotSupportedException"/>, rather than sending and catching.</summary>
    public ClientActionCapabilities Capabilities { get; }

    /// <summary>Chat and command sending.</summary>
    public ChatActions Chat { get; }

    /// <summary>Movement, rotation, and navigation.</summary>
    public MovementActions Movement { get; }

    /// <summary>Block/entity interaction (dig, place, use, attack).</summary>
    public InteractionActions Interaction { get; }

    /// <summary>Inventory and container operations.</summary>
    public InventoryActions Inventory { get; }

    /// <summary>Session-level actions (client settings, respawn, abilities).</summary>
    public SessionActions Session { get; }

    /// <summary>Responding to a server dialog (1.21.6+).</summary>
    public DialogActions Dialog { get; }
}
