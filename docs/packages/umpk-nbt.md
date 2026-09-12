---
title: "Umpk.Nbt"
description: "NBT tags, the three Java root framings, SNBT parsing and printing, and a byte and depth accounter."
sidebar:
  order: 3
---

`Umpk.Nbt` reads and writes Minecraft's Named Binary Tag format. It has the full tag model, modified UTF-8 string handling, SNBT (the text form you see in commands), and an accounter that caps how many bytes and how much nesting a hostile server can make you allocate.

It is usable entirely on its own. If all you want is to read a `level.dat` or round-trip a tag, this package is the whole answer and you never touch a socket.

## Its place in the stack

`Umpk.Nbt` depends only on [Umpk.Core](umpk-core.md). Above it, [Umpk.Text](umpk-text.md) uses it for the NBT form of chat components, [Umpk.Game](umpk-game.md) uses it for item component payloads and block entity data, and [Umpk.Protocol.Java](umpk-protocol-java.md) reads and writes tags straight off the wire.

## Main entry points

`NbtTag` is the abstract base. The concrete tags are `NbtByte`, `NbtShort`, `NbtInt`, `NbtLong`, `NbtFloat`, `NbtDouble`, `NbtString`, `NbtByteArray`, `NbtIntArray`, `NbtLongArray`, `NbtList`, `NbtCompound` and `NbtEnd`. The six numeric tags share an `NbtNumeric` base that exposes `AsInt`, `AsLong`, `AsFloat`, `AsDouble`, `AsShort` and `AsSByte`, so you can read a value without caring which width the server used.

`NbtCompound` is the workhorse. It has typed accessors (`GetInt`, `GetString`, `GetCompound`, `GetList`, and the rest), typed writers (`PutInt`, `PutString`, `Put`), `TryGet` in both an untyped and a generic form, `ContainsKey`, `Remove`, and a `Keys` list.

`NbtReader.Read` and `NbtWriter.Write` are the wire boundary. Both take an `NbtWireFormat`. One `NbtReader.Read` overload reports `out int bytesRead`, which is what you want when a tag is embedded in a larger frame.

`NbtAccounter` bounds a decode. `NbtAccounter.CreateDefault()` gives you the vanilla network quota (`NbtAccounter.DefaultNetworkQuota`, 2 MiB) and a depth cap of `NbtAccounter.DefaultMaxDepth` (512). `NbtAccounter.Create(quota)` sets your own, and `NbtAccounter.Unlimited()` removes the cap. Overruns raise `NbtSizeLimitException` and `NbtDepthLimitException`; malformed bytes raise `NbtFormatException`.

`Umpk.Nbt.Snbt.SnbtParser` and `SnbtPrinter` convert to and from the text form. `ParseCompound` expects a compound, `ParseValue` accepts any tag.

## Example

```csharp
using Umpk.Nbt;
using Umpk.Nbt.Snbt;

var tag = new NbtCompound();
tag.PutString("id", "minecraft:chest");
tag.PutInt("x", 12);
tag.PutBool("Lock", true);

var items = new NbtList();
var slot = new NbtCompound();
slot.PutByte("Slot", 0);
slot.PutString("id", "minecraft:diamond");
items.Add(slot);
tag.Put("Items", items);

// Disk format and pre-1.20.2 network format: type byte, root name, body.
byte[] onDisk = NbtWriter.ToArray(tag, NbtWireFormat.JavaNamedRoot);

// 1.20.2+ network format: type byte, body, no root name.
byte[] onWire = NbtWriter.ToArray(tag, NbtWireFormat.JavaUnnamedRoot);

// Read it back under an explicit quota rather than trusting the sender.
NbtTag decoded = NbtReader.Read(onWire, NbtWireFormat.JavaUnnamedRoot, NbtAccounter.CreateDefault());

var back = (NbtCompound)decoded;
Console.WriteLine(back.GetString("id"));           // minecraft:chest
Console.WriteLine(back.GetList("Items")?.Count);   // 1
Console.WriteLine(SnbtPrinter.Print(back));        // {id:"minecraft:chest",x:12,...}
```

## Things that catch people out

The three wire formats are not interchangeable, and picking the wrong one produces bytes that decode into garbage rather than throwing. `JavaNamedRoot` is the disk format for every version and the network format before 1.20.2: type byte, a modified-UTF-8 root name (usually empty), then the body. `JavaUnnamedRoot` is the network format from 1.20.2 on: type byte, then body, no name. A bare `End` byte under that format decodes to `NbtEnd.Instance`, which is the null-NBT marker. `JavaRootTagOrString` has the same framing as `JavaUnnamedRoot`, but the root may be any type rather than a compound, which is what 1.20.3+ text components need because a component can be a plain string.

`NbtByteArray` holds `sbyte[]`, not `byte[]`. Minecraft's byte tag is signed, and the model keeps it signed rather than quietly reinterpreting. `NbtByte.Value` is an `sbyte` too, with `AsBool` and an `NbtByte(bool)` constructor for the many tags that are really booleans.

`NbtCompound.Keys` preserves insertion order. That is deliberate and there is a test suite pinning it (`OrderPreservationTests`), because byte-identical re-encoding is a requirement here, not a nicety. Do not rely on alphabetical ordering.

The typed getters do not throw when a key is missing. `GetInt` on an absent key returns `0`, `GetString` returns the empty string, and `GetCompound` and `GetList` return `null`. Use `ContainsKey` or `TryGet` when the difference between absent and zero matters.

`NbtAccounter` is stateful. One accounter tracks the usage of one decode. Do not share an instance across decodes unless you mean to make the budget cumulative.

Strings use Java's modified UTF-8, not standard UTF-8. The two differ on the NUL character and on characters outside the basic multilingual plane. The package handles this for you, but it is why you cannot round-trip an NBT string through `Encoding.UTF8` and get the same bytes back.
