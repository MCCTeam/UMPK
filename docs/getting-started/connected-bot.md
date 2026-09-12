---
title: A connected bot
description: Status ping, auth, connect, and an interactive chat loop in one program.
sidebar:
  order: 5
---

` samples/ConnectedBot/Program.cs` ties four layers together: it pings the server, logs in, joins, and then trades chat lines with the server until you type `/quit`. It is the sample to read after [a minimal bot](/getting-started/minimal-bot), which says one line and leaves, and after the [offline samples](/getting-started/offline-samples), which need no server.

## Run the sample

Run it against a server you control. In offline mode the second argument is the player name:

```bash
dotnet run --project samples/ConnectedBot -- localhost Steve
```

With `--online` the second argument is the login hint, usually your email, and the sample runs the Microsoft device code flow before joining:

```bash
dotnet run --project samples/ConnectedBot -- localhost player@example.com --online
```

In chat, type a line and press Enter to send it. A leading slash runs a command instead. Type `/quit` to leave. After Ctrl+C, press Enter once so the read loop notices the cancel and runs the logout.

## Step 1, ping

The sample resolves the address the same way vanilla does, with an SRV lookup only when you typed no port, and then pings:

```csharp
ServerStatus status = await JavaStatus.QueryAsync(
    endpoint, TcpConnectionFactory.Shared, new JavaStatusOptions(), ct);
```

The ping answers two questions at once. It prints the MOTD, version, and player count, and its `version.protocol` field picks the protocol the client is built for. A client speaks exactly one protocol, so every bot starts this way. `MinimalBot` does the same lookup with less printing around it.

## Step 2, auth

Without `--online`, auth is one line. Offline mode is the absence of an authenticator:

```csharp
profile = OfflineIdentity.ComputeProfile(who);
```

With `--online`, the sample logs in through `MinecraftAuthFlow` with a file token store under your user profile, never inside the repo. Repeat runs resume the cached session with no browser step. The session service and the flow stay alive for the whole session, because the client borrows both and owns neither. The token cache, the resume call, and the device code interaction are the same shape as in `samples/AuthOnline/Program.cs`, split out there so you can read auth with no server involved.

## Step 3, build and connect

Registries come next. Item stacks and entity types decode against them, so the sample installs them before joining:

```csharp
var builder = new UmpkClientBuilder()
    .UseVersion(version)
    .UseProfile(profile)
    .UseStaticRegistries(JavaGameData.Registries(version.Version.Protocol));
```

Online mode adds two calls: `UseAuthenticator` with the session service and profile credentials, and `UseChatSigning` with a thin wrapper around the flow certificate cache. Without certificates, chat on 1.19 and later goes out unsigned, which open servers accept and strict ones reject.

Then the sample waits for the spawn, not just play start:

```csharp
if (!await client.ConnectAndWaitForSpawnAsync(endpoint, ct))
    return 1;
```

A whitelist or ban kick lands between those two points. Waiting only on connect would treat a session that is already over as a live one. After the spawn, the sample reads its own position through `InvokeAsync`, which marshals the read onto the session loop so the print cannot race packet application.

## Step 4, received chat

Inbound chat arrives on one event on every version:

```csharp
using IDisposable chatSubscription = client.Events.Subscribe<ChatMessageReceived>(
    chat =>
    {
        string line = FormatChat(chat, translations);
        if (line.Length > 0)
            Console.WriteLine(line);
    });
```

`Message` is the composed line, already decorated with the sender name on eras that decorate server side, so the sample prints it as is. The category picks the prefix: `[chat]`, `[system]`, or `[disguised]`. Overlay lines are the action bar and print apart as `[actionbar]`. Player messages add a short standing note when the signature did not verify cleanly: `(?)` for unverified, `(rejected)` for a failed check, `(unsigned)` for no signature at all. Blank lines print as nothing, since empty action bar clears would spam the log.

Translations come from `VanillaTranslations.ForProtocol(protocol)`, the same protocol the ping reported. Without a table, translate keys print raw, which suits logs and confuses humans.

Handlers run on the session loop, so each one formats and returns. Slow work inline would stall packet processing for the whole session.

## Step 5, the chat loop

The loop is deliberately plain. `Console.ReadLine` blocks, so a kick while you are typing surfaces on the next Enter, when the loop top sees the disconnect signal first:

```csharp
string? line = await Task.Run(Console.ReadLine, CancellationToken.None);
if (line is null)
    break;
if (line.Equals("/quit", StringComparison.OrdinalIgnoreCase))
    break;
if (line.StartsWith('/'))
    await client.Actions.Chat.SendCommandAsync(line, CancellationToken.None);
else
    await client.Actions.Chat.SendChatAsync(line, CancellationToken.None);
```

A leading slash runs a command. That branch matters: on 1.19 and later, sending a slashed string through the chat path broadcasts it as a message instead of running it. The logout takes a fresh token, because on the Ctrl+C path the loop token is already cancelled and the disconnect packet still has to reach the server.

## What to read next

- [Offline samples](/getting-started/offline-samples) for each layer on its own with no server.
- [Authentication](/guides/authentication) for the online, offline, and third party flows in full.
- [Chat and signing](/guides/chat-and-signing) for what 1.19 changed about sending a message.
- [Umpk.Client](/packages/umpk-client) for the rest of the action and event surface.
