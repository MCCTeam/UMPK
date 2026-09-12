namespace Umpk.Commands;

/// <summary>A scoped registration handle. Every command registered through a scope is unregistered together when the scope is disposed, by rebuilding the underlying dispatcher from the surviving scopes. UMPK never mutates a live command tree to remove nodes, and never reflects into the dispatcher.</summary>
/// <typeparam name="TSource">The command source type.</typeparam>
public interface ICommandRegistrationScope<TSource> : IDisposable
    where TSource : ICommandSource
{
    /// <summary>An identifier for the scope owner (for diagnostics).</summary>
    string OwnerId { get; }

    /// <summary>Registers one or more top-level commands into this scope.</summary>
    /// <remarks>Triggers a dispatcher rebuild so the new commands become dispatchable immediately.</remarks>
    void Register(Action<CommandBuilder<TSource>> build);
}
