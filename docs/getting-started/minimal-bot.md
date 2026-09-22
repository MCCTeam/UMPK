---
title: "A minimal bot"
description: "A walkthrough of the MinimalBot sample connect in offline mode, subscribe to events, send one message, and leave cleanly."
sidebar:
  order: 3
---

`samples/MinimalBot/Program.cs` is 140 lines and does four things: it joins a server without an account, prints the chat it hears, says one thing, and logs out on Ctrl+C. Two of its decisions look odd until you know what they are avoiding, and both are worth reading before you copy the pattern.

## Run the sample

Do not run the sample against a server you do not control. The sample sends a chat message on join.

1. Start a Minecraft server in offline mode.

2. Run the sample. Pass the address and the user name as two arguments.

   ```bash
   dotnet run --project samples/MinimalBot -- localhost Steve
   ```

3. To use a port other than 25565, put the port in the address argument.

   ```bash
   dotnet run --project samples/MinimalBot -- mc.example.com:25566 Steve
   ```

4. Press Ctrl+C to disconnect. The sample exits with code 0.

## Finding the protocol first

A client is built for one protocol version, so the sample has to know the version before it can build anything. It asks the server, with the same status ping from [your first status ping](status-ping.md):

```csharp
// SRV lookup only when the user typed no port, which is what vanilla does.
ServerEndpoint endpoint = requested.Port == ServerEndpoint.DefaultJavaPort
    ? await new DnsSrvResolver().ResolveAsync(requested, ct)
    : requested;

// A client is built for one protocol version, so ask the server which one it speaks. This is the
// same status ping StatusPing performs; only version.protocol is needed here.
ServerStatus status = await JavaStatus
    .QueryAsync(endpoint, TcpConnectionFactory.Shared, new JavaStatusOptions(), ct);
if (status.Protocol is not int protocol)
{
    Console.Error.WriteLine($"{endpoint} answered the ping without a version.protocol field.");
    return 1;
}

if (!JavaVersions.TryGetByProtocol(protocol, out JavaVersion version))
{
    Console.Error.WriteLine($"{endpoint} speaks protocol {protocol}, which UMPK has no data for.");
    return 1;
}
```

`JavaVersions` lives in `Umpk.Data.Java` and is the generated catalog of all 50 supported protocols. Besides `TryGetByProtocol` it offers `TryGetByName`, an `All` list, and one static property per version name, so `JavaVersions.V1_21_8` works when you already know what you are talking to.

`status.Protocol` is already the decoded `version.protocol` field, an `int?`. `ServerStatus.Parse` does the fiddly part for you: some proxies stringify the number, and vanilla itself would show no version at all for that shape, but real proxies emit one, so a stringified protocol is still accepted. A missing or genuinely malformed field leaves `Protocol` null, which is the cue this sample checks for. See [your first status ping](status-ping.md#what-serverstatus-carries) for everything else `ServerStatus` decodes.

## Building the client

```csharp
await using UmpkClient client = new UmpkClientBuilder()
    .UseVersion(version)
    // Offline mode is the absence of an authenticator. The UUID is the one a vanilla offline
    // server derives from the name, so the bot keeps its identity across reconnects.
    .UseProfile(OfflineIdentity.ComputeProfile(username))
    // Item stacks and entity types decode against the version's registries. Without them the first
    // packet carrying a non-air item fails to decode and takes the session down with it.
    .UseStaticRegistries(JavaGameData.Registries(version.Version.Protocol))
    .Build();
```

Three points here.

Offline mode is not a flag. It is the absence of `UseAuthenticator`. If you never call it, the login runs unauthenticated and the server had better be in offline mode too. See [authentication](../guides/authentication.md) for the other half.

`OfflineIdentity.ComputeProfile(username)` returns a `GameProfile` whose id is the UUID a vanilla offline server derives from the name. Using it means the bot keeps the same identity across reconnects, which matters for anything that persists per player.

`UseStaticRegistries` is not optional in practice on any version. Registries are how item stacks and entity types decode. Without them, the first packet carrying a non-air item fails to decode and takes the session with it. `JavaGameData.Registries(protocol)` is the generated table for that protocol.

`UmpkClientBuilder` has more: `UseConnectionFactory`, `UseAddressResolver`, `UseChatSigning`, `UseBlockShapes`, `UseLoggerFactory`, `UseScheduler`, `UseTickSource`, `AddPlugin`, and the three `Configure*` hooks for `ClientOptions`, `ClientFeatures` and `ClientPolicies`. The sample uses none of them, which is the point of it.

## Subscribing

```csharp
using IDisposable chatSubscription = client.Events.Subscribe<ChatMessageReceived>(
    chat => Console.WriteLine(chat.Message.ToPlainText()));

using IDisposable disconnectSubscription = client.Events.Subscribe<Disconnected>(gone =>
{
    Console.WriteLine($"Session ended: {gone.Info.Message?.ToPlainText() ?? gone.Info.Reason.ToString()}");
    ended.TrySetResult();
});
```

`Subscribe` returns an `IDisposable`. Disposing it unsubscribes. There is a second overload taking a `Func<TEvent, ValueTask>` for async handlers, and a `Stream<TEvent>` pair that hands you an `IAsyncEnumerable<TEvent>` instead, with a capacity and a `StreamFullPolicy` on the longer overload.

The handlers run on the session loop. That is why every one of them here does the smallest possible thing and returns: printing a line, or completing a `TaskCompletionSource` that the main flow is waiting on. Do real work in a handler and you stall packet processing for the whole session. The completion source is created with `TaskCreationOptions.RunContinuationsAsynchronously`, so the continuation does not run inline on the loop either.

Something over seventy event types live under `Umpk.Client.Events`, covering chat, world, entities, inventory, UI and connection lifecycle. [World and entities](../guides/world-and-entities.md) goes through the ones that matter for state.

## Two decisions that look wrong

### It waits on Disconnected instead of reconnecting

There is no reconnect loop on `UmpkClient` itself. Once it is in the world, the sample waits on a `TaskCompletionSource` that the `Disconnected` handler completes:

```csharp
try
{
    await ended.Task.WaitAsync(ct);
}
catch (OperationCanceledException) when (ct.IsCancellationRequested)
{
}
```

Neither `ConnectAsync` nor `ConnectAndWaitForSpawnAsync` makes any decision about what happens after the session ends. A sample with no reconnect policy configured has nothing to accidentally misconfigure: it connects once, and a kick reads as a kick rather than as the bot silently redialing a server that just threw it out.

Reach for `UmpkClientSupervisor` (`Umpk.Client.Supervision`) once you actually want automatic reconnect. It owns a session across many connections instead of one: `StartAsync` runs the first attempt inline and arms a background watch, an `IReconnectPolicyProvider` is consulted after every unexpected disconnect, and `ConnectFailedException` / `LoginRejectedException` replace the five-way exception ladder a bare `ConnectAsync` caller has to write by hand. See [Umpk.Client](../packages/umpk-client.md) for the rest of its surface.

### It waits for the spawn, not just the connect

This is the bug you will write if you call `ConnectAsync` and move straight on. `ConnectAsync` returning does not mean you are in the world: it means the play phase has started, which is before the join packet - let alone the teleport that places the player - has been applied. A whitelist rejection or a ban arrives in exactly that window, after login succeeds and before either one, so a sample that pressed on regardless would send chat into a session that is already over, or read a position that is really just the tracker's unset default.

`ConnectAndWaitForSpawnAsync` is `UmpkClient`'s answer to that window:

```csharp
if (!await client.ConnectAndWaitForSpawnAsync(endpoint, ct))
{
    return 1;
}
```

It connects, then awaits `client.Spawned`, a task armed before `ConnectAsync` is even called and re-armed synchronously inside it, ahead of its first await, so nothing the server sends can outrace the subscription that completes it. `Spawned` resolves true once the server has actually placed the player - keyed on the initial position teleport, not on the join packet - and false the moment the session ends first, kick or otherwise. `ConnectAndWaitForSpawnAsync` never throws for a clean session end. It returns `false` and lets the caller decide, which is what the `if` above does. Reach for `client.Spawned` directly, or the plain `ConnectAsync`, if you need the play phase to start without waiting for the world - `ConnectAsync`'s own contract (return before the teleport) is unchanged either way.

## Sending and leaving

```csharp
await client.Actions.Chat.SendChatAsync("Hello from UMPK.", ct);
```

`client.Actions` groups the outbound API: `Chat`, `Movement`, `Inventory`, `Interaction`, `Dialog`, `Session`, plus a `Capabilities` view of what the negotiated version can actually do. `SendChatAsync` applies the instance chat cooldown, splits anything past 256 characters, and picks the signed or unsigned path by version. [Chat and signing](../guides/chat-and-signing.md) covers what that means.

The shutdown has one detail worth stealing:

```csharp
// The logout gets an uncancelled token, because on the Ctrl+C path ct is already cancelled and the
// disconnect packet still has to reach the server.
await client.DisconnectAsync(CancellationToken.None);
```

If you pass the already-cancelled token here, the disconnect never reaches the server and you leave a half-open socket behind. The same reasoning drives the Ctrl+C handler at the top of the file, which sets `eventArgs.Cancel = true` to take the interrupt away from the runtime so the session gets to close itself.

`UmpkClient` implements `IAsyncDisposable`, and `DisposeAsync` calls `DisconnectAsync` for you, so the `await using` would have covered it. Doing it explicitly makes the ordering visible.

## What to read next

- [Offline samples](offline-samples.md) to try chat, NBT, physics, versions, and identity with no server.
- [A connected bot](connected-bot.md) to ping, log in, join, and trade chat lines.
- [Authentication](../guides/authentication.md) to replace the offline profile with a real account.
- [Chat and signing](../guides/chat-and-signing.md) for what 1.19 changed about sending a message.
- [Movement and pathfinding](../guides/movement-and-pathfinding.md) to make the bot go somewhere.
- [Umpk.Client](../packages/umpk-client.md) for the rest of the surface.
