---
title: "Umpk.Data.Java"
description: "Generated per-protocol tables for 50 protocols, plus the JavaVersions catalog and the JavaGameData accessors."
sidebar:
  order: 6
---

`Umpk.Data.Java` is where the per-version facts live: packet ids, item and block and entity name tables, collision shapes, metadata key layouts, piston push reactions, and the feature axes that decide era behavior. Almost all of it is generated. Of the 58 `.cs` files in the package, 53 end in `.g.cs`, emitted from `data/java/` by `tools/Umpk.DataGen`.

You rarely use this package for its own sake. You use it to get two things: a `JavaVersion` to build a client with, and a `RegistryAccess` to hand that client so item stacks decode.

## Its place in the stack

`Umpk.Data.Java` depends on [Umpk.Protocol.Java](umpk-protocol-java.md), which is the direction that surprises people. Generated descriptors are built out of protocol types (`ProtocolDescriptor`, `ProtocolFeatures`, `JavaVersion`), so the data package sits above the protocol package rather than below it. `Umpk.Protocol.Java` carries no version tables of its own; it is handed a descriptor.

[Umpk.Client](umpk-client.md) references it directly, which is how a session gets default block shapes without you asking.

## Main entry points

The whole public surface is three types.

`JavaVersions` is the catalog. `JavaVersions.All` lists every supported version, `TryGetByProtocol` looks one up by wire protocol number, and `TryGetByName` looks one up by version string. There are also 72 named properties, from `JavaVersions.V1_8` through `JavaVersions.V26_2`.

`JavaGameData` is the per-protocol accessor. `Registries(protocol)` builds the static `RegistryAccess` (items, blocks, entity types, menu types, and some of the rest). `BlockShapes(protocol)` gives the version's real collision geometry for slabs, stairs, fences, walls, panes and carpets. `BlockPushData(protocol)` gives piston push reactions. `EntityMetadataKeys(protocol)` gives the metadata index layout. `LegacyItemBridgeSource` and the `LegacyItemBridgeEra` constant cover pre-1.13 item NBT and numeric enchantment ids.

`JavaEntityMetadataKeys.TryResolveIndex` resolves one semantic metadata key to its raw index for an entity type. You normally get at it through the `IMetadataKeySource` that `EntityMetadataKeys` returns.

Every accessor caches. Calling `JavaGameData.Registries(770)` in a loop builds the registries once.

## How it is consumed

This is lifted from `samples/MinimalBot/Program.cs`, which is the shortest honest use of the package. Ask the server which protocol it speaks, resolve that to a `JavaVersion`, and hand both the version and its registries to the builder.

```csharp
using Umpk.Client;
using Umpk.Data.Java;
using Umpk.Protocol.Java;

if (!JavaVersions.TryGetByProtocol(protocol, out JavaVersion version))
{
    Console.Error.WriteLine($"Protocol {protocol} is not in the dataset.");
    return 1;
}

await using UmpkClient client = new UmpkClientBuilder()
    .UseVersion(version)
    .UseProfile(profile)
    // Without this the first packet carrying a non-air item fails to decode
    // and takes the session down with it.
    .UseStaticRegistries(JavaGameData.Registries(version.Version.Protocol))
    .Build();
```

To use the tables outside a session, for example to feed the physics engine real block geometry:

```csharp
using Umpk.Data.Java;
using Umpk.Game.Registries;

IBlockShapeSource shapes = JavaGameData.BlockShapes(protocol: 770);
IBlockPushSource push = JavaGameData.BlockPushData(protocol: 770);
```

## Things that catch people out

There are 73 version properties but only 50 protocols, and `JavaVersions.All` has 50 entries. `JavaVersions.V1_8_9` and `JavaVersions.V1_8` return the same object, because all ten 1.8.x releases are protocol 47. `TryGetByProtocol` returns the first version in `All` with that protocol number, so `TryGetByProtocol(47, out var v)` gives you a version whose `Version.Name` is `"1.8.9"`, the representative release stored by that descriptor. If you need to report the exact version a server named, keep the server's own string.

`JavaGameData.Registries` does not populate everything. `Attributes` is empty on purpose. `Biomes` and `DimensionTypes` are empty because those are dynamic registries that arrive during the configuration phase at runtime. `Enchantments` is empty on protocols 47 through 404 (no vanilla registries report existed yet) and on 767 and later (vanilla moved enchantments onto the config-phase sync). An empty registry means "cannot be resolved statically", never "no such thing exists".

`BlockPushData` can return a source whose `HasData` is false, for protocols the dataset carries no measurement for. Read that as "unknown", not as "every block is normal".

Every `.g.cs` file is regenerated from `data/java/`, so a hand edit is overwritten the next time anyone runs the generator. Change a table through the dataset instead.

The generator overwrites every generated output. Start with no uncommitted generated-file changes. To change a table:

1. Edit the file under `data/java/`.
2. Run `dotnet run --project tools/Umpk.DataGen -- verify --data data/java`. The command must print `verify: OK (50 protocols)`.
3. Run the three-output generation and Git comparison in [the dataset](../concepts/the-dataset.md).
4. Review every generated-file difference that Git reports.
5. Run `dotnet build UMPK.sln`. The build must report 0 warnings and 0 errors.

The dataset is also how era behavior is selected. Feature axes in `data/java/<protocol>/features.json` thread through the generator into `ProtocolFeatures` and the physics profile factory. Do not write `if (protocol >= N)` in engine code. Add a feature axis, or read the one that already exists. See [era gating](../concepts/era-gating.md) and [the dataset](../concepts/the-dataset.md).
