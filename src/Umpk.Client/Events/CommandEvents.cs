using Umpk.Client.Commands;

namespace Umpk.Client.Events;

/// <summary>Raised when the server sends a new command tree (<c>minecraft:commands</c>) and it has been reconstructed. Carries the reconstructed tree; the same instance is stored on <c>ClientState.ServerCommands.Tree</c>.</summary>
public sealed record ServerCommandTreeUpdated(ServerCommandTree Tree) : IClientEvent;
