---
title: Authentication
description: What Umpk.Auth supports Microsoft device code and browser flows, Yggdrasil, the token store, session resume, and offline identity.
sidebar:
  order: 1
---

`Umpk.Auth` gets you a `JavaSession`. It does not connect to anything. Handing that session to a client is a separate, manual step, and the seam between the two is narrower than you might expect: `Umpk.Client` does not reference `Umpk.Auth` at all.

Three samples cover the three ways in, each runnable on its own: `samples/AuthOffline` needs no network, `samples/AuthOnline` runs the Microsoft device code flow, and `samples/AuthThirdParty` runs the Yggdrasil flow against a third party auth server. `samples/ConnectedBot` then shows the online flow wired into a client that joins and chats.

## The shape of it

One class runs every flow.

```csharp
public sealed class MinecraftAuthFlow : IDisposable
{
    public MinecraftAuthFlow(MinecraftAuthOptions options);

    public Task<JavaSession> LoginAsync(
        IAuthInteraction interaction, CancellationToken ct, string? loginHint = null);

    public Task<JavaSession?> TryResumeAsync(string loginHint, CancellationToken ct);
    public Task<PlayerCertificates> GetCertificatesAsync(JavaSession session, CancellationToken ct);
    public Task InvalidateAsync(string loginHint, CancellationToken ct);
}
```

Note the parameter order on `LoginAsync`. The cancellation token sits in the middle so the optional `loginHint` can trail it, which means you always pass the token explicitly.

`MinecraftAuthOptions` picks which flow runs, through `FlowKind`:

| `AuthFlowKind` | What it does |
| --- | --- |
| `MicrosoftDeviceCode` | Default. Shows a code, polls for approval. |
| `MicrosoftBrowser` | Opens a sign-in URL, collects the authorization code. |
| `Yggdrasil` | Username and password against a third-party auth server. |
| `Offline` | No network. Derives the profile from `OfflineUsername`. |

Every other option has a default: `ClientId` is the bundled public application id, `TokenStore` is a fresh `InMemoryTokenStore`, `TimeProvider` is `TimeProvider.System`, `Logger` is a no-op, `HttpHandlerFactory` is `DefaultHttpMessageHandlerFactory.Instance`, and `BrowserRedirectUri` is `MinecraftAuthOptions.DefaultBrowserRedirectUri`. All properties are init-only.

The result is a record:

```csharp
public sealed record JavaSession(
    GameProfile Profile, string AccessToken, DateTimeOffset ExpiresAt,
    string? RefreshToken, AuthKind Kind)
{
    public bool IsExpired(DateTimeOffset now);
}
```

`JavaSession.ToString()` prints `<redacted>` rather than the token. So do `YggdrasilCredentials`, `ProfileCredentials` and `PlayerCertificates`. That is deliberate and pinned by a test, so do not "improve" it.

## You supply the interaction

Every flow that talks to a human goes through one interface, which you implement:

```csharp
public interface IAuthInteraction
{
    Task ShowDeviceCodeAsync(DeviceCodePrompt prompt, CancellationToken ct);
    Task<string> GetBrowserAuthCodeAsync(Uri signInUrl, CancellationToken ct);
    Task<YggdrasilCredentials> GetYggdrasilCredentialsAsync(CancellationToken ct);
}
```

There is no console implementation in the box. A device-code host implements the first method and can throw from the other two.

## Microsoft device code

This is the default flow and the one to reach for in a console application. You show a short code, the user types it into a browser somewhere, and the library polls until Microsoft says yes.

```csharp
using var flow = new MinecraftAuthFlow(new MinecraftAuthOptions
{
    FlowKind = AuthFlowKind.MicrosoftDeviceCode,
});

JavaSession session = await flow.LoginAsync(interaction, CancellationToken.None, "player@example.com");
```

`ShowDeviceCodeAsync` receives a `DeviceCodePrompt` with four fields: `UserCode`, `VerificationUri`, `Message` and `ExpiresAt`. `Message` is Microsoft's own ready-made instruction, so printing it is usually enough.

The poll loop delays first and asks second, at the interval Microsoft returned. On `slow_down` it adds five seconds to that interval and keeps going. Three outcomes end it, all as `DeviceCodeAuthorizationException` carrying a `DeviceCodeFailure`: `Declined` when the user refuses, `Expired` when Microsoft says the code is dead, `TimedOut` when the deadline passes. There is no separate timeout setting; the deadline is Microsoft's `expires_in` and nothing else.

Cancelling the token aborts the poll.

`DeviceCodePrompt` is the one auth type that does not redact itself, because the whole point is to show the code. Keep device-code prompts out of shared logs.

After the token arrives, the flow runs the rest of the chain (Xbox Live, XSTS, `login_with_xbox`, the entitlement check, the profile fetch). Failures there are typed: `XstsAuthorizationException` carries an `XErr` and an `XstsErrorReason` (`NoXboxAccount`, `RegionUnavailable`, `AdultVerificationRequired`, `ChildAccount`, `AccountBanned`, `Unknown`), and `NoMinecraftEntitlementException` means the account does not own the game. Anything else is an `AuthServiceException` with a `Stage` string and a `StatusCode`.

## Microsoft browser

The browser flow has two modes, and which one you get is decided by the redirect URI before any browser opens:

```csharp
public static BrowserRedirectMode Resolve(Uri redirectUri);
```

`BrowserRedirectMode.HostedPastePage` is the default. The authorization code comes back in the URL fragment, so it never reaches the hosting server, the page displays it, and your `GetBrowserAuthCodeAsync` returns whatever the user pasted. The `state` parameter is not checked on this path, because the user is the transport.

`BrowserRedirectMode.LoopbackListener` happens when the redirect URI is a loopback address. The library starts a one-shot local listener, hands you the URI it actually bound (port 0 becomes an OS-assigned port), and races your `GetBrowserAuthCodeAsync` against the listener. Here the `state` is checked at the door: a request with the wrong state gets a 400 and does not consume the slot.

`BrowserRedirectStrategy.Resolve` refuses a redirect URI Microsoft could never match, and it does so before opening a browser so the user sees a real reason rather than a Microsoft error page. It rejects relative URIs, schemes other than http and https, and loopback hosts other than `localhost` that use port 0 or no port at all. `BrowserRedirectStrategy.PortAgnosticLoopbackHost` is the string `"localhost"`.

Two practical notes. `ClientId` is required for both Microsoft flows and defaults to the bundled public application id; there is no client secret anywhere, since these are public-client flows. Overriding `BrowserRedirectUri` only works if you own the Azure application behind `ClientId` and registered the replacement.

One naming trap: the default receiver type is called `HttpListenerLoopbackReceiver` and its own doc comment says it does not use `HttpListener`. It is a socket-level responder that binds `127.0.0.1` and `[::1]`, and forces anything non-loopback down to `127.0.0.1`.

## Yggdrasil

For third-party auth servers (authlib-injector and the like). Set the base URL, and the flow asks your interaction for a username and password.

```csharp
using var flow = new MinecraftAuthFlow(new MinecraftAuthOptions
{
    FlowKind = AuthFlowKind.Yggdrasil,
    YggdrasilBaseUrl = new Uri("https://example.com/api/yggdrasil/"),
});

JavaSession session = await flow.LoginAsync(interaction, CancellationToken.None);
```

The trailing slash matters. The request goes to `authserver/authenticate` resolved against that base, so `https://example.com/api/yggdrasil/` gives you `https://example.com/api/yggdrasil/authserver/authenticate`. Leaving `YggdrasilBaseUrl` null throws an `AuthException` naming the missing option.

Now the honest part. Only `authserver/authenticate` is implemented. There is no refresh, no validate, no invalidate and no signout. The expiry on a Yggdrasil session is not real either: the flow stamps `now + 24 hours` and sets `RefreshToken` to null, because the endpoint does not tell it anything better. So a Yggdrasil user re-enters the password every 24 hours of wall-clock time whether or not the actual token is still good, and `TryResumeAsync` returns null for an expired one.

`Umpk.Auth.Session.YggdrasilSessionService` is a separate thing from the login flow. It implements the join and hasJoined calls against a session server, defaults to `https://sessionserver.mojang.com/`, and takes an override through `SessionServiceOptions.BaseUrl` for an authlib-injector provider.

## The token store

`ITokenStore` has three members and no way to enumerate what is in it:

```csharp
public interface ITokenStore
{
    ValueTask<T?> GetAsync<T>(string key, CancellationToken ct);
    ValueTask SetAsync<T>(string key, T value, CancellationToken ct);
    ValueTask RemoveAsync(string key, CancellationToken ct);
}
```

Three types can be stored, and only three: `JavaSession`, `PlayerCertificates` and `PersistedRefreshToken`. Anything else throws `NotSupportedException`. Serialization runs through a source-generated JSON context, which is what keeps the whole package AOT clean.

Two implementations ship. `InMemoryTokenStore` is the default, so out of the box nothing survives a process restart. `FileTokenStore` persists.

A file token store holds access tokens, refresh tokens and profile private keys. Choose a directory outside every repository working tree before you construct one.

```csharp
public FileTokenStore(string directory, ITokenProtector protector, ILogger? logger = null);
```

The directory is a required argument. There is no default path and no helper that computes one, so pick a location yourself.

Files are named `sha256hex(key).tok`, which sanitizes the key, prevents path escape, and keeps account names out of a directory listing. Writes go to a temporary file, get their permissions restricted, and are then moved into place. On Unix the store chmods the directory to 0700 and each file to 0600. An unreadable or corrupt entry is logged, deleted and treated as a cache miss rather than thrown.

### Encryption is Windows only

```csharp
public static ITokenProtector CreateDefault(ILogger? logger = null);
```

`TokenProtectors.CreateDefault` returns `DpapiTokenProtector` on Windows and `NoOpTokenProtector` everywhere else. On macOS and Linux the tokens are plaintext JSON on disk, protected by file permissions and nothing more. `NoOpTokenProtector` logs one warning saying exactly that. There is no Keychain or libsecret backend. If you need one, implement `ITokenProtector` yourself; it is two methods.

### Security

The repository's `.gitignore` has no pattern for `*.tok`. A cache directory placed inside a working tree is committable. Follow these rules:

1. Put the token store directory outside any repository working tree.
2. Do not commit the token store directory, any `.tok` file, or any file that holds a refresh token or a profile private key.
3. Do not paste an access token, a refresh token, or a profile private key into logs, issues or commit messages.
4. Treat the whole cache directory as credential material. A stored session holds the access token and the refresh token; stored certificates hold the profile private key.

Exception messages from this package carry stage names, HTTP status codes and OAuth error codes. They carry no tokens. Keep it that way when you add logging of your own.

## Session resume

```csharp
JavaSession? resumed = await flow.TryResumeAsync("player@example.com", CancellationToken.None);
JavaSession session = resumed ?? await flow.LoginAsync(interaction, CancellationToken.None, "player@example.com");
```

`TryResumeAsync` reads the store under `"session:" + loginHint.ToUpperInvariant()` and then decides:

- Nothing cached, return null.
- Cached and not expired, return it with no network call at all.
- Expired, Microsoft, and a refresh token present: refresh, re-cache, return the new session.
- Anything else (an expired Yggdrasil session, an expired Microsoft session with no refresh token), return null and let the caller log in again.

`LoginAsync` caches under the profile name and, when the login hint differs, under the hint too. That double keying is why you can resume by either. It also means `InvalidateAsync` is sharper than it looks: it removes only the key you name, so invalidating by email leaves the gamertag-keyed copy of the same access token on disk. Call it for both if you mean to wipe an account.

## Offline identity

```csharp
public static Guid ComputeUuid(string username);
public static GameProfile ComputeProfile(string username);
```

`OfflineIdentity.ComputeProfile` derives the UUID a vanilla offline server assigns: MD5 of `"OfflinePlayer:<name>"` in UTF-8, with the version nibble forced to 3 and the RFC 4122 variant bits set, assembled big-endian to match Java's `UUID.nameUUIDFromBytes`. Same name, same UUID, every time, which is what lets a bot keep its identity across reconnects.

You do not need `MinecraftAuthFlow` for this. The [minimal bot](/getting-started/minimal-bot) calls `OfflineIdentity.ComputeProfile(username)` directly, and that is the normal way to use it. Going through the flow with `FlowKind = AuthFlowKind.Offline` gives you a `JavaSession` whose `AccessToken` is the empty string and whose `ExpiresAt` is `DateTimeOffset.MaxValue`. Offline sessions are never cached. Never pair one with `UseAuthenticator`.

## Handing the session to a client

This is the step nothing in the repository does for you, so here it is in full. The client takes a `GameProfile` and, for online mode, an `ISessionAuthenticator` plus a `ProfileCredentials`:

```csharp
using Umpk;
using Umpk.Auth;
using Umpk.Auth.Session;
using Umpk.Client;
using Umpk.Data.Java;
using Umpk.Protocol.Java;

static async Task<UmpkClient> BuildOnlineClientAsync(
    MinecraftAuthFlow flow, IAuthInteraction interaction, string loginHint, JavaVersion version)
{
    JavaSession session =
        await flow.TryResumeAsync(loginHint, CancellationToken.None)
        ?? await flow.LoginAsync(interaction, CancellationToken.None, loginHint);

    var sessionService = new YggdrasilSessionService(new SessionServiceOptions());

    return new UmpkClientBuilder()
        .UseVersion(version)
        .UseProfile(session.Profile)
        .UseAuthenticator(sessionService, new ProfileCredentials(session.Profile, session.AccessToken))
        .UseStaticRegistries(JavaGameData.Registries(version.Version.Protocol))
        .Build();
}
```

`YggdrasilSessionService` implements `ISessionAuthenticator`. With a default `SessionServiceOptions` it talks to Mojang; give it a `BaseUrl` for an authlib-injector provider. It is `IDisposable`, and the client does not take ownership, so keep it alive for the life of the client and dispose it yourself.

Offline mode is the absence of `UseAuthenticator`. Nothing else distinguishes the two.

## Chat signing certificates

`GetCertificatesAsync` fetches the profile key pair a 1.19+ server needs to verify your messages, and caches it under `"certificates:" + name`. A cached copy is reused until `PlayerCertificates.IsExpired` says otherwise.

The client does not call it for you. You wire it up by implementing `IChatSigningProvider` and passing it to `UmpkClientBuilder.UseChatSigning`. The contract there has real constraints on caching and re-entrancy, which [chat and signing](/guides/chat-and-signing) goes through.

## What is not here

- No Yggdrasil refresh, validate or invalidate. Login is the whole surface.
- No default token store path, and no helper to compute one.
- No at-rest encryption outside Windows.
- No way to list or clear the token store through the public API. Keys are hashed into filenames, so you cannot reverse them either.
- No HTTP timeout, retry or poll-interval settings. To change any of that you replace the whole `IHttpMessageHandlerFactory`.
- `PersistedRefreshToken` is public and serializable, but nothing in the flow ever writes one.

More detail on the package surface in [Umpk.Auth](/packages/umpk-auth).
