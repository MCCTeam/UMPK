namespace Umpk.Auth;

/// <summary>Supplies the <see cref="HttpMessageHandler"/> that every auth HTTP call flows through. A single injected handler replaces ad-hoc proxy paths and lets tests script responses. The factory owns the handler lifetime; the auth flow does not dispose returned handlers.</summary>
public interface IHttpMessageHandlerFactory
{
    /// <summary>Returns the handler to use for auth and session HTTP. The same instance may be reused.</summary>
    HttpMessageHandler CreateHandler();
}
