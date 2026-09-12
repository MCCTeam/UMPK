---
title: "Limitations"
description: "What does not work, what is missing, and the known API gaps, listed plainly so you find them here rather than at three in the morning."
sidebar:
  order: 2
---

Every library has this list. Most keep it in the issue tracker where it is hard to read as a whole. Here it is in one place, with what I could verify against the source stated as verified and the rest marked for what it is.

## Not implemented at all

### Server support

`Umpk.Server` contains zero `.cs` files. So does `tests/Umpk.Server.Tests`. There is no server-side connection layer, no listener, no server-side login flow beyond the pieces `Umpk.Protocol.Java` uses in its own tests. The name is a placeholder in the solution, and treating it as intent rather than as shipped code will save you an afternoon.

### Proxy support

`Umpk.Proxy` is in exactly the same state: zero `.cs` files, empty test project. Some of the plumbing a proxy would need does exist in the protocol package, since `UnknownPacketPolicy.Preserve` and `DecodeFailureMode.ForwardVerbatim` are there specifically so a frame can be relayed verbatim. But nothing assembles those into a proxy.

### Bedrock Edition

Not supported, not started, and not planned as an extension of anything here. Bedrock runs over RakNet rather than TCP, and its protocol shares nothing meaningful with the Java Edition wire format. Support would mean a new package with its own transport, its own codecs and its own dataset. The `Umpk.Data.Java` and `Umpk.Protocol.Java` names carry the `Java` for this reason.

## Stability

No package has a frozen public API. Every declaration sits in `PublicAPI.Unshipped.txt`, and every `PublicAPI.Shipped.txt` in the repository contains a single line, `#nullable enable`, and nothing else. The practical consequence is that any public name can change without a deprecation cycle. If you build on this now, pin a version and read the diffs.

The analyzer still makes every public change visible as a diff on a checked-in text file, which is better than nothing. It is not the same as a compatibility promise.

## Data coverage

Recorded packet captures exist for all 49 protocols. A capture can still omit the packet or behavior that failed, so codec work also relies on the dataset and on hand-authored frames.

Piston push data exists for 31 protocols. A protocol without it gets an empty table, which the client reads as "the blocks a piston pushes are not modeled here". That is deliberately not the same statement as "every block behaves normally", and nothing in the pipeline is allowed to collapse the two.

Collision shapes on the flattened protocols are derived from a single modern snapshot, read positionally onto each version's own block state order. Renamed blocks are handled explicitly and an unresolved name is now left out rather than invented, but state schema drift is an open problem: blocks whose state schema changed since the snapshot land on the wrong entries. The extraction tooling measures this against a jar-driven extractor and reports 875 of 11,271 states wrong at 1.14.4, 861 of 11,337 at 1.15.2, 25 of 20,342 at 1.17.1, and none at 1.21.9. Walls, wall skulls, bells, beds, scaffolding, hoppers, grindstones, piston heads, cauldrons, anvils, chorus plants and lanterns are the affected blocks. Fixing it needs a per-version jar dump, which needs offline mappings that only some versions have.

Per-state physics fields (friction, speed factor, jump factor, fluid state) are not populated from extraction. They exist as material-level toggles in `features.json` instead.

## Documentation and tooling gaps

Continuous integration publishes the `MinimalBot` sample with `PublishAot`, enforces a 24 MiB binary budget, and runs the published binary against committed corpus frames and an in-memory server. CI does not run the live-server matrix because it does not provision vanilla server jars.

XML documentation warnings are suppressed repository-wide (`CS1591`), so a missing doc comment on a public member does not fail the build the way other warnings do.

## Known API gaps

These three came out of writing the sample programs, and each one is a case where the library knows something it will not tell you. I checked all three in the source.

### The legacy ping drops the protocol number

`JavaLegacyPing.Parse` reads the modern legacy-ping reply as `section sign`, `1`, protocol, version, motd, online, max, then constructs the result from fields 2 through 5:

```csharp
string[] parts = text[1..].Split('\0');
if (parts.Length >= 6)
{
    return new LegacyServerStatus(
        parts[2], parts[3], ParseInt(parts[4]), ParseInt(parts[5]), latency);
}
```

`parts[1]` is the server's protocol number. It is parsed out of the reply and then thrown away, because `LegacyServerStatus` has no field for it:

```csharp
public sealed record LegacyServerStatus(
    string? MinecraftVersion, string Motd, int OnlinePlayers, int MaxPlayers, TimeSpan Latency);
```

So the legacy ping path can report the version string but not the protocol number, even though the number arrived. The modern status ping does report it.

### A joined-game event cannot tell you which server it joined

`SessionInfo` has an `Endpoint` property, documented as "the server endpoint dialed for this session". The connection applier fills it in like this:

```csharp
await context.PublishAsync(new JoinedGame(new SessionInfo
{
    Profile = new GameProfile(self.Uuid, self.Username),
    Endpoint = new ServerEndpoint("unknown", 0),
    Version = context.Version,
    Phase = Umpk.Protocol.Java.ProtocolPhase.Play,
})).ConfigureAwait(false);
```

The host is the literal string `unknown` and the port is 0. An event consumer that wants to know which server it is connected to has to retain the endpoint it dialed. That is manageable for one connection and awkward for an application that manages several.

### There is no "spawned and ready" event

`JoinedGame` fires when the play phase begins and the login packet has been applied. That is earlier than the moment you usually care about, which is when the server has actually placed your player in the world. Nothing in `Umpk.Client.Events` signals that: there is `JoinedGame`, `Respawned`, `Died`, `PositionCorrected`, and no distinct readiness event.

A consumer can work around it by treating the first position correction as the spawn signal:

```csharp
IDisposable subscription = client.Events.Subscribe<PositionCorrected>(_ => MarkSpawned());
```

with a comment noting that the subscription has to be armed before connecting, because `ConnectAsync` returns as soon as the login phase finishes while the join packet and the initial teleport are still in flight. Subscribe after starting and you can miss the teleport and wait forever.

This remains a workaround. If you need a ready signal, arm the subscription before you connect.

### Logging out leaves a second copy of the token

`MinecraftAuthFlow.LoginAsync` caches a session under the hint you passed and, when the resolved profile name differs from it, caches a second copy under the profile name. `InvalidateAsync` removes only the keys for the hint you name:

```csharp
await _options.TokenStore.RemoveAsync(SessionKey(loginHint), ct);
await _options.TokenStore.RemoveAsync(CertificatesKey(loginHint), ct);
```

For a Microsoft account you log in with an email and the profile is a gamertag, so the two keys always differ. Calling `InvalidateAsync` with the email leaves a usable access token on disk under the gamertag. Invalidate both names until this is fixed.

Do not treat `InvalidateAsync` as a complete logout. Delete the token store file if you need certainty.

### The block data source has no public implementation

`Umpk.Game.World.World` requires an `IBlockDataSource`, the interface is public, and the only implementation in the library is `internal sealed class RegistryBlockDataSource` inside `Umpk.Client`. If you use `Umpk.Game` without a live client session, you must write your own. Five test projects each wrote one, which is the clearest sign the gap is real.

### The obvious physics profile entry point is not the correct one

`PhysicsProfile.ForProtocol` is public and looks like the way to build a profile. Its own documentation records a deliberate divergence from the dataset: it reports `FluidMovementEra.Legacy` for protocols 47 through 392, where the verified dataset says `SwimmingUpdate`. The mapping that production uses lives in an internal factory. Outside `Umpk.Client` you have to assemble the profile yourself from public inputs, which includes deriving the crouch height from the pose feature and the cadence from the protocol number.

### Dynamic registries cover the client model, not every vanilla registry

Configuration registry data is installed before play decoding and authoritative entries replace the corresponding static table. The client models the registry families needed by its world, item, attribute, biome and enchantment state; it does not expose every registry the vanilla client receives. For protocols with custom dimensions, an absent or unknown current dimension is a protocol failure rather than a silent Overworld fallback. Pre-custom-dimension protocols retain their legacy fallback.

### Modern recipe additions can be recognized without being represented

The modern recipe-book codec consumes every supported recipe-display form and publishes decoded entries atomically. If a bounded entry is recognized but cannot be represented, the packet records the opaque addition count instead of inventing a recipe. A malformed or truncated entry fails the whole packet and leaves the previous recipe state unchanged. `RecipeState.Displays` therefore contains only represented entries; check the opaque count in the recipe snapshot when completeness matters.

### Resource packs are downloaded, not rendered

The default client policy declines resource packs. The opt-in built-in policy can accept, download, bound the response size, verify a supplied SHA-1 and atomically cache the bytes. It reports `Downloaded`, not `SuccessfullyLoaded`, because this headless client does not apply textures, sounds or other visual assets. The legacy explicit `ResourcePackPolicy.Accept` shortcut makes the stronger claim without doing that work; a host choosing it owns the claim.

### Pathfinding rough edges

`PathfinderOptions.MaxReplans` is declared and defaulted to 5, and nothing reads it. The replan limit is hardcoded elsewhere.

`AStarPathFinder.BuildDefaultExpanders` is public, static, and returns `IMoveExpander[]`, but the only public constructor takes `IMove[]?`. The public factory cannot feed the public constructor.

`PathSegmentBuilder.FromPath` returns zero segments for a single-node path, and a `PathExecutor` built from zero segments reports `Complete` on its first tick without moving. Check the segment count before you drive an executor.

Submerged air budgeting is optimistic for short diagonal segments with startup, turn and settle cost. The controlled `pausebell` course measured 46–47 execution ticks for diagonals that the search and post-plan validator price at roughly 10–14 ticks. The life-safety supervisor correctly interrupts such a route before drowning, but that means a path which appears to schedule reachable breathing stops can still require recovery and replanning. Fixing this requires one conservative, move-type-aware timing model shared by search, validation and continuation pricing; increasing recovery budgets or suppressing the safety check would only hide the mismatch.

### Smaller sharp edges

`RealmServerAddress.Port` is an `int` while `ServerEndpoint.Port` is a `ushort`, so joining a Realm requires a cast. `Umpk.Protocol.Java.ProtocolViolationException` collides by simple name with `System.Net.ProtocolViolationException`. Three unrelated `ProfileProperty` types exist in different namespaces. `JavaVersions` exposes 72 named versions but `All` has 49 entries, and the aliases return the same object, so a lookup by protocol 47 always reports its representative name as `1.8.9` whichever 1.8.x release the server runs.

## What is not on this list

Movement, chunk decoding, chat signing, authentication and Realms all work and are covered by tests against recorded traffic. The [packages overview](../packages/overview.md) is the place to see what each one does. This page is deliberately the pessimistic view; it is not a summary of the project.

No public-server compatibility claim follows from a successful login or from the local vanilla and proxy tests. In particular, the compatibility investigation did not reproduce Donut SMP, and this change does not claim to fix it. A public-server conclusion needs an authorized account, an explicit action scope and packet-level evidence from that server.
