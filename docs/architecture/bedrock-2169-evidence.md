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

- **Verified:** `pacman -Qi minecraft-bedrock-server` reports package `minecraft-bedrock-server 1.26.45.1-1`. `/home/anon/Minecraft/bedrock-server/bedrock_server` is a stripped x86-64 ELF PIE with SHA-256 `8ba803f23d681816495c7ac83bdba4b9cd7165a3bee5aedd18fa0c8c3d408ec2` and BuildID `0b40c886132941f2cd98d45e73062bc75e66726e`.
- **Verified:** The package installs BDS under `/opt/minecraft-bedrock-server`, creates the unprivileged `minecraft-bedrock` account, and supplies `minecraft-bedrock-server.service` with that directory as its working directory and `LD_LIBRARY_PATH=.`. The packaged `server.properties` selects RakNet and defaults to UDP 19132 for IPv4 and 19133 for IPv6. The ArchWiki documents the same account, directory, service, launch environment, and ports.
- **Conclusion:** Live tests copy or stage an isolated runtime outside Git, disable LAN visibility, choose unique IPv4 and IPv6 ports, set an isolated world name, and use bounded startup and shutdown. Each run recomputes the executable hash and records package version, BuildID, settings, port, start time, and result in its private run directory.
- **Conclusion:** Binaries, worlds, raw logs, packet captures, auth material, and disassembly traces stay outside Git. Only reproducible procedures, hashes, minimal redacted byte fixtures, and behavioral conclusions may enter the repository through their governed fixture workflow.

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
