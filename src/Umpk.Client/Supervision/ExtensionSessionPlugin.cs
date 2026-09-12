using Umpk.Client.Plugins;

namespace Umpk.Client;

/// <summary>The <see cref="IClientPlugin"/> bridge for exactly one <see cref="IClientExtension"/>'s participation in exactly one session attempt. A fresh instance is created per attempt (per <see cref="UmpkClient"/>): it is added to that client's <see cref="ClientPluginCollection"/>, and its <see cref="Attach"/> raises <see cref="ClientExtensionContext.SessionStarted"/> once play is reached.</summary>
/// <remarks><see cref="Events.Disconnected"/> is published from <c>UmpkClient</c>'s receive loop when a session ends, but the plugin's own <see cref="ClientPluginContext.Detached"/> token only fires later, when the dead client is actually torn down (<c>DisconnectAsync</c>/<c>DisposeAsync</c>). So <see cref="ClientExtensionContext.SessionEnded"/> has two possible sources: the supervisor, which observes the session-end signal directly and calls <see cref="EndSessionOnce"/> before the client is released; and this bridge's <see cref="Attach"/>, which registers this as a backstop on <see cref="ClientPluginContext.Detached"/> for paths that never route through that signal (a local stop cancels the supervision loop before it ever reads the signal). Whichever source reaches it first wins; the interlocked guard makes the other one a no-op.</remarks>
internal sealed class ExtensionSessionPlugin : IClientPlugin
{
    private readonly ClientExtensionContext _context;
    private readonly UmpkClient _client;
    private readonly Action _onSessionEnded;
    private int _ended;

    internal ExtensionSessionPlugin(ClientExtensionContext context, UmpkClient client, Action onSessionEnded)
    {
        _context = context;
        _client = client;
        _onSessionEnded = onSessionEnded;
    }

    /// <inheritdoc/>
    public string Id => _context.Id;

    /// <inheritdoc/>
    public void Attach(ClientPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context.RaiseSessionStarted(context);
        context.Detached.Register(() => EndSessionOnce(_client.LastDisconnect));
    }

    /// <summary>Raises <see cref="ClientExtensionContext.SessionEnded"/> exactly once for this attempt.</summary>
    internal void EndSessionOnce(DisconnectInfo? info)
    {
        if (Interlocked.Exchange(ref _ended, 1) != 0)
            return;

        try
        {
            _context.RaiseSessionEnded(info);
        }
        finally
        {
            _onSessionEnded();
        }
    }
}
