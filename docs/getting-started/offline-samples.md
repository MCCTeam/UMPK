---
title: Offline samples
description: Five small programs that run with no server, each showing one layer of UMPK in isolation.
sidebar:
  order: 4
---

You do not need a server to learn UMPK. Five samples run offline, each in under a second, each showing one layer on its own. Run them first, then read the connected samples once the offline shapes feel familiar.

```bash
dotnet run --project samples/ChatRender
dotnet run --project samples/NbtLab
dotnet run --project samples/PhysicsWalk
dotnet run --project samples/VersionTable
dotnet run --project samples/AuthOffline -- Steve
```

The connected samples need a server you control:

```bash
dotnet run --project samples/StatusPing -- mc.example.com
dotnet run --project samples/MinimalBot -- localhost Steve
dotnet run --project samples/ConnectedBot -- localhost Steve
```

Auth on its own needs no game server, but the two online flows need their auth server to be reachable:

```bash
dotnet run --project samples/AuthOnline -- player@example.com
dotnet run --project samples/AuthThirdParty -- https://example.com/api/yggdrasil/
```

## ChatRender: one tree, several wire shapes

` samples/ChatRender/Program.cs` builds a styled "Welcome, Steve" message and prints it four ways: plain text, modern JSON, legacy JSON, and section sign text. It also shows translation with and without a table.

The idea to take away is that a component tree has no version in it. Only the serializers know that click and hover events changed shape at 1.21.5. You pick the era when you serialize:

```csharp
string modern = ComponentJson.ToJsonString(message, ComponentWireEra.Modern);
string legacy = ComponentJson.ToJsonString(message, ComponentWireEra.Legacy);
```

Pass the wrong era and a server will reject or misread the JSON. The default is Modern, so old servers need the explicit argument. A server MOTD arrives in two shapes, a bare string on old servers and a tree on new ones, and `ComponentJson.Parse` reads both, so you never branch on that. `ToPlainText` without a table falls back to the key or the fallback string, which suits logs. Pass `VanillaTranslations.ForProtocol(protocol)` when a human will read the output.

Start here if chat text confuses you. It is the shortest path to seeing how styles, click events, and translations fit together.

## NbtLab: tags without a world

`samples/NbtLab/Program.cs` builds a small chest tag, writes it in both framings, reads it back under a quota, and round trips through SNBT.

The point to remember is the framing. `JavaNamedRoot` is disk and pre 1.20.2 network: type byte, root name, body. `JavaUnnamedRoot` is network from 1.20.2: type byte, body, no name. Same tree, different bytes, and the wrong choice decodes into garbage instead of throwing:

```csharp
byte[] onWire = NbtWriter.ToArray(tag, NbtWireFormat.JavaUnnamedRoot);
NbtTag decoded = NbtReader.Read(onWire, NbtWireFormat.JavaUnnamedRoot, NbtAccounter.CreateDefault());
```

Always decode under an accounter when the bytes came from elsewhere. `CreateDefault` gives you the vanilla network budget. Typed getters stay lenient: a missing int reads as 0, a missing string as empty, a missing compound or list as null. Use `ContainsKey` or `TryGet` when absent and zero mean different things.

Start here if items, block entities, or chunk data feel opaque. NBT sits under all three.

## PhysicsWalk: movement with no server

`samples/PhysicsWalk/Program.cs` builds a flat stone floor in memory, drops a player onto it, walks forward for 60 ticks, then jumps once from a fresh start.

The engine is tick based at 20 ticks per second. One `Step` call is one tick. There is no solver, only simulation, so the sample settles first:

```csharp
var engine = new PlayerPhysics(world, PhysicsProfile.Modern);
engine.SetConditions(PhysicsConditions.Default);
engine.Reset(new Vec3d(0.5, 65.0, 0.5), yaw: 0f, pitch: 0f);
engine.Step(MovementInput.None);
engine.Step(MovementInput.None);
```

Reading `OnGround` right after `Reset` tells you nothing. Those two quiet ticks give the engine ground and fluid state before measurement starts. The world comes through `IPhysicsWorldView`, four members: `GetBlock`, `GetCollisionShapes`, `IsChunkLoaded`, and `CollectEntityColliders`. Shapes use local block space, 0 to 1. Mixing that with world space is the easy mistake here.

For the era correct profile in real code, map the dataset flags through `PhysicsProfile.FromFeatures` instead of using `Modern` or `ForProtocol`. The client builds it that way internally. See [movement and pathfinding](/guides/movement-and-pathfinding) for the full story.

Start here if you want to simulate a jump before you commit to it, or if movement numbers look like magic.

## VersionTable: the catalog

`samples/VersionTable/Program.cs` lists all 49 protocols, then shows the two lookups a bot uses at startup.

A client is built for one protocol, so a bot pings first, reads `version.protocol`, then builds. `MinimalBot` follows exactly this flow. The catalog calls are:

```csharp
JavaVersions.TryGetByProtocol(protocol, out JavaVersion byNumber);
JavaVersions.TryGetByName("1.21.8", out JavaVersion byName);
```

`All` holds 49 entries, one per protocol, while the named properties number 72, since several releases share one protocol. All ten 1.8.x releases share 47. `Version.Name` reports one representative name per protocol, the last release in the band, so 47 reports "1.8.9". `TryGetByName` accepts every alias.

Start here if you need to pin a version, list what is supported, or load registries for a protocol without connecting.

## AuthOffline: identity without login

`samples/AuthOffline/Program.cs` derives the offline UUID for a name and prints the profile a bot would join with.

Offline mode is the absence of an authenticator. You hand the profile to the builder and never call `UseAuthenticator`:

```csharp
await using UmpkClient client = new UmpkClientBuilder()
    .UseVersion(version)
    .UseProfile(OfflineIdentity.ComputeProfile(username))
    .UseStaticRegistries(JavaGameData.Registries(version.Version.Protocol))
    .Build();
```

The UUID is MD5 of `"OfflinePlayer:<name>"` with version and variant bits set to match Java. Same name gives the same UUID every time, which is what keeps player data stable across reconnects. Case matters, since the hash covers the exact string.

Online login is a separate flow through `MinecraftAuthFlow`, covered in [authentication](/guides/authentication). Never pair an offline profile with `UseAuthenticator`. The two modes exclude each other.

Start here if you want to join an offline test server, or if you need stable test identities.

## What to read next

Once the offline shapes feel familiar, read [your first status ping](/getting-started/status-ping) and [a minimal bot](/getting-started/minimal-bot). Those two use the same pieces against a live server: ping to pick the protocol, registries to decode, components to render, and the client to join.
