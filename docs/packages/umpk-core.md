---
title: Umpk.Core
description: The protocol-neutral kernel: identifiers, profiles, geometry primitives, a subscription list, and the hosting seams.
sidebar:
  order: 2
---

`Umpk.Core` is the bottom of the stack. It holds the handful of types that every other package needs and that none of them should define twice: a namespaced identifier, a game profile, a server endpoint, the geometry primitives, and the two hosting seams (a scheduler and a tick source) that let a host drive a session on its own thread and its own clock.

It has no project references. Its only package dependency is `Microsoft.Extensions.Logging.Abstractions`, which is the abstraction, not an implementation.

## Its place in the stack

Everything depends on `Umpk.Core`, directly or through something else. Nothing depends on it in the other direction, and it knows nothing about Minecraft's wire format, NBT or chat. Types here live in the bare `Umpk` namespace (`Umpk.Identifier`, `Umpk.GameProfile`) or in `Umpk.Geometry`, `Umpk.Events` and `Umpk.Hosting`.

## Main entry points

`Identifier` is a namespaced resource key: `minecraft:stone`, `mypack:widget`. It is a readonly struct with `Parse`, `TryParse`, a `Minecraft(path)` shorthand, `Namespace`, `Path` and `IsMinecraft`. The default namespace constant is `Identifier.DefaultNamespace`, which is `"minecraft"`.

`ServerEndpoint` is a host plus a port, with `ServerEndpoint.DefaultJavaPort` at 25565. `Parse` and `TryParse` accept the `host:port` form a user types.

`GameProfile` is a UUID, a name and a list of `ProfileProperty` (name, value, optional signature). `ProfileCredentials` pairs a profile with an access token. `GameVersion` is an edition, a version name and a protocol number; `GameEdition` has `Java` and `Bedrock` members, but only `Java` is implemented anywhere in this repository.

`Umpk.Geometry` holds `Vec3d`, `BlockPos`, `ChunkPos`, `Aabb`, `Direction` and `Axis`. All four of the first are readonly structs. `Aabb` carries vanilla's axis-separated collision helpers (`CollideX`, `CollideY`, `CollideZ` and the generic `Collide(int axis, ...)`), which is what [Umpk.Physics](/packages/umpk-physics) resolves movement with.

`Umpk.Events.SubscriptionList<T>` is a small multicast list with `Subscribe` returning an `IDisposable`, an `Invoke`, and an optional error sink so one throwing handler does not take out the rest.

`Umpk.Hosting` has two seams. `ISessionScheduler` marshals work onto a session's own loop (`ChannelSessionScheduler` is the shipped implementation, and `IsCurrent` tells you whether you are already on it). `ITickSource` yields ticks as an `IAsyncEnumerable<long>`; `PeriodicTimerTickSource` is the real one and `ManualTickSource` is the deterministic one for tests, with `Advance` and `Complete`.

## Example

```csharp
using Umpk;
using Umpk.Geometry;

// Identifiers: parse what a server sent, or build one you already know.
Identifier stone = Identifier.Minecraft("stone");
if (Identifier.TryParse("mypack:widget", out Identifier widget))
{
    Console.WriteLine($"{widget.Namespace} / {widget.Path}");
}

// Endpoints: "host" or "host:port".
ServerEndpoint endpoint = ServerEndpoint.Parse("mc.example.com:25566");

// Geometry: the block a position is inside, and the chunk that block is in.
var eye = new Vec3d(12.7, 64.0, -3.2);
BlockPos block = BlockPos.Containing(eye);
ChunkPos chunk = ChunkPos.Containing(block);

// Player-sized box, then a one-block collider to test against.
Aabb player = Aabb.OfSize(eye.X, eye.Y, eye.Z, width: 0.6, height: 1.8);
Aabb wall = Aabb.BlockAt(block.X + 1, block.Y, block.Z);

// How far the player can actually move east before hitting that block.
double allowedX = wall.CollideX(player, movement: 0.5);
```

## Things that catch people out

`Direction` uses vanilla's numbering, not a tidy one: `Down = 0`, `Up = 1`, `North = 2`, `South = 3`, `West = 4`, `East = 5`. If you serialize the enum's integer value anywhere, that is the order you get, and it is the order the wire expects.

`Vec3d` has `+`, `-`, unary `-` and multiplication by a scalar, but no division operator. Use `Scale(1.0 / n)` or `Multiply`.

`BlockPos` gives you three different points, and they are not interchangeable. `MinCorner` is the block's minimum corner, `Center` is the middle of the cube, and `BottomCenter` is the middle of its bottom face. Feet positions want `BottomCenter`.

`Aabb.Collide` takes the axis as an `int` (0, 1, 2), matching `Axis.X`, `Axis.Y` and `Axis.Z`. So does `Vec3d.Get(int axis)` and `Vec3d.With(int axis, double value)`. The enum is there for readability but the methods take the raw index.

`GameVersion.Protocol` is the wire protocol number, not the Minecraft version string. Several Minecraft versions share one protocol number (all ten 1.8.x releases are protocol 47), which is exactly why `GameVersion` carries both. See [versions and protocols](/concepts/versions-and-protocols).

`ManualTickSource` takes a nominal interval that it reports through `TickInterval` but does not wait for. It only advances when you call `Advance`. That is the point, but it means a test that forgets to call `Complete` will hang on the enumerator rather than finish.
