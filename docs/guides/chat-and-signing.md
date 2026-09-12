---
title: Chat and signing
description: Sending and receiving chat, what 1.19 changed with message signing and session keys, and which half of it you have to supply.
sidebar:
  order: 2
---

Before 1.19, chat was a string in one direction and a chat component in the other. Then Mojang added cryptographic signatures, and the shape of "send a message" changed three times in four releases. UMPK hides almost all of that. What it cannot hide is that you must supply the key material.

## Receiving

One event, whatever the version:

```csharp
using IDisposable chatSubscription = client.Events.Subscribe<ChatMessageReceived>(
    chat => Console.WriteLine(chat.Message.ToPlainText()));
```

`ChatMessageReceived` is a record with a positional core and several extra init properties:

```csharp
public sealed record ChatMessageReceived(
    Component Message,
    ChatCategory Category,
    Component? SenderName,
    Guid? SenderId,
    bool IsOverlay,
    ChatVerification Verification = ChatVerification.NotApplicable) : IClientEvent
```

`Message` is the composed line, decorated through the server's chat type where one applied. `Body` is the undecorated body, equal to `Message` on every era that composes server-side. `TargetName` carries the whisper or team parameter. `ChatTypeId` is the chat type's registry id, normalized across the 1.21 encoding change, and is -1 when the era carries none. It is how you tell a whisper from a `/say` without re-parsing the rendered text.

`Category` is one of `Legacy`, `Player`, `System` or `Disguised`. `Verification` is `NotApplicable`, `Verified`, `Unverified`, `Failed` or `Insecure`. On a pre-1.19 server every message is `ChatCategory.Legacy` with `ChatVerification.NotApplicable`, so a consumer that only wants text can ignore all of it and read `Message`.

Peer verification runs whether or not you sign anything. Verifying somebody else's message needs only their announced profile key and the version's era, so the client installs a verifier on every signing-era session, provider or not. On 1.8 through 1.18.2 there is nothing to verify, because no peer ever announces a key.

Related events, for the parts of the chat stream that are not messages: `ChatMessageDeleted` (a server retracting a message by id or signature), `ChatMessageSuppressed` (filtered out entirely), `ChatReset`, and `ChatStreamGap` (the 1.21.5+ global chat index skipped, which means you missed something).

## Sending

```csharp
await client.Actions.Chat.SendChatAsync("Hello from UMPK.", ct);
await client.Actions.Chat.SendCommandAsync("/msg Steve hello", ct);
```

`SendChatAsync` waits out the cooldown, splits anything longer than 256 characters into 256-character chunks, and picks the signed or unsigned path from the negotiated version. The cooldown is `ClientOptions.ChatCooldown`, which defaults to one second; set it to `TimeSpan.Zero` to disable the throttle.

`SendCommandAsync` accepts the command with or without a leading slash, and this is not cosmetic. Below 1.19 a command really is a chat frame with a slash, because vanilla's pre-1.19 `handleChat` routes a leading slash into the dispatcher. From 1.19 it does not: `handleChat` only broadcasts a message, and only `handleChatCommand` and `handleSignedChatCommand` reach `Commands.performCommand`. So protocols 759 through 765 send `minecraft:chat_command`, and 766 onward send the bare unsigned `minecraft:chat_command` when nothing in the command is signable, or `minecraft:chat_command_signed` when something is. Send a slashed string through `SendChatAsync` on a modern server and it is broadcast as a message with a slash in it, not run.

Two more members on `ChatActions`:

```csharp
public Task<CompletionResult> CompleteAsync(string input, int cursor, CancellationToken ct = default);
public IReadOnlyList<SignedArgumentSpan> GetSignedArguments(string commandBody);
```

`CompleteAsync` merges three sources: suggestions from your own host commands registered on `client.Commands`, a local walk of the server's declared command tree, and a server tab-complete round trip when the tree says the token needs one. If the server round trip fails, it logs at debug and returns the local results rather than throwing. `GetSignedArguments` tells you which arguments of a command the server tree marks signable, which is what the send path uses to decide between the two 1.20.5+ command packets.

## What changed at 1.19

Three signature eras, and the library selects between them from the dataset rather than from a version comparison:

- `ChatSignatureEra.V1_19` (protocol 759). The signature covers salt, timestamp and the message. Nothing acknowledges anything.
- `ChatSignatureEra.V1_19_1` (protocol 760). Two-stage signature over a body that folds in a five-entry last-seen window, chained onto the previous message's signature, with the same window repeated on the packet.
- `ChatSignatureEra.V1_19_3` (761 and up). The linked message body plus an offset and bitset acknowledgement window over the last twenty messages.

In every era the window that goes into the signature and the window written on the packet come from one snapshot. A server re-derives the signed body from what the packet declares, so if the two ever disagree the signature fails. That invariant is why the send path exists as one closed scope rather than as a resolve step and a send step.

Where the profile key travels also moved. On 1.19 and 1.19.1 the public key rides the login hello, so there is exactly one chance to present it and no way to replace it mid-session. From 1.19.3 the key moves to a `minecraft:chat_session_update` packet sent during play, which means it can be rotated.

## What the library does and what you supply

You supply certificates. That is the whole of your side:

```csharp
public interface IChatSigningProvider
{
    ValueTask<PlayerCertificates?> GetCertificatesAsync(CancellationToken cancellationToken);
}
```

```csharp
public sealed record PlayerCertificates(
    string PublicKeyPem, string PrivateKeyPem,
    string PublicKeySignature, string PublicKeySignatureV2,
    DateTimeOffset ExpiresAt, DateTimeOffset RefreshedAfter);
```

Wire it in with `UmpkClientBuilder.UseChatSigning(provider)`. If you never call it, or the negotiated version has no signing era, the send path stays unsigned, byte-identical to what an offline session emits. An `enforce-secure-profile=false` server takes those happily.

`MinecraftAuthFlow.GetCertificatesAsync(session, ct)` from [Umpk.Auth](/guides/authentication) fetches and caches exactly this record, so a provider is usually a thin wrapper around it.

The client owns everything else: the per-session `ChatSigningSession`, the era derived from the version, the message index and signature chain, both flavors of last-seen window, the rotation bookkeeping, and the `chat_session_update` announcement.

Returning null means "no certificates", and the session falls back to unsigned. Throwing is safe: every exception is caught and treated exactly like null.

### When your provider is called

Not once. The contract is specific, and worth sizing your caching against:

- Once at session start, on every signing-era version. On 1.19 and 1.19.1 that call happens before the login hello, because the key rides the hello.
- From 1.19.3, at most once an hour from the session tick while the held key is past its `RefreshedAfter` instant, or while no key is held. The floor is vanilla's own `MINIMUM_PROFILE_KEY_REFRESH_INTERVAL`, and it is armed before the call, so a provider that fails, hangs or returns null is not retried sooner for either reason.
- From 1.19.3, when a send finds the held certificates expired: once immediately, jumping that floor, then back onto the hourly floor until something usable arrives. Usable means non-null and not already expired. One attempt per episode, not one per message.

A call can also be abandoned. The client stops waiting after 30 seconds and proceeds without an answer, cancelling the token you were given; a result produced after that point is discarded.

### One hard constraint

Do not send chat or run a command from inside `GetCertificatesAsync`, and do not block on anything that does. The client holds the chat-signing lock across the whole of resolve, sign and send, so a provider that re-enters the send path waits on a lock its own caller holds. Every other client API is safe to call from there.

This is not discoverable from the provider interface. Re-entering the send path deadlocks because the caller already holds the signing lock.

## Why the announcement waits for the join packet

On 1.19.3 and later the client has to announce its chat session before any signed message is accepted. The obvious place to do that is right after login succeeds. It is the wrong place, and the reason is a wire fact rather than a preference.

A vanilla server switches its channel protocol to play only when it sends its own first play packet, which is the join packet. Between login success and that packet, the server's inbound decoder still holds the login packet set, which has three entries. A `chat_session_update` arriving in that window gets looked up by its play-phase wire id in a three-entry list, and the server dies with `IndexOutOfBoundsException: Index 32 out of bounds for length 3`. It then disconnects using the login disconnect packet, which a client already reading under the play table decodes as `add_entity`. The symptom is a phantom entity spawn and a dead connection, which points nowhere near the real cause.

So UMPK defers the whole setup, certificate acquisition included, to the join applier. Vanilla puts the same send in `ClientPacketListener.handleLogin`, for the same reason.

The acquisition moved along with the announcement deliberately. The coordinator announces a key in the same lock hold that installs it, so acquiring early and announcing late would still put the frame on the wire at acquisition time. Nothing can be signed before the join in any case.

If the setup fails, the client logs a warning and chat goes out unsigned. That claim is precisely true because the announcement happens before the install: a failed setup leaves nothing installed, so nothing is ever signed with a key the server was never told about.

## Rotation, and why the scope is the contract

`ClientState.Chat` exposes what the session knows about its own key:

```csharp
public int NextGlobalIndex { get; }
public int ObservedGaps { get; }
public bool TracksGlobalIndex { get; }
public bool ServerPreviewsChat { get; }
public int ProfileKeyRotations { get; }
public bool ProfileKeyRotationSupported { get; }
public DateTimeOffset? ProfileKeyRefreshedAfter { get; }
```

`ProfileKeyRotationSupported` is true only from 1.19.3, where `chat_session_update` exists.

A rotation swaps the chat session id, the key and the message index together, and announces the swap on the wire. A sender that resolved its signing state before the swap and sent after the announcement puts a message behind it that the server cannot verify, and a 1.19.2 server answers that by disconnecting rather than dropping. That is why `ChatSigningState` is handed to the send body inside a scope instead of being returned from a resolve call. Carrying one out of its scope and signing with it later reintroduces exactly that window.

## The unsigned path in detail

With no provider, no certificates, or a version with no signing era, a message goes out as:

- `minecraft:chat` with just the text, on protocols with `ChatSigning` of `"none"`.
- The signed chat packet with a null signature, current timestamp, random salt, and an empty acknowledgement (offset 0, three zero bytes, checksum 0) on signing-era versions.

The second shape is what an offline session emits, byte for byte. Servers running with `enforce-secure-profile=false` accept it; servers that enforce it do not.

## Related reading

- [Authentication](/guides/authentication) for where `PlayerCertificates` comes from.
- [Era gating](/concepts/era-gating) for how the version-dependent behavior is selected.
- [Umpk.Client](/packages/umpk-client) for the rest of the action surface.
