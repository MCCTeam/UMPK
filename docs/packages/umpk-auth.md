---
title: Umpk.Auth
description: Microsoft device code and browser login, Yggdrasil, offline identity, session caching and the session-join seam.
sidebar:
  order: 12
---

`Umpk.Auth` turns a person into a `JavaSession`: a game profile, an access token, an expiry and a refresh token. It runs the Microsoft device code flow, the Microsoft browser flow, or Yggdrasil for third-party auth servers, caches the result so the next start does not prompt again, and provides the session-join call an online-mode login needs.

The package performs no console output and asks for nothing directly. Every prompt goes through an `IAuthInteraction` you implement, which is what keeps it usable from a GUI, a TUI or a headless service.

## Its place in the stack

`Umpk.Auth` depends on [Umpk.Core](/packages/umpk-core) and [Umpk.Protocol.Java](/packages/umpk-protocol-java). It implements `ISessionAuthenticator`, the client-role session-join seam that `Umpk.Protocol.Java` declares, and it produces the `PlayerCertificates` that 1.19+ chat signing needs.

[Umpk.Client](/packages/umpk-client) does not reference `Umpk.Auth`. The dependency runs the other way: you pass an authenticator into `UmpkClientBuilder.UseAuthenticator`. An offline bot never pulls in the auth stack at all.

[Umpk.Realms](/packages/umpk-realms) is the one package that depends on `Umpk.Auth` directly.

## Main entry points

`MinecraftAuthFlow` is the orchestrator, and it is `IDisposable`. `LoginAsync(interaction, ct, loginHint)` runs the configured flow and caches the session. `TryResumeAsync(loginHint, ct)` returns a cached session, refreshing an expired Microsoft token transparently, or null. `InvalidateAsync` drops a cached session. `GetCertificatesAsync(session, ct)` fetches the player key pair for chat signing.

`MinecraftAuthOptions` configures it. `FlowKind` picks the flow, `ClientId` is the Azure application id (it defaults to a registered public client), `TokenStore` is the cache, `YggdrasilBaseUrl` points at an authlib-injector server, `OfflineUsername` is required for the offline flow, and `HttpHandlerFactory`, `Logger`, `TimeProvider`, `BrowserRedirectUri` and `LoopbackReceiverFactory` are the seams.

`AuthFlowKind` has `MicrosoftDeviceCode` (the default), `MicrosoftBrowser`, `Yggdrasil` and `Offline`.

`IAuthInteraction` is the three prompts you must implement: `ShowDeviceCodeAsync(DeviceCodePrompt)`, `GetBrowserAuthCodeAsync(signInUrl)` and `GetYggdrasilCredentialsAsync()`.

`JavaSession` is the result: `Profile`, `AccessToken`, `ExpiresAt`, `RefreshToken`, `Kind` and an `IsExpired(now)` test.

`OfflineIdentity.ComputeProfile(username)` and `ComputeUuid(username)` produce the vanilla-deterministic offline identity, matching Java's `UUID.nameUUIDFromBytes("OfflinePlayer:<name>")`. No network call, no flow.

`ITokenStore` has two implementations: `InMemoryTokenStore` (the default) and `FileTokenStore`, which takes a directory and an `ITokenProtector`. `TokenProtectors.CreateDefault()` returns `DpapiTokenProtector` on Windows and `NoOpTokenProtector` everywhere else.

`Umpk.Auth.Session.YggdrasilSessionService` implements `ISessionAuthenticator`. This is what you hand to `UmpkClientBuilder.UseAuthenticator` for online mode.

Failures are typed: `AuthException` at the top, then `AuthServiceException` (an HTTP stage and status), `XstsAuthorizationException` (with an `XstsErrorReason` such as `NoXboxAccount`, `ChildAccount` or `AccountBanned`), `DeviceCodeAuthorizationException` (with `Declined`, `Expired` or `TimedOut`) and `NoMinecraftEntitlementException`.

## Example

```csharp
using Umpk;
using Umpk.Auth;
using Umpk.Auth.Session;
using Umpk.Client;

var options = new MinecraftAuthOptions
{
    FlowKind = AuthFlowKind.MicrosoftDeviceCode,
    TokenStore = new FileTokenStore(cacheDirectory, TokenProtectors.CreateDefault()),
};

using var flow = new MinecraftAuthFlow(options);

// The hint is what YOU will pass to TryResumeAsync later, typically the account email.
const string loginHint = "player@example.com";

JavaSession session = await flow.TryResumeAsync(loginHint, ct)
    ?? await flow.LoginAsync(new ConsoleInteraction(), ct, loginHint);

using var sessionService = new YggdrasilSessionService(new SessionServiceOptions());

await using UmpkClient client = new UmpkClientBuilder()
    .UseVersion(version)
    .UseProfile(session.Profile)
    .UseAuthenticator(sessionService, new ProfileCredentials(session.Profile, session.AccessToken))
    .Build();

sealed class ConsoleInteraction : IAuthInteraction
{
    public Task ShowDeviceCodeAsync(DeviceCodePrompt prompt, CancellationToken ct)
    {
        Console.WriteLine($"Open {prompt.VerificationUri} and enter {prompt.UserCode}.");
        return Task.CompletedTask;
    }

    public Task<string> GetBrowserAuthCodeAsync(Uri signInUrl, CancellationToken ct) =>
        throw new NotSupportedException("This host only runs the device code flow.");

    public Task<YggdrasilCredentials> GetYggdrasilCredentialsAsync(CancellationToken ct) =>
        throw new NotSupportedException("This host only runs the device code flow.");
}
```

## Things that catch people out

The `loginHint` argument is the whole reason a resume works. A session is always cached under the resolved Minecraft profile name, but for a Microsoft account that name is the gamertag, which is not what your configuration file calls the account. Without a hint, `TryResumeAsync("player@example.com")` misses and you get a fresh interactive sign-in on every start. Pass the same hint to `LoginAsync`. Pass that same hint again to `TryResumeAsync`. The flow then caches the session under both keys. This was found in live testing, not in theory.

`AuthKind` and `AuthFlowKind` are two different enums that look alike. `AuthFlowKind` selects the flow to run (`MicrosoftDeviceCode`, `MicrosoftBrowser`, `Yggdrasil`, `Offline`). `AuthKind` records what a completed `JavaSession` came from (`Microsoft`, `Yggdrasil`, `Offline`). They do not have the same members and they are not interchangeable.

The default `TokenStore` is `InMemoryTokenStore`, so out of the box nothing survives a restart and `TryResumeAsync` always misses. Use a `FileTokenStore` if you want resume to work.

`TokenProtectors.CreateDefault()` gives real at-rest encryption only on Windows. On Linux and macOS it returns `NoOpTokenProtector`, which logs a warning and stores plaintext; the file store restricts permissions to the owner where the OS supports it, and that is the whole protection. Supply your own `ITokenProtector` if that is not good enough for your threat model.

Online mode needs both halves. `UseProfile` alone gets you an offline identity even with a real session in hand. You must also call `UseAuthenticator` with a session service and the profile credentials, or the server will reject the encryption handshake.

`BrowserRedirectUri` defaults to a hosted paste page registered against the default `ClientId`: the code arrives in the URL fragment, the page shows it, and your `IAuthInteraction` reads it back. Override it only when you control the Azure application behind your own `ClientId`. `BrowserRedirectStrategy.Resolve` tells you which mode a given redirect URI implies, `HostedPastePage` or `LoopbackListener`.

Never log a `JavaSession`, an access token or a refresh token. The package itself logs neither. See [the authentication guide](/guides/authentication).
