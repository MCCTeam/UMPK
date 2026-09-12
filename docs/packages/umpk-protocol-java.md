---
title: Umpk.Protocol.Java
description: Codecs, packet registration, framing and encryption, login, chat signing and status pings for 49 protocols.
sidebar:
  order: 7
---

`Umpk.Protocol.Java` is the wire. It owns framing (length prefix, compression, AES-CFB8 encryption), the packet model and its registration tables, the login and configuration handshakes, chat message signing, and the two status pings. It is the biggest package in the repository, and the one where "vanilla decides" is enforced hardest: a codec is proven by decoding bytes a real server sent, then re-encoding them byte-identically.

Nothing in here knows which version it is talking to until you hand it a `ProtocolDescriptor`. That is what keeps 49 protocols in one codebase.

## Its place in the stack

It depends on [Umpk.Core](/packages/umpk-core), [Umpk.Nbt](/packages/umpk-nbt), [Umpk.Text](/packages/umpk-text) and [Umpk.Game](/packages/umpk-game). It does not depend on [Umpk.Data.Java](/packages/umpk-data-java); the arrow points the other way, because the generated descriptors are built out of types defined here.

[Umpk.Client](/packages/umpk-client) drives it, and [Umpk.Auth](/packages/umpk-auth) implements its `ISessionAuthenticator` seam.

## Main entry points

Status, if all you want is a ping. `JavaStatus.QueryAsync` runs the modern handshake and returns a `ServerStatus` (the raw JSON plus a measured latency). `JavaLegacyPing.QueryAsync` runs the pre-1.7 `0xFE 0x01` ping and returns a `LegacyServerStatus`. Both take a `JavaStatusOptions` with a `Timeout` and a `Logger`.

Transport. `IConnectionFactory` produces an `IDuplexPipe`; `TcpConnectionFactory.Shared` is the plain one, and `Socks5ConnectionFactory` and `HttpConnectConnectionFactory` take a `ProxyOptions`. `IServerAddressResolver` resolves the endpoint; `DnsSrvResolver` does the `_minecraft._tcp` lookup and `DnsSrvResolver.Passthrough` does not.

Socket factories return a resource-owning `SocketDuplexPipe`. Completing its input and output preserves the ordinary pipe shutdown contract, while disposing `JavaConnection` also disposes that adapter and closes its `NetworkStream` and socket. Custom or in-memory `IDuplexPipe` implementations are disposed only when they explicitly implement `IAsyncDisposable` or `IDisposable`.

`JavaConnection` wraps a pipe and turns it into packets. It owns the phase (`SetPhase`), compression (`EnableCompression`), encryption (`EnableEncryption`), the codec binding (`BindCodec`, `SetCodecState`), and three receive shapes: `ReceiveAsync` for one item, `ReceiveAllAsync` for a stream of decoded `InboundItem`, and `ReceiveFramesAsync` for raw `InboundFrame`. `JavaConnectionOptions` carries the frame length cap, timeouts, and the two failure policies.

`JavaClientLogin.LoginAsync` performs handshake, login and encryption, returning a `LoginResult` with the resolved UUID, username and the phase you landed in. `JavaClientLogin.RunConfigurationPhaseAsync` runs the 1.20.2+ configuration phase. `JavaServerLogin` and `IServerSessionVerifier` are the server-role counterparts.

`ProtocolDescriptor` is the per-version table: `GetRegistry(phase, flow)` yields a `PhaseRegistry` that maps wire ids to `BoundPacketCodec` entries and back. `ProtocolFeatures` is the era axis set (`ChatSigning`, `ItemStackFormat`, `NbtWireFormat`, `ComponentEra`, `Bundles`, `ConfigurationPhase`, `HashedSlots` and more). `ProtocolDescriptorBuilder` and `PacketRegistrar.Register` build one.

`Umpk.Protocol.Java.Codecs` has `PacketCodec<T>` with `Encode` and `Decode`, plus `PacketReader` and `PacketWriter`, the ref-struct readers and writers with the full Minecraft primitive set (VarInt, VarLong, angle, block position by layout, NBT by format, component by era). `PacketCodecContext` carries the registries and per-connection codec state; `PacketCodecContext.Registryless` is the one for codecs that need neither.

`Umpk.Protocol.Java.Packets` holds the packet records and their payload types, 332 public types in all, plus the static tables that name them: `HandshakePackets`, `StatusPackets`, `LoginPackets`, `ConfigurationPackets`, `PlayPackets` and the family groupings (`EntityPackets`, `WorldPackets`, `ItemPackets`, `UiPackets`, `CommandsPackets`).

`Umpk.Protocol.Java.Signing` covers 1.19+ chat: `ChatSigningSession`, `PlayerCertificates`, `ChatSignatureEra`, `SignedChatVerifier`, `MessageSignatureCache`, `LastSeenMessagesTracker` and `LastSeenMessagesCollector`.

## Example: a status ping

Adapted from `samples/StatusPing/Program.cs`. The status exchange is version-blind, so this never picks a protocol.

```csharp
using Umpk;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;

var requested = new ServerEndpoint("mc.example.com");
var options = new JavaStatusOptions { Timeout = TimeSpan.FromSeconds(10) };

// Vanilla only does the SRV lookup when the player typed no port, so neither does this.
ServerEndpoint endpoint = requested.Port == ServerEndpoint.DefaultJavaPort
    ? await new DnsSrvResolver().ResolveAsync(requested, CancellationToken.None)
    : requested;

try
{
    ServerStatus status = await JavaStatus.QueryAsync(
        endpoint, TcpConnectionFactory.Shared, options, CancellationToken.None);
    Console.WriteLine(status.Json);
    Console.WriteLine($"{status.Latency.TotalMilliseconds:F0} ms");
}
catch (Exception ex) when (ex is ProtocolViolationException or ConnectionClosedException)
{
    // Servers up to 1.6 answer the modern handshake with a kick or a hang-up.
    LegacyServerStatus legacy = await JavaLegacyPing.QueryAsync(
        endpoint, TcpConnectionFactory.Shared, options, CancellationToken.None);
    Console.WriteLine($"{legacy.MinecraftVersion}: {legacy.OnlinePlayers}/{legacy.MaxPlayers}");
}
```

## Example: a codec round trip

This is the shape every codec test in the repository uses.

```csharp
using System.Buffers;
using Umpk.Protocol.Java.Codecs;

static T Cycle<T>(PacketCodec<T> codec, T value)
{
    var buffer = new ArrayBufferWriter<byte>();
    var writer = new PacketWriter(buffer);
    codec.Encode(ref writer, value, PacketCodecContext.Registryless);

    var reader = new PacketReader(buffer.WrittenSpan);
    T decoded = codec.Decode(ref reader, PacketCodecContext.Registryless);
    // A codec that leaves bytes on the floor is a codec that is wrong.
    if (reader.Remaining != 0)
    {
        throw new InvalidOperationException($"{reader.Remaining} bytes unread.");
    }

    return decoded;
}
```

## Things that catch people out

`ServerStatus.Json` is the response verbatim. This package does not parse it, on purpose: the fields servers actually put in there go well past what vanilla documents, and a typed model would either lose them or lie about them. Parse the JSON yourself with `System.Text.Json`. Check the value kind of each element before you read it: proxies stringify the protocol number often enough to matter.

`LegacyServerStatus` does not expose a protocol number even though the legacy reply carries one. If you need it, you cannot get it from this type today.

`JavaStatus.QueryAsync` cancels its own token when `Timeout` elapses, so a timeout surfaces as `OperationCanceledException`, not as a timeout exception type.

The receive path has three shapes and they are not interchangeable. `ReceiveAsync` and `ReceiveAllAsync` give you decoded `InboundItem` values. `ReceiveFramesAsync` gives you raw `InboundFrame` values with the wire id and payload untouched. Use the frame shape to proxy or to record. Use the item shape to play.

`UnknownPacketPolicy` and `DecodeFailureMode` are separate settings for separate problems. The first covers a wire id with no registration (`Throw`, `Preserve`, `Skip`). The second covers a registered codec that threw (`FailConnection`, `SkipFrameAndReport`, `ForwardVerbatim`). `Preserve` and `ForwardVerbatim` can relay bytes without producing a decoded item, so choose them deliberately and test the decoded values rather than only checking that the session survived.

Under the default `FailConnection` policy, a mapped codec failure is observable through `JavaConnection.PacketDecodeFailed`. The record includes protocol, phase, flow, wire id, packet id, codec identity, payload length and the original exception. Retained payload evidence is bounded to 256 bytes and is always empty during handshake and login so authentication material does not enter diagnostics. A fatal frame is reported once; exceptions thrown by diagnostic subscribers are contained and cannot replace the protocol failure.

The login and configuration drivers use the same reporting boundary for required packets that they decode manually. Cookie requests are answered in their actual phase, never deferred to play. Cookie payloads are limited to 5,120 bytes on both encode and decode; an oversized resolver value is ignored without overwriting a valid stored value. During configuration the client claims no known data packs, then installs the authoritative registries before acknowledging `finish_configuration`, so the first play packet is decoded with the final registry context.

`PacketReader` and `PacketWriter` are ref structs passed by `ref`. You cannot capture them in a lambda, store them in a field, or use them across an `await`. That is why `ReaderFunc<T>` and `WriterAction<T>` exist as their own delegate types.

Encryption and compression are enabled at exact points in the login flow, and the descriptor knows where: `IsCompressionEnablePoint` and `IsEncryptionEnablePoint` take a phase, a flow and a wire id. Turning either on one frame early or late corrupts the stream with no useful error. Let `JavaClientLogin` do it.

`JavaServerLogin`, `JavaServerLoginOptions`, `ServerLoginResult` and `IServerSessionVerifier` exist in this package even though `Umpk.Server` is empty. They are the server side of the login handshake and nothing else. There is no server-side connection layer above them.

Chat signing has three eras (`ChatSignatureEra.V1_19`, `V1_19_1` and `V1_19_3`) and they differ in what gets hashed and how the last-seen window is tracked, not just in packet layout. See [chat and signing](/guides/chat-and-signing).

Bundles (`BundleAccumulator`, `BundleFeed`, `PacketBundle`) only exist from 1.19.4 on. The gate is the binding itself: `BoundPacketCodec` marks an entry `FrameRole.BundleDelimiter` when its identifier is `minecraft:bundle_delimiter`, so a protocol whose dataset registers no such packet has no delimiter to mark and everything arrives loose.
