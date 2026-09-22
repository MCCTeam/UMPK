---
title: "Package overview"
description: "The fourteen shipping UMPK packages, what each one owns, and which way the dependencies point."
sidebar:
  order: 1
---

UMPK is split into fourteen NuGet packages. Thirteen of them contain code; the fourteenth, `Umpk`, is a meta package that references a useful subset of the others so you can add one thing and get a working client. The split is not cosmetic. `Umpk.Nbt` and `Umpk.Text` have no idea a network exists, and you can use them on their own to read a region file or render a chat component without ever opening a socket.

## The packages

| Package | What it owns |
| --- | --- |
| [Umpk.Core](umpk-core.md) | Identity, geometry, the event helper, hosting seams. No dependencies at all. |
| [Umpk.Nbt](umpk-nbt.md) | NBT tags, the three Java wire framings, SNBT, allocation accounting. |
| [Umpk.Text](umpk-text.md) | Chat components, styles, click and hover events, JSON and NBT serializers. |
| [Umpk.Game](umpk-game.md) | The game model: blocks, entities, items and components, containers, registries, world storage. |
| [Umpk.Data.Java](umpk-data-java.md) | Generated per-protocol tables for 50 protocols, plus `JavaVersions` and `JavaGameData`. |
| [Umpk.Data.Lang](umpk-data-lang.md) | Generated per-protocol vanilla `en_us` translations with hand-written lookup and formatting support. |
| [Umpk.Protocol.Java](umpk-protocol-java.md) | Codecs, packet registration, framing, login and encryption, chat signing, status pings. |
| [Umpk.Client](umpk-client.md) | The session runtime: connect, apply packets to state, raise events, send actions. |
| [Umpk.Physics](umpk-physics.md) | Tick-accurate player movement and collision with per-era profiles. |
| [Umpk.Pathfinding](umpk-pathfinding.md) | An A* planner over a captured world region, plus per-move execution templates. |
| [Umpk.Commands](umpk-commands.md) | A Brigadier-backed command tree with scoped registration and completions. |
| [Umpk.Auth](umpk-auth.md) | Microsoft device code and browser flows, Yggdrasil, offline identity, a token store. |
| [Umpk.Realms](umpk-realms.md) | The Realms HTTP API: list worlds, join one, check compatibility. |
| `Umpk` | Meta package. References `Umpk.Client`, `Umpk.Data.Java`, `Umpk.Data.Lang`, `Umpk.Auth`, `Umpk.Physics` and `Umpk.Pathfinding`. It contains no code of its own. |

Worth noticing about the meta package: it does not reference `Umpk.Realms`, and it only picks up `Umpk.Commands`, `Umpk.Game`, `Umpk.Text`, `Umpk.Nbt`, `Umpk.Core` and `Umpk.Protocol.Java` transitively through `Umpk.Client`. If you want Realms, reference `Umpk.Realms` explicitly.

## Which way the arrows point

`Umpk.Core` sits at the bottom and depends on nothing but `Microsoft.Extensions.Logging.Abstractions`. Everything else is layered on top of it.

```text
Umpk.Core
  ├── Umpk.Nbt
  ├── Umpk.Text          (Core, Nbt)
  │     └── Umpk.Data.Lang (Text)
  ├── Umpk.Game          (Core, Nbt, Text)
  ├── Umpk.Protocol.Java (Core, Nbt, Text, Game)
  │     └── Umpk.Data.Java   (Protocol.Java)
  ├── Umpk.Physics       (Core, Game)
  │     └── Umpk.Pathfinding (Core, Game, Physics)
  ├── Umpk.Commands      (Core, Text)
  └── Umpk.Auth          (Core, Protocol.Java)
        └── Umpk.Realms      (Auth)

Umpk.Client (Core, Game, Protocol.Java, Commands, Text, Physics, Pathfinding, Data.Java)
```

The one edge that surprises people: `Umpk.Data.Java` depends on `Umpk.Protocol.Java`, not the other way round. Generated per-version descriptors are built out of protocol types, so the data package sits above the protocol package. `Umpk.Protocol.Java` on its own has no version tables; it gets them handed to it.

## Reserved but empty

`Umpk.Server` and `Umpk.Proxy` exist as projects in the solution and contain zero `.cs` files. Their test projects, `tests/Umpk.Server.Tests` and `tests/Umpk.Proxy.Tests`, are likewise empty. They are reserved names, not shipped code. There is no server-side connection layer and no proxy. Do not plan around them.

Bedrock Edition is not supported and not started.

## The API is not frozen

Every package's `PublicAPI.Shipped.txt` is empty. Everything public lives in `PublicAPI.Unshipped.txt`, which is the analyzer's way of saying nothing has been committed to yet. Names, signatures and whole types can change without a major version bump until that changes. If you build on UMPK today, pin a version.

Those `PublicAPI.Unshipped.txt` files are also the most reliable index of what actually exists. When these docs and the source disagree, the source wins; when you want the complete list rather than the curated one, read the file for the package.

## Common properties

Every shipping package targets `net10.0`, enables nullable reference types, builds with `TreatWarningsAsErrors`, and is marked `IsAotCompatible`. That last one is why you will not find reflection-based activation or assembly scanning anywhere in `src/`: the design always has a generated table or an explicit registration instead.

Where to go next: [installation](../getting-started/installation.md), then the [status ping](../getting-started/status-ping.md) and [minimal bot](../getting-started/minimal-bot.md) walkthroughs. For the ideas that run through all of these packages, read [versions and protocols](../concepts/versions-and-protocols.md) and [vanilla as the oracle](../concepts/vanilla-as-the-oracle.md).
