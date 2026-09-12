---
title: Umpk.Client
description: The session runtime: connect and log in, apply packets into state, raise typed events, and send actions.
sidebar:
  order: 8
---

`Umpk.Client` is the part you actually hold. It connects, logs in, runs the configuration phase, enters play, and from then on applies every decoded packet into a state model, raises a typed event, and lets you send actions back. It is the only package here with a session loop and a lifecycle.

If you are writing a bot, this is your API. Almost everything else in the repository exists to make this package correct across 49 protocols.

## Its place in the stack

`Umpk.Client` references more than anything else: [Umpk.Core](/packages/umpk-core), [Umpk.Game](/packages/umpk-game), [Umpk.Protocol.Java](/packages/umpk-protocol-java), [Umpk.Commands](/packages/umpk-commands), [Umpk.Text](/packages/umpk-text), [Umpk.Physics](/packages/umpk-physics), [Umpk.Pathfinding](/packages/umpk-pathfinding) and [Umpk.Data.Java](/packages/umpk-data-java). It does not reference [Umpk.Auth](/packages/umpk-auth); online-mode login goes through the `ISessionAuthenticator` seam that `Umpk.Auth` implements, so an offline bot never pulls in the auth stack.

Nothing depends on `Umpk.Client` except the `Umpk` meta package.

## Main entry points

`UmpkClientBuilder` composes a session. `UseVersion` and `UseProfile` are the only two that are really required; `Build` throws if either is missing. The rest are seams with defaults: `UseStaticRegistries`, `UseAuthenticator`, `UseChatSigning`, `UseConnectionFactory`, `UseAddressResolver`, `UseTickSource`, `UseScheduler`, `UseLoggerFactory`, `UseBlockShapes`, `AddPlugin`, and the three configure hooks `ConfigureFeatures`, `ConfigureOptions` and `ConfigurePolicies`.

`UmpkClient` is the session. `ConnectAsync` runs the whole handshake and completes when play begins. `DisconnectAsync` closes cleanly, and the client is `IAsyncDisposable`; final disposal closes an owned socket transport rather than waiting for peer timeout or garbage collection. `Status` reports `Created`, `Connecting`, `Configuring`, `Playing` or `Disconnected` (never `Authenticating` or `Reconnecting`, which are supervisor-only states). `StatusChanged` raises every transition synchronously, and `LastDisconnect` holds the reason the most recent session ended, populated even when a kick during login or configuration throws out of `ConnectAsync` before a session ever reaches play. `Session` gives you the endpoint, profile, version and phase once connected.

`Umpk.Client.Supervision.UmpkClientSupervisor` owns a session across many connections instead of one. `StartAsync` runs the first attempt inline and arms a background reconnect watch; `ReconnectAsync` tears the current session down and dials a fresh one, optionally on a new endpoint; `StopAsync` ends it for good. It takes an `IClientSessionFactory` rather than a client, because a `UmpkClient` resolves its version, wire index and identity at construction, so a reconnect that switches account or lands on a server with a different version has to build a new one. `IReconnectPolicyProvider` is consulted after every unexpected disconnect (and again after every failed retry), and `ConnectFailedException` / `LoginRejectedException` (both a `SessionStartException`, alongside `VersionResolutionException`) are what a caller catches instead of pattern-matching `ConnectionClosedException.Reason` by hand. See [a minimal bot](/getting-started/minimal-bot) for why the sample there does not use it.

Server-directed transfers are surfaced by every client but are followed only when `ClientSupervisorOptions.FollowServerTransfers` is enabled. A followed hop creates a fresh client with the same profile, copies owned cookie bytes into the destination session, uses the transfer handshake intent, and is bounded by `MaxTransferHops`. This is independent of ordinary reconnect policy.

`UmpkClient.State` is the read model. `Self` (position, health, food, game mode, abilities, effects), `World`, `Entities`, `Inventory`, `Server` (brand, latency, keep-alive timing, ticks per second), `Chat`, `Dialogs`, `TabList`, `Scoreboard`, `BossBars`, `Maps`, `Advancements`, `Recipes`, `ServerCommands` and `Registries`.

`UmpkClient.Events` is the typed bus. `Subscribe<TEvent>` takes a synchronous or a `ValueTask` handler and returns an `IDisposable`. `Stream<TEvent>` gives you an `IAsyncEnumerable`. There are over seventy event types in `Umpk.Client.Events`, from `JoinedGame` and `ChatMessageReceived` down to `ParticleSpawned` and `LevelEventOccurred`, all of them implementing `IClientEvent`.

`UmpkClient.Actions` is the write side, grouped: `Chat` (send chat, send a command, complete), `Movement` (move, look, sneak, sprint, navigate), `Interaction` (dig, place, use, attack, sign and command block edits), `Inventory` (click, quick move, close, creative set slot, trades), `Session` (respawn, client settings, flying) and `Dialog`.

`UmpkClient.Navigation` is a `Navigator` with `MoveToAsync` and `NavigateAsync(IGoal)`. `UmpkClient.Commands` is a `CommandService<ClientCommandSource>` for host-side commands. `UmpkClient.Cookies` is the 1.20.5+ cookie store.

`Umpk.Client.Plugins.IClientPlugin` is the extension point. A plugin gets a `ClientPluginContext` with the client, events, actions, a command registration scope, a tick scheduler, plugin channel registration, and `TryAcquireMovement` for taking a movement lease so two plugins do not fight over the player.

## Example

The core of `samples/MinimalBot/Program.cs`: connect offline, print chat, say one thing, leave on Ctrl+C.

```csharp
using Umpk;
using Umpk.Auth;
using Umpk.Client;
using Umpk.Client.Events;
using Umpk.Data.Java;
using Umpk.Protocol.Java;

await using UmpkClient client = new UmpkClientBuilder()
    .UseVersion(version)
    // Offline mode is the absence of an authenticator. This UUID is the one a vanilla
    // offline server derives from the name, so the bot keeps its identity across reconnects.
    .UseProfile(OfflineIdentity.ComputeProfile(username))
    .UseStaticRegistries(JavaGameData.Registries(version.Version.Protocol))
    .Build();

// Handlers run on the session loop. Hand the news to the main flow and return.
var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

using IDisposable a = client.Events.Subscribe<JoinedGame>(e =>
{
    Console.WriteLine($"Joined as {e.Session.Profile.Name} on {e.Session.Version.Version.Name}.");
    joined.TrySetResult();
});

using IDisposable b = client.Events.Subscribe<ChatMessageReceived>(
    chat => Console.WriteLine(chat.Message.ToPlainText()));

using IDisposable c = client.Events.Subscribe<Disconnected>(gone =>
{
    Console.WriteLine($"Session ended: {gone.Info.Reason}");
    ended.TrySetResult();
});

await client.ConnectAsync(endpoint, ct);

// A whitelist or ban kick lands after login and before the join packet, so waiting on
// the join alone would wait forever for a session that is already over.
if (await Task.WhenAny(joined.Task, ended.Task).WaitAsync(ct) == ended.Task)
{
    return 1;
}

await client.Actions.Chat.SendChatAsync("Hello from UMPK.", ct);
await ended.Task.WaitAsync(ct);

// The logout gets an uncancelled token: on the Ctrl+C path ct is already cancelled
// and the disconnect packet still has to reach the server.
await client.DisconnectAsync(CancellationToken.None);
```

## Things that catch people out

`ConnectAsync` completes when the play phase starts, which is before the join packet has been applied. Chat cannot go out until it has. Do not send anything on the line after `ConnectAsync` returns. Wait for the `JoinedGame` event first.

And do not wait for `JoinedGame` alone. A whitelist or ban kick arrives after login and before the join packet, so a bare `await joined.Task` on a rejected session waits forever. Race the join against `Disconnected`, as the sample does.

Without `UseStaticRegistries` the session cannot decode a non-air item stack, and under the default strict decode policy the first item packet takes the whole session down. It is optional in the builder and effectively required in practice. Get the argument from `JavaGameData.Registries(version.Version.Protocol)`.

Event handlers run on the session loop. A handler that blocks stalls packet processing for the whole session. Do not do slow work inline in a handler. Set a `TaskCompletionSource`, post to your own queue, or call `PluginScheduler.RunOffLoop` instead.

Disabled features throw rather than returning empty. `ClientState.World` raises `FeatureDisabledException` when `ClientFeatures.Terrain` is off, and so do `Entities` and `Inventory` for their features. The implications resolve upward: pathfinding implies physics, physics implies terrain, and `ClientFeatures.Normalized()` is what applies that at build time. Turning off terrain and leaving pathfinding on does not disable terrain.

`ClientState.Registries` is nullable and is null before the configuration phase has delivered them. `ClientState.HasWorld` and `IsLocalChunkLoaded` are the guards for the world being usable at all.

Not every action exists on every protocol. Sending one that does not raises `ActionNotSupportedException`, which carries the action name, the packet identifier and the protocol number. Check `client.Capabilities` first: `CanPlaceBlock`, `CanUpdateSign`, `CanEditBook`, `CanRenameItem`, `CanSpectatorTeleport`, `CanSubmitDialog` and the rest are per-version booleans. See [era gating](/concepts/era-gating).

`ConnectAsync` makes no reconnect decision at all; that is `UmpkClientSupervisor`'s job. `ReconnectPolicy` has `MaxAttempts`, `InitialDelay`, `MaxDelay`, `BackoffFactor` and a `ShouldRetry` predicate over the `DisconnectInfo`; `ReconnectPolicy.IsRetryable` is the pure decision the supervisor applies, and it is public so a host can reuse it.

The default resource pack policy is `ResourcePackPolicy.Decline`: accepting network content is never implicit. A caller can still supply a synchronous decision callback, or opt in to bounded downloading and caching:

```csharp
.ConfigurePolicies(p => p.ResourcePack = ResourcePackPolicy.Configure(new()
{
    Accept = true,
    Download = true,
    Cache = true,
    CacheDirectory = "/var/cache/my-bot/resource-packs",
    MaxDownloadBytes = 256L * 1024 * 1024,
}))
```

The built-in downloader accepts only HTTP(S), reports `Accepted` before the fetch, enforces the body limit even when no content length is supplied, verifies a supplied SHA-1, and publishes a verified cache entry with an atomic rename. It reports `Downloaded`, never `SuccessfullyLoaded`, because this headless client does not apply visual assets. Set `Download = false` for acceptance without a fetch; set `Accept = false` to decline. `ResourcePackPolicy.Accept` remains as the older explicit shortcut that claims `SuccessfullyLoaded` without downloading, and should be used only when the host itself owns that claim.

`Stream<TEvent>` without an explicit capacity uses `ClientOptions.DefaultStreamCapacity` and `StreamFullPolicy.DropOldest`. A slow consumer silently loses the oldest events, and a `StreamLagged` event tells you it happened. Pass `StreamFullPolicy.Throw` if losing events is worse than failing.

Reads off the session loop are not strongly consistent. `client.State.Self.Position` read from your own thread may be mid-update. Use `InvokeAsync` to marshal the read onto the loop when the value has to be coherent with the rest of the state.

`DisconnectAsync` needs a token that is not already cancelled. On a Ctrl+C path the token you have is cancelled by definition, and passing it means the disconnect packet never reaches the server. Pass `CancellationToken.None`.
