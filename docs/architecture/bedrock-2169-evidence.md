# Bedrock 2169 evidence ledger

This ledger is the reproducible baseline for marketed release 26.45, dataset 1.26.45, and game protocol 2169. **Verified** means inspected source, data, or an executed local command. **Conclusion** means a design decision derived from that evidence. **Unresolved** means the available evidence does not establish the behavior. A community implementation is supporting evidence; byte behavior from the exact BDS build is the interoperability oracle.

## Target identity

- **Verified:** The target is BDS 1.26.45.1, dataset 1.26.45, game protocol 2169, and RakNet transport protocol 11. The protocol numbers describe different layers.
- **Conclusion:** Release/build, game protocol, packet dialect, data profile, and behavior profile are independent identifiers. The implementation selects `bedrock-1.26.45-2169` explicitly and rejects an unknown same-number dialect.
- **Unresolved:** No additional 26.45 same-number dialect has been identified. A newly observed dialect gets its own profile and witnesses; it never silently resolves to the latest codec.

## Source revisions

| Source | Revision | Verified role and boundary |
|---|---|---|
| UMPK | `509694de2ad8ba4a0d550da2466a7fb148226d17` | Starting implementation revision and Java/API/AOT reference. |
| minecraft-data | `8ee2fbe890949fe6c2cf037cd50007ef50444931` | Primary packet and data source. Frozen 1.26.45 JSON and reused `bedrock/latest` inputs are hashed separately. |
| MCCTeam/RakNet | `673b6fc737ee4273f05a7fbf2a99bc87311a1517` | Primary .NET transport source before corrections. Bedrock packet knowledge is excluded from the fork. |
| PowerNukkitX | `f325c30eb75ae869f3f0652d464526de7c0c9877` | Selects 1.26.45/2169 and provides lifecycle, chunk writer, hash, and mapping-loader evidence. Its mutable snapshot dependency is not treated as a frozen source. |
| Geyser | `d50a5ffbd8a6924ad52cf4c211600e8ca24c7c3d` | Registers 26.45/2169 and provides translation/session-state corroboration. Java-authoritative behavior is not native Bedrock reconciliation evidence. |
| Cloudburst Protocol | `db36d289968a1d07d6591f0d54426cc058b227c0` | Serializer cross-check close to Geyser's pinned artifact timestamp. Exact source-to-artifact identity remains unresolved. |
| PrismarineJS/bedrock-protocol | `05940d77bee67b96395c69afb57b92e32bea4148` | Connection, authentication, compression, and encryption cross-check. |
| Sandertv/gophertunnel | `b8bd7357c24fcba3e32f940d1afb1d3b4e43b96d` | Independent framing and encryption cross-check. |

## Frozen input hashes

| Artifact | SHA-256 | Use |
|---|---|---|
| `minecraft-data/data/bedrock/1.26.45/protocol.json` | `83ce13dd473c0ee07bde75d21740ca852a1f432dffb5dd032253158082c382f1` | Frozen compiled protocol input. |
| `minecraft-data/data/bedrock/1.26.45/version.json` | `4abf57327908d3c1f197df9b3d6b3162c72148213651fe7cc1cd6db5de9b26e9` | Frozen version identity. |
| `minecraft-data/data/bedrock/latest/proto.yml` | `ac646a84404ce472dd6e8cbbdcb9b79384a18bd7db6dc88e4214bd1777dc5ee5` | Reused mutable upstream input, pinned by content hash before import and never read at runtime. |
| `minecraft-data/data/bedrock/latest/types.yml` | `bc6f09026453b467ca6cd1068fa08e4f824b5011fdd33ca848f10dd544c62cfe` | Reused mutable upstream input, pinned by content hash before import and never read at runtime. |

## Exact BDS target and acquisition

- **Historical verification:** At initial acquisition, `pacman -Qi minecraft-bedrock-server` reported package `minecraft-bedrock-server 1.26.45.1-1`. `/home/anon/Minecraft/bedrock-server/bedrock_server` is a stripped x86-64 ELF PIE with SHA-256 `8ba803f23d681816495c7ac83bdba4b9cd7165a3bee5aedd18fa0c8c3d408ec2` and BuildID `0b40c886132941f2cd98d45e73062bc75e66726e`.
- **Verified:** The package installs BDS under `/opt/minecraft-bedrock-server`, creates the unprivileged `minecraft-bedrock` account, and supplies `minecraft-bedrock-server.service` with that directory as its working directory and `LD_LIBRARY_PATH=.`. The packaged `server.properties` selects RakNet and defaults to UDP 19132 for IPv4 and 19133 for IPv6. The ArchWiki documents the same account, directory, service, launch environment, and ports.
- **Conclusion:** Live tests copy or stage an isolated runtime outside Git, disable LAN visibility, choose unique IPv4 and IPv6 ports, set an isolated world name, and use bounded startup and shutdown. Each run recomputes the executable hash and records package version, BuildID, settings, port, start time, and result in its private run directory.
- **Conclusion:** Binaries, worlds, raw logs, packet captures, auth material, and disassembly traces stay outside Git. Only reproducible procedures, hashes, minimal redacted byte fixtures, and behavioral conclusions may enter the repository through their governed fixture workflow.

## Native inventory and movement follow-up

The 2026-10-12 native investigation used the licensed 1.26.45.1 Windows x64 client image, SHA-256 `008408e6f544d7987f9cde4e34c8e853690a65afd97f8498da2cd6ed24c4793b`, and the cached exact BDS package, SHA-256 `1ca02a753e04d1951906d8530c76139675f0b8ec11732cf31817dcde01af1446`. Its packaged executable is SHA-256 `297ce10bee4b1e7486ac8fb22be51dbfa151e69d76dfd88c84da524cb6ffefce`; this differs from the original unpatched executable above. The installed distribution has subsequently changed to 26.52, so a live run selects and verifies a separate 26.45 distribution through `UMPK_BDS_SOURCE_DIR`. Raw artifacts and licensing material remain outside Git.

- **Verified native instructions:** Client allocator `0x142eeb650` generates successive odd negative request IDs, beginning -3 and -5. Client validators `0x14755ee34`/`0x148bea104` and exact-BDS validators `0x7601009`/`0x7f7b0e9` test the odd-negative class. A historical probe of -1, -2, 0, 1 and 2 did not establish that only -1 works.
- **Verified finite runtime observation:** A private isolated exact-BDS Place witness accepted -3 and then -5, with correlated response status0 and independently calibrated server slot counts. No matching new content49 arrived after either operation. The original complete raw directory later disappeared for an unproved reason; observed payload hex and command results were retained, but original raw-bundle replay is unavailable. New implementation qualification requires a fresh retained witness.
- **Verified native instructions:** Response manager `0x142eed140` selects retained request/prediction item state for a known accepted request, applies the returned count through `0x1422dabc0`, replaces the typed negative request identity with a plain server stack identity, publishes the item and removes the matched prediction. Its described accepted path does not wait for content49. Response148 alone cannot identify an unknown item type.
- **Conclusion and current implementation boundary:** The internal client admits one known hotbar Take, Place or Swap at a time, allocates never-reused odd negative IDs and retains the original items before sending. A correlated accepted response updates counts and stack identities atomically. Missing, conflicting or unusable affected rows make those slots unavailable until authoritative content or slot data arrives. Name and durability corrections remain explicit sidecars to opaque item data. This implementation does not qualify creative/crafting, overlapping predictions or lifecycle recovery.
- **Verified native instructions:** The formerly opaque BDS call `0x85b3a60` uses a RTTI-bound PacketSecurityController. Gameplay dispatch is separate at `0x85b3ae8`; packet144 reaches a typed server callback, a connection-plus-subclient actor lookup and gated movement queue storage. Typed initialization113 directly writes actor byte +0xcbe; a separate writer sets +0x6b1 and sends PlayStatus3. Static queue admission, structural validation and packet-security success do not establish server-applied movement. Queue consumption, runtime physics ordering and position authority remain separate witnesses.

A fresh production live witness on exact packaged BDS passed two sequential Place operations with -3/-5, byte-identical147/148 round trips, full inventory reconciliation, nineteen calibrated server count controls and final counts4/0/1. Player content49 count stayed at one throughout; the second request used the first accepted response’s returned positive stack identity. Four owned process identities exited, and fourteen raw evidence files were independently read back and hash-verified outside Git. This qualifies the single-Place sequence only.

Focused reconstruction, allocator, store and session tests prove local state and ownership behavior. Live, conformance, AOT and complete phase acceptance are recorded separately; successful local tests do not promote a public gameplay capability.

## Protocol discrepancy ownership

| ID | Evidence disposition | Owner, witness, and blocking scope |
|---|---|---|
| D01: base 2168, hotfix-4, and 2169 | **Verified:** 2169 extends base 2168 while hotfix-4 has a distinct SetScore override. Identical community schema files do not prove staleness. | Data/code generation owns explicit profile selection. An unknown-dialect rejection test closes the selection contract. |
| D02: RemoveScore objective | **Verified:** Base/2169 retains one optional-objective marker; hotfix-4 adds a second marker when the objective is present. | Native codecs own exact independent vectors. Only scoreboard decoding is blocked until the vectors pass. |
| D03: PlayerAuthInput list | **Verified:** The optional list uses a presence Boolean, VarUInt count, signed ZigZag VarInt enum ordinals, and unsigned VarLong tick. | Native codecs own ordered/presence vectors. Movement input encoding is blocked until they pass. |
| D04: ItemV4 | **Verified:** The network stack form uses signed i16LE runtime ID and u16LE count; item-instance forms use signed VarInt identifiers. | Native codecs own separate context-bound forms. Inventory capabilities remain unavailable until their forms pass. |
| D05: metadata and stack requests | **Verified:** Metadata and request actions contain distinct outer and inner discriminators; request-slot stack network IDs are fixed i32LE. | Native codecs own exact vectors that preserve both discriminator bytes and widths. |
| D06: MovePlayer | **Unresolved:** Cloudburst uses unsigned VarLong for actor runtime ID, riding runtime ID, and tick while minecraft-data declares different signed/fixed forms. | Data generation and native codecs own a preconditioned overlay. Independent serializer bytes plus an exact BDS trace are required before movement correction is supported. |
| D07: chunk envelope | **Verified:** LevelChunk has independent limit presence and unconditional blob count; SubChunk members have independent optional payload, heightmap, and hash fields. Native subchunk palette bodies remain separate. | World/terrain owns exact palette, layer, singleton, and partial-arrival witnesses. Terrain support is blocked per unresolved form. |
| D08: prediction flags | **Verified:** The unsigned seven-bit encoding has 131 logical bits and occupies 19 bytes for this profile, including an all-zero value. `UInt128` is insufficient. The packet direction and three prediction-sync float meanings remain unresolved. | Generation records the logical width and a separate byte limit. Native codecs preserve unknown bits, and registration stays blocked until exact 2169 traffic resolves direction. Reconciliation owns the remaining semantics. |
| D09: remaining validation | **Unresolved:** Skin enums, resource response details, selected StartGame values, prediction semantics, and some chunk forms need exact-version witnesses. | Authentication/resource policy, join lifecycle, and terrain own their respective captures. Each issue blocks only its consuming capability. |

## Required packet seed

The canonical names below match minecraft-data 1.26.45. Numeric identifiers and transitive type dependencies are resolved by BedrockDataGen and are not copied by hand.

| Packet | Direction | Initial obligation |
|---|---|---|
| `request_network_settings` | client to server | Required before Login. |
| `network_settings` | server to client | Installs compression settings before the next dependent envelope. |
| `login` | client to server | Required identity/client-data envelope. |
| `server_to_client_handshake` | server to client | Enables the inbound encryption transition when sent. |
| `client_to_server_handshake` | client to server | Completes the encrypted handshake when required. |
| `disconnect` | server to client | Produces a typed terminal result. |
| `resource_packs_info`, `resource_pack_stack` | server to client | Required protocol responses stay separate from download/accept/cache policy. |
| `resource_pack_client_response` | client to server | Required response with explicit content policy. |
| `start_game` | server to client | Publishes bootstrap state; it does not alone mean ready. |
| `item_registry` | server to client | Publishes item context before dependent records, including within one batch. |
| `level_chunk`, `subchunk` | server to client | Supplies cache-disabled terrain with explicit partial/unknown state. |
| `subchunk_request` | client to server | Correlates requested terrain when the negotiated mode needs it. |
| `player_auth_input` | client to server | Couples the one session tick with input, prediction, and ordered actions. |
| `move_player` | both | Blocked on D06 for correction use. |
| `client_movement_prediction_sync` | unresolved | D08 encoding is separate from unresolved direction and gameplay semantics. PocketMine labels it client to server, while the primary schema has no bound direction; exact 2169 traffic is required before registration. |
| `set_score` | server to client | Covers D01/D02 serializer selection. |
| `item_stack_request`, `item_stack_response` | both | Correlated authoritative inventory operations. |
| `add_entity`, `remove_entity` | server to client | Maintains runtime and unique ID namespaces. |
| `text` | both | Capability-gated chat/text subset with no Java command-tree assumption. |

## Initial capability boundary

| Capability | Initial claim | Required later witness |
|---|---|---|
| Offline join | Direct IPv4, one subclient, exact BDS build. | Controlled offline join through truthful readiness and peer-observed teardown. |
| Online join | No initial claim. | Separately authorized account leg with token, signature, refresh, and cancellation evidence. |
| Actors | Runtime and unique identifiers with session-qualified lifetime. | High-value IDs, removal, dimension change, and stale-work tests. |
| Terrain | Cache-disabled, partial layered state, known-air distinct from unknown. | Exact palette and subchunk forms plus partial/out-of-order live traces. |
| Inventory | Explicitly listed request/response operations only. | Independent request IDs, stack IDs, rejection, correction, and resync witnesses. |
| Movement/actions | Declared basic modes driven by one session tick. | Exact input bytes, measured physics, correction replay, and no-duplicate action tests. |
| Navigation | Disabled until world, simulation, correction, and capability prerequisites pass. | Bounded route execution against exact BDS with intentional correction injection. |

## AOT and infrastructure

- **Conclusion:** Linux x64 is the first mandatory native publish-and-run leg. It uses the repository-pinned SDK and treats ordinary, ILLink, and ILC warnings as errors. The binary must execute real assertions for transport, codecs, crypto, bootstrap, and the advertised client slice; a successful publish alone is insufficient.
- **Conclusion:** The historical unchanged-fork SDK 10.0.112 probe establishes feasibility only. Modified RakNet and the whole Bedrock stack are rerun from exact package hashes. Other RIDs are added only with an available host and an executable gate; unavailable infrastructure remains visible and never passes by omission.

## Closure rule

Every new finding records target build, source or bytes, affected field, conclusion, owner, minimal reproduction, and blocked capabilities. A conflict stops only the consuming capability. Its implementation can continue behind an explicit unavailable result while unrelated work proceeds.

## Initial loading notifications

Exact BDS 1.26.45.1 executable SHA-256 `297ce10bee4b1e7486ac8fb22be51dbfa151e69d76dfd88c84da524cb6ffefce` decoded serverbound packet 312 payload `02 00` as StartLoadingScreen1 and `04 00` as EndLoadingScreen2, both with absent loading-screen identifiers. Both passed security and reached the connection-local actor; End appended one SchedulePlayerLoadingScreen end event. These observations come from the isolated offline run retained at `/home/anon/.local/state/umpk/bedrock-research/2026-10-12-native-explicit-loading-nwgf1g7p/9b4769d5954b`, metadata SHA-256 `e0f995bbca4353400457d99e00c413a616e9a1af054c1941c18a40201db78afe`. The run failed its later protection-clear prerequisite and sent no movement input. Its missing clear observation does not establish a server failure: the observer's outer/inner registry filter was subsequently found incorrect.

The internal protocol-2169 loading codec preserves the signed ZigZag32 type and Boolean identifier engagement. An engaged identifier uses fixed little-endian UInt32 according to the frozen primary schema; tests for that branch are synthetic and do not claim an engaged-ID live witness. The codec rejects unsupported types, noncanonical scalar values, truncation, excess length and trailing bytes. This codec introduces no automatic session loading policy. The native client's initial scene-release condition, production notification timing, protection clearing and movement application remain separate obligations.

A fresh run with the corrected observer passed the protection-clear prerequisite and observed the same actor's movement component change from 4 to 0. One unchanged idle input then passed native admission, entered the queue, drained into current movement and reached correction processing. Evidence is retained at `/home/anon/.local/state/umpk/bedrock-research/2026-10-12-umpk-native-explicit-loading-corrected-n7k0tl7y/ca59f4f452c2`, metadata SHA-256 `428b411ac9c77d2a3778ab407c2e12950896e262739b83d96c63da39d1993073`. This qualifies the observed loading transition and idle input queue path. Native UI completion timing, a production tick policy, displacement and physics remain unqualified.

The initial headless session policy sends Start after StartGame actor/world setup and awaits End after its existing resource, registry, spawn and initialization prerequisites complete. It publishes usable Ready only after End transfers to the transport. The initial Start, initialization113 and End sends share generation-scoped admission and the existing phase deadline. Life/world/terminal retirement cancels pending capacity waits; capacity already granted remains committed and is accounted when its result returns. A deadline can publish Timeout promptly while that commit remains awaited. This is local protocol completion rather than an acknowledgement of the remote protection flag. Later loading IDs and movement recovery remain unsupported, and an already pending movement input still needs separate lifecycle admission ownership.

The fixed native creative acquisition also passed against exact BDS: actual Start/End transfers preceded self inventory-open33, observed ContainerOpen46 and one request-3 accepted by response148 with cursor count64 and positive stack ID1. The item and creative IDs came from the live registry/catalog. Evidence is retained at `/home/anon/.local/state/umpk/bedrock-research/2026-10-12-creative-explicit-t3m6zjqv/742d59b5ef15`, metadata SHA-256 `1be2d2a4f2fbc05c23033bc82946225bfee44833331dedecc95f3bf30627d4d4`. The diagnostic used a frozen private source fork; generic creative acquisition and full-item convergence remain unqualified.

A fresh witness using the current production DLL observed automatic Start312, initialization113 and End312 in order, then the same exact-BDS actor's protection4→0 transition before one idle input reached queue, drain and ServerCorrectionPolicy. Evidence is retained at `/home/anon/.local/state/umpk/bedrock-research/2026-10-12-umpk-native-production-loading-gb3g_oye/056059289e86`, metadata SHA-256 `22642038a199b0a65d14df38a8c392d3049f97d67cdd410b6bf9d9bbc99bf9ba`. All three incoming respawn45 payloads round-tripped, all19 final evidence hashes matched and seven owned descendants exited without forced cleanup. This qualifies the production initial loading path; the test waited independently for native clearing and establishes no ordinary locomotion or tick cadence.

The unchanged production inventory regression also passed with this bootstrap: two accepted Place requests reconciled full retained items without another content49, with19 independent quantity controls. Evidence is retained at `/home/anon/.local/state/umpk/bedrock-research/2026-10-12-umpk-current-reconciliation-uboaq0z2/52af73929ea5`, metadata SHA-256 `576162db2012e686f59bbfaa25d8632f62bf67bc62a6d370cac2d472aa901c55`. Full solution validation reported13629 passed,40 gated skips and no failures across23suites; build, count, format, datasets and generated freshness passed. Java language re-extraction remains unpassed because its official representative jars are absent.

Movement submission now links caller cancellation and the session-owned movement admission lifetime across both serialized egress and transport capacity waits. Respawn, world replacement and terminal teardown retire that lifetime permanently; an already granted token-free commit retains its result and tick/transcript accounting. Disposal cancels waiters and waits on the existing egress owner before destroying the transport or connection. A granted commit that never returns can keep disposal pending. Eighteen deterministic tests cover these ownership boundaries; this changes local admission safety and makes no locomotion or recovery claim.

The Java language prerequisite was subsequently restored using all73 official catalog-named jars outside Git. All50 representative SHA-256 values matched the committed provenance, and language re-extraction passed for50protocols without missing-alias skips. Durable verified inputs are `/home/anon/.local/state/umpk/oracles/java-language-2026-10-12-_9muj3nk`, retention manifest SHA-256 `b7cd30c9d4f931db5c31c1a84a72a25b77af702cb3abe6f0d1474b3679c1bf2c`. No Java server was executed or dataset changed.

The movement admission build passed all 23 suite totals: 13,647 passed, 40 gated skips and zero failures. Build, format, count, datasets, language and generated freshness passed. A fresh witness against exact BDS using Client DLL SHA-256 `78b10e02189c297db5752d051e5132c3b47e5382a4d445b577265dda90c0c317` observed production loading completion, native protection clearing and one unchanged idle input reaching queue, drain and correction policy. Durable metadata is `/home/anon/.local/state/umpk/bedrock-research/2026-10-12-umpk-native-current-movement-loading-sffd1osa/1f1f70600b39/one-packet-result.json`, SHA-256 `fac5c8581225e6e8cb41755f85e96df9bee8313937d8156c615b96cd2fdf667d`. All 19 final artifact hashes and seven owner exits were verified. This qualifies the current admission and disposal path; ordinary locomotion remains unavailable.

The unchanged ordinary inventory regression passed with that same Client DLL: two accepted Place requests converged retained full items using packet148 without a replacement49/50, with 19 independently checked quantity controls. Durable metadata is `/home/anon/.local/state/umpk/bedrock-research/2026-10-12-umpk-current-reconciliation-movement-3nvv1_b9/f9b13e9636c6/itemstack-reconciliation.json`, SHA-256 `7fd20d4e94112b03abf93206a663cb5c57f9764e6f4871c5cd3845aac867512c`. All 16 final artifacts matched and six recorded owners exited without forced cleanup. This verifies ordinary inventory behavior alongside the movement admission fix; creative cursor integration remains incomplete.
