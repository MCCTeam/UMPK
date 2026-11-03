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
