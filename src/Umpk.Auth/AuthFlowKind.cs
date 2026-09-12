namespace Umpk.Auth;

/// <summary>Selects the concrete login flow <see cref="MinecraftAuthFlow"/> runs. The enum is deliberately extensible: <c>Msal</c> and <c>Sisu</c> are reserved for later additions (prismarine-auth precedent) and are not yet implemented.</summary>
public enum AuthFlowKind
{
    /// <summary>Microsoft OAuth 2.0 device-code flow (default). Host renders the code and URL.</summary>
    MicrosoftDeviceCode,

    /// <summary>Microsoft OAuth 2.0 browser auth-code flow through a loopback listener.</summary>
    MicrosoftBrowser,

    /// <summary>Third-party Yggdrasil (authlib-injector) username/password flow.</summary>
    Yggdrasil,

    /// <summary>Offline mode: deterministic UUID, no network calls.</summary>
    Offline,
}
