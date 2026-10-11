# Bedrock implementation contracts

These contracts are the stable working boundaries for the B01 through B23 architecture. They constrain implementations without pretending that every protocol field is already known.

## Project boundaries

| Component | Responsibility | Dependency rule |
|---|---|---|
| `Umpk.Core.Transport` | Small owned-message seam with cancellation, admission, ownership transfer, and terminal completion. | No RakNet type, Java phase, or edition state. |
| `Umpk.Transport.RakNet` | Corrected fork adapter, message delivery, bounded admission, logging, and typed transport termination. | Depends on Core and the exact pinned RakNet package. No Bedrock packets or game ticks. |
| `Umpk.Protocol.Bedrock` | Primitive codecs, NBT wire modes, packet records, framing, compression, encryption, bootstrap obligations, and immutable codec context. | May use Core, NBT DOM, and proven value types. Does not depend on Data.Bedrock or Client.Bedrock. |
| `Umpk.Data.Bedrock` | Generated immutable version descriptors, static registries/defaults, source provenance, and support profiles. | Depends on Protocol.Bedrock. Runtime never reads mutable source schemas. |
| `Umpk.Client.Bedrock` | Composition, credentials, readiness, actor/world/inventory state, movement, correction history, semantic capabilities, and disposal. | Depends inward on the Bedrock protocol/data/transport projects and validated shared foundations. |
| `Umpk.BedrockDataGen` | Import, normalize, verify, diff, and deterministic generation. | Build-time only. No ProtoDef interpreter ships in a runtime package. |

`UmpkClient`, its builder, Java `IConnectionFactory`, Java framing/lifecycle, package defaults, and reserved Server/Proxy projects remain unchanged. `BedrockClient` is a separate entry point with an explicit Bedrock endpoint and port. Java packet handlers, inventory clicks, physics profiles, and Brigadier adapters do not satisfy Bedrock contracts.

## Owned message connection

- A connection has a single inbound reader and a serialized outbound admission path. The exact multiplicity is executable API behavior, not an implementation accident.
- Successful send completion means the complete logical message was admitted to bounded connection-owned storage. It does not mean UDP transmission, peer ACK, decoding, or server application acceptance.
- A transport can expose an optional send reservation. The reservation holds capacity for the exact complete-message length at the connection boundary before a stateful transform advances. Committing the complete message makes the admission decision once and has no caller-cancellation point; releasing an unused reservation restores capacity. A closed connection can reject a commit, and the protocol must then terminate its stateful stream.
- Outbound ownership transfers only after successful admission. Rejection or pre-admission cancellation leaves ownership with the caller. Every inbound message has one receiver owner and is disposed exactly once.
- Terminal completion is distinct from establishment and receive availability. It records peer close, local graceful close, abort, caller cancellation, protocol failure, and transport failure without converting teardown into a successful connect.
- Graceful close drains only within a deadline. Abort is prompt. Disposal waits for owned tasks, timers, registrations, sockets, and buffers without joining the currently failing worker.

## Codec context and registry barrier

- Packet dialect, numeric protocol, data profile, behavior profile, direction, subclient routing, compression state, encryption state, and immutable registry snapshots form the decode context.
- A registry-producing record publishes a new immutable context before decoding the next dependent record, including records later in the same batch.
- Registry publication can synchronously update decode context. Semantic state commits run on the session scheduler and cannot block receive progress by awaiting work that requires more receive progress.
- Packet IDs, subclient bits, runtime IDs, unique IDs, stack network IDs, request IDs, and recipe IDs remain exact wire values. Session-qualified actor handles, container addresses, world generations, and capability results are separate semantic identities.

## Actor and world identity

- Actor state retains unsigned 64-bit runtime ID and signed 64-bit unique ID. A player UUID exists only when the wire supplies it. Removal uses the unique ID stated by the protocol.
- World, dimension, and registry generations are explicit. Replacement invalidates pending requests and background results before stale data can mutate the new generation.
- Terrain queries distinguish absent, partial, known-air, and known-non-air. An unreceived subchunk is unknown. Network, disk, Java, and translation mapping layouts stay separate.

## Action and tick ordering

- The session tick is the only locomotion clock. PlayerAuthInput, predicted state, embedded item/block actions, and their action timeline share that tick boundary.
- RakNet retry/ACK timers, chunk workers, UI callbacks, and path search never advance locomotion.
- Correction history is bounded by tick count, bytes, snapshot versions, and replay work. Missing history takes an explicitly tested resynchronization path or yields an unavailable result.
- Replay cannot duplicate an inventory request, block break, placement, use action, or other side effect. Each action has an identity and exactly one authoritative reconciliation outcome.

## Capability rules

- Public capabilities are immutable observations of the selected profile and completed evidence gates. Unsupported behavior returns an explicit unavailable result before sending guessed bytes.
- Initial scope is direct IPv4, one subclient, proven ReliableOrdered/channel behavior, cache-disabled terrain, and individually listed movement and inventory operations.
- Online authentication, nonzero subclients, other address families, terrain blob caching, vehicles, flight, gliding, and unlisted inventory/crafting actions each require an independent witness before advertisement.
- Navigation stays disabled until its world queries, physics modes, correction policy, and required actions are all verified for the active session.

## RakNet consumption

The corrected fork is built as a distinctly versioned package into an isolated local feed. The build records fork commit, clean/dirty state, SDK, package identity, package SHA-256, and test results. UMPK pins that identity without an absolute cross-repository project reference. Generic parser, sequence, fragment, queue, lifecycle, and cancellation corrections belong in the fork; Bedrock batching, encryption, login, registries, and game ticks belong above the adapter.

## Native AOT gate

Linux x64 is mandatory first. The gate publishes with AOT compatibility and all ordinary, trim, and native-code warnings as errors, then runs the native binary against independent byte assertions and an isolated exact-build server. A publish with no executed assertions is not a pass. Other RIDs remain visible as unavailable until a matching host executes the binary.
