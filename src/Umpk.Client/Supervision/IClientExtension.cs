namespace Umpk.Client;

/// <summary>A client extension: activated once by <see cref="ClientExtensionCollection.AddAsync"/> and, unlike an <see cref="Plugins.IClientPlugin"/>, kept alive across every reconnect a <see cref="UmpkClientSupervisor"/> makes. Everything session-relative (packet subscriptions, per-session state) is acquired through the <see cref="ClientExtensionContext.SessionCreated"/> / <see cref="ClientExtensionContext.SessionStarted"/> / <see cref="ClientExtensionContext.SessionEnded"/> events handed to <see cref="ActivateAsync"/> rather than held across the call, because the underlying <see cref="UmpkClient"/> instance changes on every reconnect.</summary>
public interface IClientExtension
{
    /// <summary>A stable identifier for the extension.</summary>
    string Id { get; }

    /// <summary>Called once, when the extension is added to a <see cref="ClientExtensionCollection"/>, whether or not a session is currently live. Wire up <paramref name="context"/>'s session events here rather than reaching for <see cref="ClientExtensionContext.Session"/> directly, since no session may be live yet (or ever).</summary>
    ValueTask ActivateAsync(ClientExtensionContext context, CancellationToken ct);

    /// <summary>Called once, when the extension is removed or the owning supervisor stops. The current session (if any) has already ended and <see cref="ClientExtensionContext.SessionEnded"/> has already fired by the time this runs. The default does nothing.</summary>
    ValueTask DeactivateAsync(CancellationToken ct) => ValueTask.CompletedTask;
}
