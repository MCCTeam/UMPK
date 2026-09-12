---
title: "Umpk.Realms"
description: "The Realms HTTP API client: list your worlds, join one, check compatibility and accept the terms."
sidebar:
  order: 14
---

`Umpk.Realms` talks to Mojang's Realms service over HTTP. It lists the worlds an account can see, asks one of them for a server address, reports whether the client version you claim is still accepted, and records agreement to the terms of service.

That is the whole scope. It resolves an address; it does not connect. Once you have a `RealmServerAddress` you hand it to [Umpk.Client](umpk-client.md) like any other server.

## Its place in the stack

`Umpk.Realms` depends on [Umpk.Auth](umpk-auth.md) and nothing else directly, which pulls in [Umpk.Core](umpk-core.md) and [Umpk.Protocol.Java](umpk-protocol-java.md) transitively. It reuses `Umpk.Auth`'s `IHttpMessageHandlerFactory` seam so tests can script the HTTP layer.

Nothing depends on `Umpk.Realms`, and the `Umpk` meta package does not reference it. Add the package explicitly if you want Realms.

## Main entry points

`IRealmsClient` is the interface and `RealmsClient` the implementation, which is `IDisposable`. Four calls: `ListWorldsAsync`, `JoinWorldAsync(worldId)`, `CheckClientCompatibleAsync` and `AgreeToTermsAsync`. `ResolveWorldAsync(worldSelector, ct)`, an extension method, composes the common case on top: list, match the selector with `RealmWorld.Match`, join the match. Want more control (show the list before picking, for instance)? Call `ListWorldsAsync` and `RealmWorld.Match` yourself; `ResolveWorldAsync` exists for the case where you already know which world you want.

`RealmsClientOptions` configures it. `Credential` is required. `BaseUri` defaults to `https://pc.realms.minecraft.net`, `HttpHandlerFactory` to the shared default, and `Logger` to a no-op.

`RealmsSessionCredential.FromSession(session, clientVersion)` builds the auth material from a completed `JavaSession` and the Minecraft version string you want to report. No extra auth call happens; the session's own access token and profile are reused. Realms is Microsoft-only: a session whose `Kind` is not `AuthKind.Microsoft` (offline or Yggdrasil) makes `FromSession` throw `RealmsException` with `RealmsErrorKind.RequiresMicrosoftAccount` rather than build a credential Realms would just reject. A null session is still an `ArgumentNullException`; only the account-kind check is a classified failure.

`RealmWorld` describes a world: `Id`, `Name`, `Motd`, `Owner`, `OwnerUuid`, `State`, `WorldType`, `Expired`, `ExpiredTrial`, `DaysLeft`, `MaxPlayers`, `ActiveSlot` and `Member`. `RealmState` is `Open`, `Closed`, `Uninitialized` or `Unknown`. `RealmWorld.Match(worlds, selector)` resolves a selector against a list: it tries `selector` as an id first (over the whole list, before any name is checked), then falls back to a case-insensitive name match. A selector only counts as an id when it parses as bare digits with no sign or surrounding whitespace, so `"5678"` matches a world whose `Id` is 5678 even if another world in the same list happens to be named `"5678"`.

`RealmServerAddress` is the join result: `Host`, `Port` (defaulting to `RealmServerAddress.DefaultPort`, 25565), and an optional `ResourcePackUrl` and `ResourcePackHash`. `RealmServerAddress.Parse` reads the `host:port` form the service returns.

`RealmsCompatibility` is `Compatible`, `Outdated`, `Other` or `Unknown`. Failures raise `RealmsException`, or `RealmsServiceException` with the operation name, HTTP status, and a classified `RealmsErrorKind`: `Unauthorized`, `TermsNotAgreed`, `ClientOutdated`, `WorldLocked`, `WorldOutOfDate`, `WorldNotFound`, `ServiceBusy`, `InvalidResponse`, `RequiresMicrosoftAccount`, or the catch-all `ServiceError`. `RealmsServiceException.ErrorCode` and `.Reason` carry the Realms body's own `errorCode`/`reason` fields when the body had them, for the cases `RealmsErrorKind` does not name (a download/upload limit or an invalid name/description, for instance, still surface through `ErrorCode`).

## Example

```csharp
using Umpk;
using Umpk.Auth;
using Umpk.Realms;

var credential = RealmsSessionCredential.FromSession(session, clientVersion: "1.21.11");
using var realms = new RealmsClient(new RealmsClientOptions { Credential = credential });

if (await realms.CheckClientCompatibleAsync(ct) == RealmsCompatibility.Outdated)
{
    Console.Error.WriteLine("Realms rejects this client version. Report a newer one.");
    return 1;
}

IReadOnlyList<RealmWorld> worlds = await realms.ListWorldsAsync(ct);
RealmWorld? open = worlds.FirstOrDefault(w => w.State == RealmState.Open && !w.Expired);
if (open is null)
{
    Console.Error.WriteLine("No open, unexpired world on this account.");
    return 1;
}

RealmServerAddress address = await realms.JoinWorldAsync(open.Id, ct);
var endpoint = new ServerEndpoint(address.Host, (ushort)address.Port);

// From here it is an ordinary session. See the Umpk.Client page.
await client.ConnectAsync(endpoint, ct);
```

## Things that catch people out

`ClientVersion` is not decorative. Realms checks the version string in the session cookie and refuses outdated clients outright, which surfaces as `RealmsCompatibility.Outdated` or as a service exception on a later call. Report the version you are going to connect with. Call `CheckClientCompatibleAsync` before any other Realms call.

`AgreeToTermsAsync` is a real side effect on the account. It records that the account holder accepted Mojang's Realms terms. Do not call it automatically on the user's behalf. When a call fails because the terms are not accepted, report the failure to the account holder. Let the account holder decide.

`RealmServerAddress.Port` is an `int`, while `ServerEndpoint.Port` is a `ushort`. Cast the port value when you build the endpoint. This is an awkward seam and it is easy to miss, because the compiler error lands somewhere else.

A world in `RealmState.Closed` or `Uninitialized` will not give you a usable address. Filter on `State == RealmState.Open` and on `Expired` before joining.

`JoinWorldAsync` can return an address for a world the service is still starting. Realms boots a stopped world on demand, so the first connection attempt to a freshly joined world can fail even though the address is correct. Retry the connection rather than the join. A join or list call made while the world is still booting is also where you are most likely to see `RealmsErrorKind.ServiceBusy` (HTTP 429, 503, or the vanilla-specific 277): treat it as the same "not ready yet" signal and retry rather than surfacing it as a hard failure.

`ResourcePackUrl` and `ResourcePackHash` come back on the join response, not from a resource pack packet. UMPK never downloads a pack. The fields are there so that you can download it yourself.

The Realms world id is a `long`, not the world's name or its index in the list. Pass `RealmWorld.Id`.
