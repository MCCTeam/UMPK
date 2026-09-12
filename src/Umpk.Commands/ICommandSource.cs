using Umpk.Text;

namespace Umpk.Commands;

/// <summary>The originator of a command invocation. UMPK command bodies reach their host or session through this seam instead of process-global state.</summary>
/// <remarks>A source carries the issuing session/host context (queryable through <see cref="GetService{T}"/>) and a reply sink (<see cref="ReplyAsync"/>). Argument-type suggestion providers receive the source so they can query live state without dereferencing ambient globals.</remarks>
public interface ICommandSource
{
    /// <summary>Sends a reply message back to the command issuer.</summary>
    ValueTask ReplyAsync(Component message, CancellationToken cancellationToken = default);

    /// <summary>Resolves a host/session service (for example the owning client), or <c>null</c> when absent.</summary>
    T? GetService<T>() where T : class;
}
