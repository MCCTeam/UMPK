namespace Umpk.Client;

/// <summary>One dial the supervisor is about to make: the target, its position in the retry sequence, and what ended the previous session (when this is a retry). Handed to <see cref="IClientSessionFactory.CreateAsync"/>, which builds (but does not connect) the <see cref="UmpkClient"/> the supervisor will then dial.</summary>
public sealed class SessionAttempt
{
    private readonly Action _reportAuthenticating;

    internal SessionAttempt(
        ServerEndpoint endpoint,
        int attempt,
        DisconnectInfo? previous,
        Action reportAuthenticating,
        bool isTransfer = false)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(reportAuthenticating);
        Endpoint = endpoint;
        Attempt = attempt;
        PreviousDisconnect = previous;
        IsTransfer = isTransfer;
        _reportAuthenticating = reportAuthenticating;
    }

    /// <summary>The address this attempt dials, which is what <see cref="Redirect"/> changes.</summary>
    public ServerEndpoint Endpoint { get; private set; }

    /// <summary>0 for the first attempt of a fresh dial (a <c>StartAsync</c> or a <c>ReconnectAsync</c>); 1, 2, ... for each automatic retry after it.</summary>
    public int Attempt { get; }

    /// <summary>Why the previous session ended, when this attempt is an automatic retry. Null on attempt 0.</summary>
    public DisconnectInfo? PreviousDisconnect { get; }

    /// <summary>Whether this dial was requested by a server transfer packet.</summary>
    public bool IsTransfer { get; }

    /// <summary>Announces <see cref="ClientStatus.Authenticating"/> on the supervisor. Call this only while the factory is actually authenticating (resolving or refreshing credentials, running an interactive sign-in): an offline session that never authenticates must never claim the step, or a host's status line would show a step that did not happen.</summary>
    public void ReportAuthenticating() => _reportAuthenticating();

    /// <summary>
    /// Sends this attempt to a different address. The supervisor reads <see cref="Endpoint"/> after <see cref="IClientSessionFactory.CreateAsync"/> returns, so a factory that decides the target itself (a proxy pool, a host hook a plugin can answer) says so here instead of building a client the supervisor then dials at the old address.
    /// <para>One attempt only. The supervisor keeps the address it was started with, so the next automatic retry begins from there and has to be redirected again.</para>
    /// </summary>
    public void Redirect(ServerEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        Endpoint = endpoint;
    }
}
