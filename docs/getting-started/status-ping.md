---
title: Your first status ping
description: A walkthrough of the StatusPing sample: SRV resolution, JavaStatus.QueryAsync, the legacy fallback, and reading the MOTD.
sidebar:
  order: 2
---

A server list ping is what a launcher does when it draws one row in the multiplayer menu: connect, ask, print, hang up. It is the smallest useful thing UMPK does, and it needs no version, no account and no client.

## Run the sample

1. Run the sample against a host name. The sample uses port 25565 and resolves the SRV record.

   ```bash
   dotnet run --project samples/StatusPing -- mc.example.com
   ```

2. To ping a port other than 25565, pass the port as a second argument. The sample then skips the SRV lookup.

   ```bash
   dotnet run --project samples/StatusPing -- 127.0.0.1 25566
   ```

Against a live 1.21.8 server the program prints this:

```text
Host:     127.0.0.1:25760
Version:  1.21.8 (protocol 772)
Players:  0/20
Latency:  6 ms
MOTD:
  A Minecraft Server
```

The whole program is `samples/StatusPing/Program.cs`, 172 lines including the error handling. What follows walks through it.

## Picking an endpoint

`ServerEndpoint` is a record of a host string and a `ushort` port, with `DefaultJavaPort` as a constant equal to 25565.

```csharp
ushort port = ServerEndpoint.DefaultJavaPort;
if (args.Length == 2 && !ushort.TryParse(args[1], CultureInfo.InvariantCulture, out port))
{
    Console.Error.WriteLine($"'{args[1]}' is not a port number.");
    return 2;
}

var requested = new ServerEndpoint(args[0], port);
var options = new JavaStatusOptions { Timeout = TimeSpan.FromSeconds(10) };
```

`JavaStatusOptions` has exactly two settable properties, `Timeout` and `Logger`, both init-only. The timeout is not a socket timeout: `JavaStatus.QueryAsync` links your cancellation token with an internal source and calls `CancelAfter(options.Timeout)`, so the whole exchange is bounded and a timeout surfaces as `OperationCanceledException`.

## SRV resolution, and when to skip it

```csharp
// Vanilla only looks up the _minecraft._tcp SRV record when the player typed no port, so neither
// does this. DnsSrvResolver returns the endpoint unchanged for IP literals and for hosts with no
// record, which is why there is no "did it work" branch here.
ServerEndpoint endpoint = requested.Port == ServerEndpoint.DefaultJavaPort
    ? await new DnsSrvResolver().ResolveAsync(requested, CancellationToken.None)
    : requested;
```

`DnsSrvResolver` is a small UDP DNS client with no external dependency. It queries `_minecraft._tcp.<host>` for SRV records, and when several come back it takes the lowest priority, breaking ties on the highest weight. It returns the input unchanged for an IP literal, for a host with no record, for a truncated response, and when the machine has no DNS servers configured. There is no failure signal to branch on, which is the point.

If you want to skip the lookup entirely, `DnsSrvResolver.Passthrough` is a static `IServerAddressResolver` that returns whatever you give it.

## The query

```csharp
ServerStatus status = await JavaStatus
    .QueryAsync(endpoint, TcpConnectionFactory.Shared, options, CancellationToken.None);
```

`TcpConnectionFactory.Shared` is the built-in `IConnectionFactory`; it hands back an `IDuplexPipe` over a TCP socket. Supplying your own is how you would route through a proxy or feed the exchange from a recording.

There is a convenience overload that does the resolution for you, if you would rather not hold both pieces:

```csharp
static Task<ServerStatus> QueryAsync(
    string host, ushort port, JavaStatusOptions options, CancellationToken ct,
    IServerAddressResolver? resolver, IConnectionFactory? factory);
```

Passing null for either falls back to a fresh `DnsSrvResolver` and `TcpConnectionFactory.Shared`. It also carries the same SRV gate the sample applies by hand: the resolver is only consulted when `port` is 25565, matching vanilla's own status pinger, so a non-default port never touches DNS. A resolver that throws is logged at debug and the address is pinged as given, rather than failing the query.

Under the hood the exchange is hand-rolled rather than run through the packet codec framework, because the status wire format is frozen and version blind. The handshake goes out with protocol version -1 and next state 1, then an empty status request, then the JSON response is read, then a ping frame carrying a `Stopwatch` timestamp that the server echoes back. That echo is the latency measurement.

## What ServerStatus carries

Everything vanilla's own `ServerStatus` record carries, decoded for you, plus the raw response and the measured latency:

```csharp
public sealed class ServerStatus
{
    public required string Json { get; init; }
    public required TimeSpan Latency { get; init; }
    public string? VersionName { get; init; }
    public int? Protocol { get; init; }
    public int? OnlinePlayers { get; init; }
    public int? MaxPlayers { get; init; }
    public IReadOnlyList<ServerStatusPlayer> Sample { get; init; }
    public Component? Description { get; init; }
    public ReadOnlyMemory<byte> Favicon { get; init; }
    public bool EnforcesSecureChat { get; init; }
}
```

`Json` is still the response body verbatim, unparsed, alongside the decoded fields. This is deliberate: the fields servers actually put in there go well past the ones vanilla documents, so freezing a shape into the library would throw away half of what arrives if `Json` were not kept too.

Every field past `Json` and `Latency` is decoded defensively: a missing or malformed sub-object yields `null` (or an empty collection) for that field rather than failing the whole parse, mirroring vanilla's own lenient decoding of the status document. `Protocol` accepts a stringified `version.protocol` too, since real proxies emit one even though vanilla itself would show no version at all for that shape. `Sample` is all-or-nothing: one malformed UUID in `players.sample` empties the whole list rather than dropping just that entry, matching vanilla.

The sample reads the decoded fields directly:

```csharp
Console.WriteLine($"Version:  {status.VersionName ?? "unknown"} (protocol {ProtocolText(status.Protocol)})");
Console.WriteLine($"Players:  {PlayersText(status)}");
```

## Reading the MOTD

The `description` field is a bare string on old servers and a chat component tree on new ones. `ServerStatus.Parse` reads both shapes into `Description`, a `Component`; `ToPlainText()` flattens it:

```csharp
PrintMotd(status.Description?.ToPlainText() ?? string.Empty);
```

A string-shaped description containing section-sign codes has them expanded into styles while parsing, so the plain text that comes back is already clean; there is nothing left for the sample to strip for this path. (The legacy fallback below still needs to, because its MOTD is never a component.)

`ToPlainText` takes an optional `ITranslationSource` if you want `translate` components resolved; without one they fall back to their key or their `with` arguments.

More on the component model in [Umpk.Text](/packages/umpk-text).

## The legacy fallback

Servers up to 1.6 have no modern status handshake at all. They answer one with a kick packet or hang up, which arrives here as `ProtocolViolationException` or `ConnectionClosedException`. That is the cue, and the only cue:

```csharp
catch (Exception ex) when (ex is ProtocolViolationException or ConnectionClosedException)
{
    // Servers up to 1.6 have no modern status handshake. They answer it with a kick packet or hang
    // up, which reaches us as one of these two faults, so that is the cue to try the old
    // 0xFE 0x01 ping instead.
    Console.Error.WriteLine("No modern status response. Retrying with the legacy 1.6 ping.");
    LegacyServerStatus legacy = await JavaLegacyPing
        .QueryAsync(endpoint, TcpConnectionFactory.Shared, options, CancellationToken.None);
    PrintLegacyStatus(endpoint, legacy);
}
```

`JavaLegacyPing.QueryAsync` has the same signature as the modern one and returns a different record:

```csharp
public sealed record LegacyServerStatus(
    string? MinecraftVersion, string Motd, int OnlinePlayers, int MaxPlayers, TimeSpan Latency);
```

It writes the `0xFE 0x01 0xFA "MC|PingHost"` request straight onto the pipe, not through `JavaConnection`, because the legacy ping predates the framed protocol entirely. `Motd` here is a plain string with section sign codes in it, never a component, so the same strip function handles it.

One honest gap, which the sample calls out rather than papering over: the legacy reply does carry a protocol number, but `LegacyServerStatus` does not expose it, so the sample prints `(legacy ping, no protocol number)` instead of inventing one.

Modern servers still answer the legacy ping, so you can also reach for `JavaLegacyPing` directly if that is what you want. The fallback ordering in the sample is about detecting old servers, not about preferring one format.

## Failure modes

The sample catches five things at the top level, and each one means something different:

- `SocketException`, which it translates by `SocketErrorCode` into "no such host", "refused", "unreachable" and so on.
- `OperationCanceledException`, which given the token above means the timeout elapsed.
- `ConnectionClosedException`, which carries a `Reason` describing how the connection ended.
- `ProtocolViolationException`, meaning bytes arrived but were not a status response.
- `JsonException`, meaning the response was not valid JSON.

Worth noting that `ProtocolViolationException` and `ConnectionClosedException` are UMPK's own types from `Umpk.Protocol.Java`, not the `System.Net` type of the same name. If you have a `using System.Net;` in scope you will need to disambiguate.

## Next

[A minimal bot](/getting-started/minimal-bot) uses this same ping as its first step, to find out which protocol the server speaks before building a client for it.
