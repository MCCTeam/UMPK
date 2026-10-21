# Native hard entity collision classification

This audit supplies the native participation rules needed by source-aware movement collision. A block-placement obstruction flag cannot answer this query. Entity picking and soft pushes also use separate predicates.

The source audit verifies each original client jar against its artifact receipt and decompile completion receipt. It resolves every registered entity class through its original inheritance chain and checks the resulting registry identifiers against the exact canonical dataset. Raw source and native artifacts remain outside Git. `legacy-source-audit.json` and `modern-source-audit.json` retain the class chains, method owners, original method bodies, line numbers and source/dataset/jar hashes. These files are original-source evidence, with the separately executed protocol-107 witness described below.

## Protocol 107

The complete top-level entity scan identifies 92 subclasses of native `rr`. All 65 canonical mob entries and 25 canonical object entries resolve to native class owners. Native `rt` also registers abstract `Mob` and `Monster` classes at IDs 48 and 49; the audit verifies that both classes are abstract and records their exclusion from the canonical dataset.

| Role | Native rule |
| --- | --- |
| Target boat, `aag.af` | Supplies its current bounding box. |
| Target Shulker, `yu.af` | Supplies its current bounding box only when `au()` is true; inherited living `sa.au` requires a clear dead flag and positive health. |
| Target minecart, `aah.af` | Returns null. |
| Every other registered target family | Inherits `rr.af`, which returns null. |
| Falling block or ordinary source, `rr.j(peer)` | Returns null. |
| Boat source, `aag.j(peer)` | Supplies the peer's current bounding box. |
| Minecart source, `aah.j(peer)` | Supplies the peer's current bounding box only when peer `aq()` is true. |

The exhaustive override lists are exactly `rr`, `aag`, `aah` and `yu` for `af`, and `rr`, `aag` and `aah` for `j`. Of the 90 canonical wire rows, 79 inherit `rr.af`, eight cart rows inherit the null minecart override, two boat rows supply a box, and one Shulker row supplies a conditional box. Mob/object aliases remain separate wire rows rather than inflated counts of distinct native families.

`aht.a(source, query)` collects blocks first. With a non-null source, it queries native entity chunk lists using a box inflated by 0.25, excludes the source by identity and uses `rv.e`, which excludes spectator players. It then excludes peers sharing the source's root vehicle and independently tests `peer.af()` and `source.j(peer)` against the original query. The native chunk traversal also considers multipart children. A null source skips this entity phase entirely. This legacy source-null query therefore has different semantics from the modern source-null entity query.

Neither the candidate predicate nor these generic query steps impose a blanket dead/no-physics exclusion. Native membership and the actor-specific methods decide participation. In particular, a dead-flag boat still present in the native chunk list continues supplying its box before removal traversal. Native `rr.d` movement bypasses collision when the source's no-physics flag is true; explicitly calling the query with that source still returns its native collision answer.

## Modern protocols 769–777

The audit covers all 13 supported named releases in this band, including aliases. It resolves 2,005 registry rows across those releases without missing or extra canonical identifiers.

| Release | Protocol | Registry rows | Boat targets | Alive Shulker | Contextual Happy Ghast | Inherited false targets |
| --- | --- | --- | --- | --- | --- | --- |
| 1.21.4 | 769 | 149 | 20 | 1 | 0 | 128 |
| 1.21.5 | 770 | 150 | 20 | 1 | 0 | 129 |
| 1.21.6 | 771 | 151 | 20 | 1 | 1 | 129 |
| 1.21.7 and 1.21.8, each | 772 | 151 | 20 | 1 | 1 | 129 |
| 1.21.9 and 1.21.10, each | 773 | 153 | 20 | 1 | 1 | 131 |
| 1.21.11 | 774 | 157 | 20 | 1 | 1 | 135 |
| 26.1, 26.1.1 and 26.1.2, each | 775 | 157 | 20 | 1 | 1 | 135 |
| 26.2 | 776 | 158 | 20 | 1 | 1 | 136 |
| 26.3 | 777 | 161 | 22 | 1 | 1 | 137 |

All seven minecart families inherit the target `Entity.canBeCollidedWith` false result. Boats include rafts and chest variants; 26.3 adds the two poplar boat variants. This classification says nothing about their placement obstruction or pickability.

`EntityGetter.getEntityCollisions` returns an empty list only when the query's mean box size is strictly below `1.0E-7`, or when the actual candidate query is empty. Native `AABB.getSize` is `(xSize + ySize + zSize) / 3`, so a general empty-box or maximum-dimension check cannot replace it. Candidates use the query inflated by `1.0E-7`. A null source selects `NO_SPECTATORS` combined with target `canBeCollidedWith`; a non-null source combines `NO_SPECTATORS` with the source's `canCollideWith`. Returned shapes use each eligible target's current bounding box.

| Source or target rule | Original behavior |
| --- | --- |
| Ordinary source | Requires target `canBeCollidedWith` and a different root vehicle. |
| Boat or minecart source | Requires target `canBeCollidedWith` or `isPushable`, and a different root vehicle. |
| Wind-charge source | Rejects another wind charge, then delegates to the ordinary source rule. |
| Boat target | Returns true. |
| Shulker target | Returns `isAlive()`. |
| Other target before protocol 771 | Returns false. |
| Happy Ghast target from protocol 771 | Uses the source-dependent rule below. |

Protocol 771 changes target `canBeCollidedWith()` to `canBeCollidedWith(source)`. An adult, alive Happy Ghast accepts a client player whose position Y is at or above the Ghast's box maximum Y; it also accepts another Happy Ghast while the target is a vehicle. Otherwise it uses `isOnStillTimeout()`. That method reads synchronized `STAYS_STILL` or a positive server timeout. A null source remains meaningful and cannot be replaced with a synthetic player.

Pushability remains a separate dependency for boat/cart sources. The native method owners are finite: base Entity returns false; boats, carts and Parrot return true; ArmorStand and Bat return false; AbstractHorse checks that it is not a vehicle; LivingEntity requires alive, non-spectator and not on a climbable block; Creaking adds its movement predicate; Warden excludes digging/emerging before the living predicate. Missing pose, health, mount, climbable-world or metadata context must remain unknown when it can change the answer. The machine audit records every registered family's inherited owner, including new releases and aliases.

## Executed original protocol-107 witness

Fifteen scenarios execute and pass using original `af`, `j`, `aq`, `au`, `rv.e` and `aht.a` methods. Each uses owned native chunks established through original chunk packet encoding, decoding and listener dispatch. Peer insertion calls the original native chunk-list method. It does not claim ClientWorld entity publication, actor/world ticks, movement trajectories or modern native execution.

| Scenario | Returned collision boxes |
| --- | --- |
| Falling source, falling or minecart peer | 0 |
| Falling source, boat or live Shulker peer | 1 |
| Falling source, zero-health or dead-flag Shulker | 0 |
| Falling source, dead-flag boat still in the native list | 1 |
| Falling source, no-physics boat | 1 |
| Explicit query from no-physics falling source, boat peer | 1 |
| Falling source sharing the boat's root vehicle | 0 |
| Boat source, falling peer | 1 |
| Minecart source, falling peer | 0 |
| Minecart source, cow peer | 1 |
| Boat source, player peer | 1 |
| Boat source, spectator player peer | 0 |

The original unsigned client jar's 2,905 class entries compare byte for byte with the verified original client jar. Runtime origin assertions cover the actual native entity, predicate, world, chunk and box classes. The owned Player subclass supplies spectator/creative truth inputs and suppresses sound; the original base collision methods and candidate predicate execute unchanged. The retained world adapter suppresses ambient block work and avoids graphical Minecraft/local-player construction. Command receipts identify every classpath entry and helper hash.

The first two native attempts fail at ClientWorld minecart insertion because the graphical sound manager is absent. The valid attempt narrows insertion to the original chunk-list operation and states that substitution explicitly. Both failures, their source versions, compiler logs, raw native logs and packet captures remain separate. Native Netty reflective-access warnings remain in the raw logs.

This work unblocks a bounded source-aware hard-collision profile and the protocol-107 falling-block caller. It does not install a production helper or close modern motion acceptance. Unknown eligible bounds, native chunk membership, Shulker lifetime, Happy Ghast state, shared vehicles or source-specific pushability must not become an empty collider list. Runtime/live checks against the final producer remain required when an owner consumes these rules.

The retained audit bundle is `runs/hard-entity-collision-20261021-v1` under `/home/anon/Projects/MinecraftOfficial`. Its 35-file manifest has SHA-256 `6bd5c3031e431e69474fe094f62192bddb9674c22436ffe58f062c841def9d77`. The bounded protocol-107 caller is documented in [falling-block runtime](falling-block-runtime.md); modern source-aware runtime acceptance remains open.
