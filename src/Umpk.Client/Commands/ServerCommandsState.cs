namespace Umpk.Client.Commands;

/// <summary>The session's current server command tree, updated each time the server sends a <c>minecraft:commands</c> packet. Holds the most recent reconstructed <see cref="ServerCommandTree"/>, or null before the first declare-commands packet. Mutated on the session loop by the applier; read by the chat/command completion path.</summary>
public sealed class ServerCommandsState
{
    /// <summary>The current reconstructed server command tree, or null before the server sends one.</summary>
    public ServerCommandTree? Tree { get; internal set; }

    internal void Update(ServerCommandTree tree) => Tree = tree;
}
