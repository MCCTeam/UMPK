using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Umpk.Auth;

/// <summary>Configuration for a <see cref="MinecraftAuthFlow"/>: which flow to run, the Azure client id, the HTTP handler seam, the token store, the time source, and the logger. Immutable after construction; each property has a documented default.</summary>
public sealed class MinecraftAuthOptions
{
    /// <summary>The redirect URI registered against the default <see cref="ClientId"/> for the browser flow.</summary>
    /// <remarks>This is the redirect URI registered for the default public client. Applications that do not own that registration cannot substitute another URI. The page reads the authorization code from the URL fragment and displays it for the user to paste back, so the code is never sent to a server.</remarks>
    public static Uri DefaultBrowserRedirectUri { get; } = new("https://mccteam.github.io/redirect.html");

    /// <summary>The Azure application id used for Microsoft flows. Defaults to the registered public client id.</summary>
    public string ClientId { get; init; } = "54473e32-df8f-42e9-a649-9419b0dab9d3";

    /// <summary>The login flow to run. Defaults to <see cref="AuthFlowKind.MicrosoftDeviceCode"/>.</summary>
    public AuthFlowKind FlowKind { get; init; } = AuthFlowKind.MicrosoftDeviceCode;

    /// <summary>Base URL for a third-party Yggdrasil provider (authlib-injector), used by the Yggdrasil flow and by <see cref="Session.YggdrasilSessionService"/>. Null uses the Mojang defaults.</summary>
    public Uri? YggdrasilBaseUrl { get; init; }

    /// <summary>The name used for offline logins. Required when <see cref="FlowKind"/> is <see cref="AuthFlowKind.Offline"/>; ignored otherwise.</summary>
    public string? OfflineUsername { get; init; }

    /// <summary>The redirect URI used by the browser flow. Defaults to <see cref="DefaultBrowserRedirectUri"/>, the hosted page registered for <see cref="ClientId"/>: the code arrives in the URL fragment, the page shows it, and the host reads it back through <see cref="IAuthInteraction.GetBrowserAuthCodeAsync"/>.</summary>
    /// <remarks>Override this only when you control the Azure application behind <see cref="ClientId"/> and have registered the replacement. A loopback URI switches the flow to a local listener so the user pastes nothing; see <see cref="BrowserRedirectStrategy"/> for which loopback URIs can actually match a registration.</remarks>
    public Uri BrowserRedirectUri { get; init; } = DefaultBrowserRedirectUri;

    /// <summary>The HTTP handler seam. Defaults to a factory that creates a fresh <see cref="SocketsHttpHandler"/>.</summary>
    public IHttpMessageHandlerFactory HttpHandlerFactory { get; init; } = DefaultHttpMessageHandlerFactory.Instance;

    /// <summary>The loopback receiver seam for the browser flow. Defaults to <see cref="HttpListenerLoopbackReceiver"/>, a one-shot loopback HTTP responder. Tests substitute a fake.</summary>
    public ILoopbackCodeReceiverFactory LoopbackReceiverFactory { get; init; } = HttpListenerLoopbackReceiverFactory.Instance;

    /// <summary>The token/certificate cache. Defaults to an in-memory store.</summary>
    public ITokenStore TokenStore { get; init; } = new InMemoryTokenStore();

    /// <summary>The clock. Defaults to <see cref="TimeProvider.System"/>. Tests inject a fake.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>The diagnostics logger. Defaults to a no-op logger. Tokens are never logged.</summary>
    public ILogger Logger { get; init; } = NullLogger.Instance;
}
