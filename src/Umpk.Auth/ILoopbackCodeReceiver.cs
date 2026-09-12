namespace Umpk.Auth;

/// <summary>Captures the OAuth authorization code redirected to a loopback address during the browser flow. The default implementation runs a one-shot local HTTP listener; tests substitute a fake. The receiver binds first (yielding the concrete redirect URI including the assigned port), then waits for the redirect.</summary>
public interface ILoopbackCodeReceiver : IAsyncDisposable
{
    /// <summary>Binds a loopback endpoint and returns the actual redirect URI to send to the authorize call (the OS may have assigned a port). Must be called before <see cref="WaitForCodeAsync"/>.</summary>
    /// <param name="requestedRedirectUri">The redirect URI the caller wants served.</param>
    /// <param name="expectedState">The CSRF <c>state</c> the awaited redirect must carry. It is supplied HERE rather than to <see cref="WaitForCodeAsync"/> because it has to gate the handover at the moment a request arrives. A receiver that gives its one-shot slot to the first request reaching the right PATH and validates the state only afterwards can be robbed of that slot by any local process sending a single request with a wrong or absent state, which aborts the user's legitimate sign-in.</param>
    /// <remarks>Called before the caller opens a browser, so an endpoint that cannot be served must fail here: implementations report that as an <see cref="AuthException"/> naming the reason, never as a raw transport exception.</remarks>
    /// <exception cref="AuthException">The endpoint cannot be bound or cannot be served.</exception>
    Uri Start(Uri requestedRedirectUri, string expectedState);

    /// <summary>Waits for a browser redirect carrying the <c>state</c> given to <see cref="Start"/>, and returns its authorization code.</summary>
    /// <remarks>A request that does not carry that state is refused by the implementation and does NOT complete this wait, so a caller that only ever receives those waits until <paramref name="ct"/> is cancelled. That is the correct outcome: no legitimate redirect has arrived yet.</remarks>
    Task<string> WaitForCodeAsync(CancellationToken ct);
}

/// <summary>Creates <see cref="ILoopbackCodeReceiver"/> instances for the browser flow.</summary>
public interface ILoopbackCodeReceiverFactory
{
    ILoopbackCodeReceiver Create();
}
