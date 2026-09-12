---
title: Supported versions
description: Every Minecraft Java Edition version UMPK supports, grouped by the protocol number it maps onto.
sidebar:
  order: 1
---

72 Minecraft version names map onto 49 protocol numbers, 47 through 776. The table below is generated from `data/java/versions.json`, which is the catalog the library and its tooling both read. Every row is one protocol: one dataset directory, one generated descriptor, one set of codecs. The version names on a row are wire-identical to each other.

If you only want the short answer: anything from 1.8 to 26.2 works, and you address it either by release name or by protocol number.

Minecraft Java versions before 1.8 are not supported. Do not use the legacy status ping as evidence of play-protocol support. It only reads a server-list response.

## The catalog

The `identity` column names the block and item id scheme. `legacy` is the pre-flattening world where a block state is `(id, meta)` packed into one number and items carry a damage value. `flat` is 1.13 onward, where every block state has its own id and items are namespaced. That boundary matters more than any other single line in the table, because it changes what a block id even means.

| Protocol | Dataset | Identity | Version names |
| --- | --- | --- | --- |
| 47 | `data/java/47/` | legacy | 1.8, 1.8.1, 1.8.2, 1.8.3, 1.8.4, 1.8.5, 1.8.6, 1.8.7, 1.8.8, 1.8.9 |
| 107 | `data/java/107/` | legacy | 1.9 |
| 108 | `data/java/108/` | legacy | 1.9.1 |
| 109 | `data/java/109/` | legacy | 1.9.2 |
| 110 | `data/java/110/` | legacy | 1.9.3, 1.9.4 |
| 210 | `data/java/210/` | legacy | 1.10, 1.10.1, 1.10.2 |
| 315 | `data/java/315/` | legacy | 1.11 |
| 316 | `data/java/316/` | legacy | 1.11.1, 1.11.2 |
| 335 | `data/java/335/` | legacy | 1.12 |
| 338 | `data/java/338/` | legacy | 1.12.1 |
| 340 | `data/java/340/` | legacy | 1.12.2 |
| 393 | `data/java/393/` | flat | 1.13 |
| 401 | `data/java/401/` | flat | 1.13.1 |
| 404 | `data/java/404/` | flat | 1.13.2 |
| 477 | `data/java/477/` | flat | 1.14 |
| 480 | `data/java/480/` | flat | 1.14.1 |
| 485 | `data/java/485/` | flat | 1.14.2 |
| 490 | `data/java/490/` | flat | 1.14.3 |
| 498 | `data/java/498/` | flat | 1.14.4 |
| 573 | `data/java/573/` | flat | 1.15 |
| 575 | `data/java/575/` | flat | 1.15.1 |
| 578 | `data/java/578/` | flat | 1.15.2 |
| 735 | `data/java/735/` | flat | 1.16 |
| 736 | `data/java/736/` | flat | 1.16.1 |
| 751 | `data/java/751/` | flat | 1.16.2 |
| 753 | `data/java/753/` | flat | 1.16.3 |
| 754 | `data/java/754/` | flat | 1.16.4, 1.16.5 |
| 755 | `data/java/755/` | flat | 1.17 |
| 756 | `data/java/756/` | flat | 1.17.1 |
| 757 | `data/java/757/` | flat | 1.18, 1.18.1 |
| 758 | `data/java/758/` | flat | 1.18.2 |
| 759 | `data/java/759/` | flat | 1.19 |
| 760 | `data/java/760/` | flat | 1.19.1, 1.19.2 |
| 761 | `data/java/761/` | flat | 1.19.3 |
| 762 | `data/java/762/` | flat | 1.19.4 |
| 763 | `data/java/763/` | flat | 1.20, 1.20.1 |
| 764 | `data/java/764/` | flat | 1.20.2 |
| 765 | `data/java/765/` | flat | 1.20.3, 1.20.4 |
| 766 | `data/java/766/` | flat | 1.20.5, 1.20.6 |
| 767 | `data/java/767/` | flat | 1.21, 1.21.1 |
| 768 | `data/java/768/` | flat | 1.21.2, 1.21.3 |
| 769 | `data/java/769/` | flat | 1.21.4 |
| 770 | `data/java/770/` | flat | 1.21.5 |
| 771 | `data/java/771/` | flat | 1.21.6 |
| 772 | `data/java/772/` | flat | 1.21.7, 1.21.8 |
| 773 | `data/java/773/` | flat | 1.21.9, 1.21.10 |
| 774 | `data/java/774/` | flat | 1.21.11 |
| 775 | `data/java/775/` | flat | 26.1 |
| 776 | `data/java/776/` | flat | 26.2 |

## What the numbers mean

A version name is what a player sees in the launcher. A protocol number is what the client puts in the handshake packet. Mojang bumps the protocol number when the wire format changes, and leaves it alone when it does not, so the mapping is many-to-one: 14 of the 49 protocols cover more than one release. Protocol 47 alone covers all ten 1.8.x releases.

UMPK keys off the protocol number everywhere. [Versions and protocols](/concepts/versions-and-protocols) explains why, and what the consequences are when you go looking for a version by name.

Counted the other way: 24 names are pre-flattening (11 protocols, 47 through
340) and 48 are flattened (38 protocols, 393 through 776).

## Looking a version up in code

```csharp
using Umpk.Data.Java;
using Umpk.Protocol.Java;

// By protocol number, straight off a handshake or a status ping.
if (JavaVersions.TryGetByProtocol(47, out JavaVersion v))
{
    Console.WriteLine(v.Version.Name);   // "1.8.9"
}

// By release name. All 72 names in the table resolve, including the aliases:
// "1.20.3" and "1.20.4" both land on protocol 765.
JavaVersions.TryGetByName("1.21.10", out JavaVersion latestish);
```

One wrinkle worth knowing before it surprises you. `JavaVersion.Version.Name` holds a single representative name per protocol, and it is the last release in the band, not the first: protocol 47 reports `1.8.9`, not `1.8`. So round-tripping a name through the catalog is not the identity function. `TryGetByName` accepts every name in the table; the property gives back one of them.

## Where the data comes from

Each protocol directory holds the extracted dataset for that version: packets, blocks, items, entities, registries, menus, argument types, metadata, collision shapes, and the feature flags that drive era-dependent behavior. See [the dataset](/concepts/the-dataset) for what is in those files and how they become compiled C#, and [adding a version](/contributing/adding-a-version) for how a new row gets here.

Recorded packet captures under `fixtures/corpus/` exist for all 49 protocols. The dataset and hand-authored frames still cover behavior that a capture does not exercise. [Limitations](/reference/limitations) lists the remaining gaps.
