# Adventure predicates in protocols 774–777

## Behavior and ownership

UMPK now reads the native `can_break` and `can_place_on` forms for 1.21.11/774, 26.1/775, 26.2/776 and 26.3/777 through the existing component table and Adventure codec. The existing mining permission/cache, held-target identity and placement callers consume those values. Native missing permission denies Adventure mining; missing tags, unsupported property kinds and incomplete block-entity context remain unknown. The public block raycast and action signatures stay unchanged.

The internal codec profiles are Plain14 for the already measured 770–773 era, Either14 for 774 and Either15 for 775–777. Later partial predicates encode a Boolean discriminator before the registry ID: true selects a concrete predicate, while false selects a declared component-presence predicate. The fifteenth concrete entry, `minecraft:villager/variant`, starts at 775. Only the existing selected Damage concrete CODEC and the new presence branch have an owned DATA form; recognizing another valid concrete ID does not claim its value model is implemented.

Presence predicates use fresh native identity keys, so two entries with the same component identifier can coexist. The codec preserves their received NBT maps, order and duplicates. A fresh decoded presence predicate changes held-mining identity, while measured equal exact and concrete values retain it. Repeated concrete types remain invalid. The public model's existing comment about distinct native partial types remains accurate; repeated component identifiers refer to distinct native presence-type instances.

Native Adventure DATA rejects an empty predicate list even though its stream form admits it. This known invalid value now raises `ArgumentException` before sending a click frame. Truly unavailable DATA still uses `ComponentHashUnavailableException` and the existing containing-component omission policy. Local inventory prediction remains applied before hashing; the change adds no rollback. Generic hash-map behavior is unchanged.

## Original-method observations

The native campaign ran four serial original-method JVMs against the retained original 1.21.11, 26.1, 26.2 and 26.3 client artifacts. It reused the genuine component registry bootstrap and original stream/DATA/hash methods, including `TypedDataComponent.encodeValue`, `DataResult.getOrThrow`, `HashCode.asInt`, item-stack identity and the complete set-slot codec. No original actor or codec method was replaced. These are original-method measurements; the campaign did not run native input, a native network session or a server.

All 80 rows completed: 72 hashes and eight DATA errors. Original `getOrThrow` raises `IllegalArgumentException` for each empty-list DATA error. Forty-eight fresh decoded stacks compare equal and 32 presence-bearing stacks compare unequal. Presence DATA uses an empty map, including a repeated-presence-key case. The measured hashes agree across all four profiles and both Adventure components.

| Predicate | Native hash |
| --- | ---: |
| Simple block predicate | -1161294463 |
| Empty concrete Damage or presence of Damage | -6628392 |
| Concrete Damage range | -520394857 |
| Exact Damage | -1843603740 |
| Recursive exact component | -1601370338 |
| Presence of Rarity | -1820373396 |
| Duplicate presence or mixed concrete/presence | -686728813 |

Seventy-nine of 80 original set-slot streams re-encode byte identically. The 776 mixed `can_place_on` row changes native map order. Five component-only re-encodings also change map order: both mixed rows at 775, mixed `can_place_on` at 776 and both mixed rows at 777. These original outputs are retained, and UMPK round trips the captured emitted vector without normalizing raw order or duplicates. The tests do not invent a universal native canonical byte order.

The source bindings record the Identifier class change at 774 and the predicate-package change at 776. Eleven relevant original classes are byte identical across 26.1, 26.1.1 and 26.1.2; this supports reuse of their listed predicate behavior and does not replace the required named-release live pack. Native `BlockPredicate.matches(BlockInWorld)` ignores component matchers, while its separate world-position overload evaluates them. Mining permission follows the caller's actual overload.

The accepted native index is `.scratch/adventure774-777-native80-preparation-20261103-v3/ACTUAL-NATIVE80.json`, SHA256 `00de1be439a285df2732efaaa1e633970a3f40f56f2c3c3b3e95b439a8be26d3`. Alias comparison is `.scratch/adventure774-777-runtime-preparation-20261103-v1/ALIASES775-SOURCE.json`, SHA256 `4fcf3c8ee56a54036adf772d92d0b7a207d43aaa13026ae707928caf324765a3`. Raw evidence remains under `/home/anon/Projects/MinecraftOfficial/runs/adventure774-777-native80-20261103-v3`.

## Packet-driven runtime acceptance

The managed runs start from an immutable archive of `130e9730615330de97493011df84ce4c425dea07`, normal SDK projects, frozen producer references and external build overrides. Five single-file owner stages compile the identifier lookup, codec, table binding/digest, permission profile and identity profile, with focused tests after each stage. The initial baseline records 190 passes and 143 causal failures. The first run retains all successful compiler/test prefixes but stops with eight invalid new unknown-context fixtures. Those fixtures had supplied authoritative tags or read a block before its update barrier, so native denial was correct. The fixture correction changes no production binary.

A fresh Client test build records 20 passes and 50 causal failures on the old producer, then all 70 Client focus cases pass with the same retained new producer. The 263 Protocol focus cases pass, completing 333 focused passes with zero skips. An ensuing full Protocol run records 7,221 passes and one stale existing expectation that 776 Adventure remains untyped. The two-row boundary test is strengthened to assert decoded empty predicates, byte-identical stream round trips and known DATA errors on both 770 and 776. One Protocol test rebuild then passes both rows and the full 7,222-case Protocol suite, with zero skips. Another 402 adjacent Client cases pass. Failed wrappers retain their original status and raw evidence.

The 93 new Protocol cases include 80 literal original set-slot/hash vectors, twelve fault or modeling boundaries and one fingerprint test that distinguishes an incorrect Plain binding from the actual Either form on the same layout. The 50 new Client cases include sixteen framed Adventure mining/identity cases, four denial-to-fresh-permission cases, twelve explicit unknown-context cases, six real connected empty-list inventory clicks and twelve placement/result/swing cases. Connected clicks invoke public `InventoryActions.ClickAsync` on the session owner, observe no click or partial frame at the peer after the known error, verify a subsequent keepalive, retain local cursor/pending prediction and verify peer EOF. Placement retains 777's local-only swing policy.

The final standalone Protocol DLL is `5868755addfaf956af5338400edb3ac2165183e643e925d24146b575cdabbb20` with PDB `8de03fea0fa381d4a9cda00d5d2c38ab666dcf25ab19860c49bdf34234722cf0`. Its Client DLL is `04353d8c9c5aac76321b8ea6b202dc7d6bfe318b88f33de0f59a0cdd37785cc7` with PDB `ba575db83db1ffa6c94a655250da1932f668327e554eea9a3d196cd1be464300`. Both pairs remain unchanged during test refresh and conformance. The separate joint Tag/Book/Lectern candidate's full Client suite passes 36,522 cases; that receipt belongs to its combined producer and does not certify the standalone Client DLL above.

## Generated identity and strict conformance

Actual non-Plain codec forms contribute to the component shape digest. Existing Plain14 and legacy shapes remain unchanged. Conformance first records 97 passes and the four expected 774–777 identity failures. The documented `UMPK_UPDATE_CODEC_IDENTITY_PINS=1` generator runs on exactly those four rows in the isolated archive. With all update switches removed, 302 focused and all 892 Conformance cases pass without skips.

| Protocol | Previous component digest | Generated digest |
| --- | --- | --- |
| 774 | ae7b3493 | 00730222 |
| 775 | 5b36ac8c | 4a1c3d9c |
| 776 | 4b7a7a87 | 5efdace9 |
| 777 | 557088c1 | bf7c72b0 |

Field review checks all 44 changed lines and 48 nested digest occurrences. Only `components/<digest>` tokens change; every packet ID, byte count, codec/probe field and other character stays identical. All 403 other fixture entries, including timeline and witness pins, retain their hashes. No fixture is hand edited. All 32 public API files remain byte identical, and there are no dataset, dependency, DI lifetime or package-version changes.

The compact managed receipt is `.scratch/adventure774-777-runtime-preparation-20261103-v1/ACTUAL-RUNTIME143.json`, SHA256 `49d74e594e45435b4cd3d99eb0a6b501aee5607720fa7721f6c1041445063281`. It indexes exact commands, source postimages, normal compiler outputs, TRX, generated field diffs, binary pairs and cleanup for all three managed attempts. Raw directories are `adventure774-777-managed-20261103-v1`, `adventure774-777-client-refresh-20261103-v1` and `adventure774-777-final-checks-20261103-v1` below the shared oracle's `runs/` directory. Every owned unit is stopped with no remaining PID; the final run verifies all 25 observed host PIDs absent and an inactive unit with MainPID zero and an empty cgroup. Its single test compiler apphost reports `DOTNET_PROCESSOR_COUNT=1`, under the unchanged 4 GiB/200% CPU limits. Pre-CoreCompile lists omit late SDK inputs; normal build binlogs remain retained.

## Deliberate counts and remaining acceptance

The new cases add 93 Protocol and 50 Client tests, keeping every existing row count unchanged. Relative to `130e973`, the standalone expected counts become Protocol 7,222 and Client 36,201; the seventeen-suite total grows by 143. Concurrent Tag/Book/Lectern additions must be combined separately. These are deliberate baseline changes, not a newly executed local seventeen-suite gate.

Other concrete partial CODECs, unsupported property/range/full block-entity facts and other interaction families remain unfinished where they affect accepted scenarios. Fresh formatting, final code/documentation commit, complete local repository gates, producer-matched consumers and packages, Native AOT, current-head CI and the 50-protocol/75-release live acceptance remain separate requirements. No broader native client-input or graphics behavior is inferred from this bounded original-method and managed session proof.


## Native-server acceptance, 2026-11-03

Five selected releases pass the dedicated `AdventureComponentsDriveNativeMiningAndEquivalentWireProgress` leg with exactly one executed passing test per release and zero skips. The retained 1.21.5/770 control contributes nine scenarios; 1.21.11/774, 26.1/775, 26.2/776 and 26.3/777 contribute ten each. These 49 scenarios exercise actual public UMPK actions against isolated vanilla servers. They are additional bounded acceptance, not completion of the final fifty-protocol/seventy-five-release pack or execution of an original graphical client.

| Release | Protocol | Scenarios | Seconds | Ordinary / Adventure / causal selected replay | Combined payload bytes |
| --- | --- | --- | --- | --- | --- |
| 1.21.5 | 770 | 9 | 47.202 | 219 / 249 / 102 | 3,325,538 |
| 1.21.11 | 774 | 10 | 59.153 | 385 / 420 / 113 | 2,798,600 |
| 26.1 | 775 | 10 | 58.907 | 386 / 421 / 113 | 2,807,813 |
| 26.2 | 776 | 10 | 57.699 | 386 / 421 / 113 | 2,811,049 |
| 26.3 | 777 | 10 | 59.291 | 384 / 416 / 113 | 2,838,498 |

The three replay selections overlap and must not be added as unique frame counts. Each selection decodes original captured bytes with the bound producer codecs, asserts the applicable decoded fields and re-encodes byte identically. The Adventure selection includes configuration tags, held/container components, mining/use/animation, block updates and acknowledgments. Causal replay requires the exact server/RCON markers and game-mode/ability gates. The existing combined 20 MiB recording cap remains unchanged; no leg overflows it.

### Observed action and context boundaries

Physics remains enabled. Every run establishes the native flat-world generator, dry grounded position, full packet-loaded column, zero peer entities, independently observed blocks and actual inventory updates before acting. Generator settings retain SHA-256 `65c9e53a491c688c58eac7422eb4f66b8ca01cfb16be4d309fd3c2a31cb5a898`. Configuration tag frames establish the real `mineable/pickaxe` membership rather than injecting a local tag table. The native server supplies game-mode, abilities and held-stack packets; the session owner applies them before a request is accepted.

Each release covers absent/nonmatching Adventure permission, explicit unavailable ordered-state and block-entity contexts, a survival control, direct and synchronized-tag Adventure mining, equivalent fresh concrete-predicate replacement, and ordinary `can_place_on` placement. The four later releases add fresh AnyRarity replacement. Ten of the 49 scenarios are intentional unknown/no-action controls; they do not claim native permission for unavailable state-range ordering or full block-entity NBT.

Allowed diamond-pickaxe stone mining requires real progress, START/STOP/final animation order and the retained five-call destroy delay. Denied held requests retain exactly their initial animation without START/ABORT and cancel cleanly. Equivalent concrete Damage predicate components arrive as fresh objects in real count-one-to-two slot packets and retain the target. Later AnyRarity presence components instead require native ABORT/START after the inventory owner applies the fresh value. The decoded matcher kind, identifier, AnyValue branch, empty NBT, target block set and owner-event ordering remain asserted. Native stone becomes air in both identity cases.

Ordinary Adventure placement uses actual `can_place_on` stone and independently observes air-to-stone plus stack 32-to-31. Its occupied repeat sends one use without another swing or stack loss. Protocol 777 uses the separate Punch packet for attack animation, ABORT 2 and STOP 3; successful placement emits no swing packet under its measured local-only policy. No rendered arm animation is claimed.

### Retained failure and fixture correction

The first batch passes its 770 control and then fails its first 774 AnyRarity held-item marker. It does not execute 775–777. This remains a failed attempt. The original `ItemPredicateArgument` full-value branch calls `Objects.equals` between the newly parsed command value and the held component. Native AnyValue keys have fresh identity, so that exact-value predicate cannot establish equality by reparsing equivalent JSON. The native source diagnosis and actual timeout agree; no production defect is inferred from that marker failure.

Only the test marker builder changes. The corrected AnyRarity witness combines the actual item ID/count, native bare `can_break` presence and the saved native component path:

```text
execute if items entity InteractionTest weapon.mainhand minecraft:stick[count=2,minecraft:can_break] if data entity InteractionTest SelectedItem.components."minecraft:can_break".predicates."minecraft:rarity" run say <fresh-marker>
```

Original `EntityDataAccessor` delegates to `NbtPredicate.getEntityTagToCompare`, which stores `SelectedItem` through `ItemStack.CODEC`; that codec saves the component patch. Sixteen source bindings cover this chain on all four later server releases, including the criterion-to-predicates package move at 26.2. Each passing later run records three such native saved-component observations. The exact received stream fields, fresh-object and mining order/air assertions stay intact. All other component kinds retain their former exact native item commands.

The all-profile state-unknown precondition also uses an actual native axis range from x through z with observed current axis y and a server-marker/session-owner barrier. The earlier exact-y unknown fixture belonged to an older producer; exact current property values are now owned. The unknown/no-action assertion is preserved for an actually unavailable ordered-range context.

### Producer, build and cleanup ownership

Both entries use the same admitted thirteen-library producer: Client SHA-256 `2c208915154c9f08b4a7ea5696d9ecef8c73ae12707f302e31b959182af57c74` and Protocol SHA-256 `5868755addfaf956af5338400edb3ac2165183e643e925d24146b575cdabbb20`. Its 1,276 authored producer sources are byte equivalent to committed `d13f88e`; retained original compiler metadata is not relabeled as a build from that later commit. The producer manifest is `228d7718204e7aee90fbf457bcd54a7d3e9492f1e878e74fabefcfb3da450cc2`, and source-equivalence proof is `6ce20f448dc808f7bb123ce10daf70bc92fdcc12fbb82dcf4ae60262eb4b2327`.

The valid 770 control retains entry DLL `6928f6f7510cb4303e14c271d6347a7436da2ebde63a2420ce29a957265d42ab`. The corrected four later runs use entry DLL `8ea1fdf7393bb9bb326eae71f708b3225d9fef7822c050a7f9b2acc675472ab2`, PDB `9a2902038357cb6d0271285a7c9b19c3cde78725186d9e618cc8139e84d9e585`. The marker correction cannot select AnyRarity on the 770 concrete-only path, and the valid control is retained under its actual old entry. It is not described as a run of the rebuilt entry.

A normal Integration project formatter changes zero source bytes, followed by exactly one entry Csc and no producer compiler. The rebuild takes 14.116 seconds and peaks at 416,669,696 bytes. Pre-CoreCompile source/reference/analyzer lists and the normal build binlog remain retained; late SDK inputs are not claimed to appear in those earlier lists. All eight build HOST PIDs are absent after its exact unit stops. Across the entry refresh, 134 of 136 runtime files stay identical; only the intended Integration DLL/PDB change. Every later live run matches the complete new 136-file manifest.

The corrected four-row batch takes 236.383 seconds and peaks at 1,461,059,584 bytes under MemoryMax 4 GiB, CPU 200%, swap zero and core zero, without OOM events, GC overrides, compiler activity or automatic retries. All 39 producer DLL/PDB/XML files, twenty frozen references, 4,415 source files and tested seed bytes retain their hashes. Each server and launcher exits naturally with code zero, the native peer observes disconnect and all eight owned listeners close. All 79 observed HOST PID identities are absent; the exact unit is inactive/dead with MainPID zero and an empty cgroup. The earlier failed batch also retains natural cleanup, all four closed ports and all 29 observed HOST PIDs absent. Its failed result is not cleared by resetting only the stopped service's failure marker.

Exact SDK commands and service invocation, source/reference/manifests, per-row TRX and server observations remain under `/home/anon/Projects/MinecraftOfficial/runs/combined-adventure-live-entry-20261103-v2`. The four-row command uses the existing exact method filter and `UMPK_ADVENTURE_RELEASES` set to one release per invocation; it never selects unrelated proxy legs. `CONFIGURATION.json` records the normal restore/format/build arrays, and `adventure-live-v2/TERMINAL-ACCEPTANCE.json`, SHA-256 `3b033c09f0c2b35d67d02ff7f1f57aff216fc9631fb3ee44454c5ccf90418780`, indexes all four completed rows. The retained control/failure directory is `combined-interaction-live-entry-20261103-v1/adventure-live-v1`, whose `TERMINAL-FAILURE.json` is `0f88c99bde6a25498fa9dbf44e299878e910f5caaf9c27ede3ccca08c17cf06d`. Raw logs, worlds, official jars and credentials remain outside Git.

The machine-readable scenario index is `.scratch/adventure-later-live-preparation-20261103-v2/LIVE49-SCENARIO-INDEX.json`; the source correction and sixteen native bindings are in its `SOURCE-PROPOSAL.json`. This checkpoint adds no normal opt-in cases or baseline counts: the existing theory keeps its four default 766–769 releases and permits later releases only when explicitly selected. Current-producer consumers, packages/runtime, AOT, the final repository gates, other contextual/interaction families and the final 125-row matrix remain separate. Later production changes require explicit matching source/runtime acceptance before these rows receive final-tuple credit.
