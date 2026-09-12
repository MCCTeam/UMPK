---
title: Versions and protocols
description: Why UMPK keys everything off the protocol number instead of the release name, and what the catalog in data/java/versions.json actually holds.
sidebar:
  order: 1
---

Minecraft has two version identities and people mix them up constantly. There is the name on the launcher button, 1.20.4, and there is the number the client puts in its handshake packet, 765. They are not the same kind of thing, and the difference decides how a protocol library is organised.

The name is a marketing and release artifact. Mojang ships a version, names it, and moves on. The protocol number is a compatibility claim: it changes when the wire format changes, and it stays put when the release only touched things that never cross the socket. 1.20.3 and 1.20.4 both announce 765 because nothing on the wire moved between them. A server on either one will accept a client on the other, and it has no way to tell them apart from the handshake alone.

So the number is the real unit. Everything in UMPK is keyed on it.

## The catalog

`data/java/versions.json` is the ordered list of every release UMPK knows about. Each entry is small:

```json
{
  "name": "1.20.4",
  "protocol": 765,
  "dataset": "765",
  "identity": "flat"
}
```

72 entries, 49 distinct protocol numbers, running from 47 (1.8) to 776 (26.2). The file's own provenance block says it plainly: "ordered catalog; names map N:1 onto protocols". The `dataset` field names the directory under `data/java/` that holds the extracted data for the protocol, and there are 49 of those directories, one per number. The `identity` field records which block and item id scheme the version uses, `legacy` before the 1.13 flattening and `flat` after it.

There is no directory named `1.20.4`. There is no per-release anything. If you go looking for a file that belongs to a release rather than a protocol, you will not find one, and that is the design working rather than something missing.

The full mapping is in [supported versions](/reference/supported-versions).

## What this buys

The alternative most protocol libraries land on is a class or a module per release, and it collapses as soon as you count the releases. 72 names is 72 forks to keep in sync, when 23 of them are byte-for-byte identical to a neighbour on every packet they send.

Keying on the number does the deduplication for you at the level where it is actually true. One protocol means one dataset directory, one generated descriptor, one packet table, one set of codec bindings. 1.21.7 and 1.21.8 are both protocol 772, so supporting the second once you have the first is a catalog entry and a regeneration, with no new data and no new codecs.

You can see the collapse in the generated code. `Umpk.Data.Java` emits a property per release name, but the properties share a backing field:

```csharp
/// <summary>1.8 (protocol 47).</summary>
public static JavaVersion V1_8 => s_v47 ??= global::Umpk.Data.Java.V47.Descriptor.Build();
private static JavaVersion? s_v47;

/// <summary>1.8.1 (protocol 47).</summary>
public static JavaVersion V1_8_1 => s_v47 ??= global::Umpk.Data.Java.V47.Descriptor.Build();
```

Ten 1.8.x properties, one descriptor, one object. `JavaVersions.All` has 49 entries, not 72, because there are only 49 different things to be.

## Looking one up

Two entry points, both on the generated catalog:

```csharp
JavaVersions.TryGetByProtocol(765, out JavaVersion byNumber);
JavaVersions.TryGetByName("1.20.3", out JavaVersion byName);
```

`TryGetByName` resolves all 72 names through a generated name table, aliases included. It exists because name lookup is what a config file or a command-line flag hands you, and the alternative is making every caller learn the mapping.

Here is the wrinkle that catches people. `JavaVersion.Version.Name` is a single representative name for the protocol, and it is the last release in the band. Protocol 47 reports `1.8.9`. Protocol 765 reports `1.20.4`. Feed it `1.20.3` and you will get an object back that calls itself `1.20.4`, which is correct and still surprising the first time. If you need to echo the user's version string back at them, keep their string; do not round-trip it through the catalog.

## Where the number comes from at runtime

In practice you rarely type a protocol number. A status ping reports the server's, and the handshake carries whatever you decide to send. See [your first status ping](/getting-started/status-ping) for the ping path and [installation](/getting-started/installation) for wiring a version into a client.

The number then selects a `ProtocolDescriptor`, which is the packet table plus the codec bindings for that wire format, and a `ProtocolFeatures` record, which is the feature flags that drive behavior the packet table cannot express. Those two objects are the whole of what "this is version X" means inside the library. The [packet pipeline](/concepts/packet-pipeline) covers the first, and [era gating](/concepts/era-gating) covers the second.
