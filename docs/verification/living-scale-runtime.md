# Living scale through owned geometry and physics

Commit `193178f` connects scale attributes to cached entity bounds and the existing local physics owner. The bounded families are player, villager, cat, cow, horse, iron_golem, chicken, pig and sheep on protocols 766–777, covering nineteen named releases. Other entity families, riders and pushes retain their separate unknowns and acceptance requirements. This is an implementation checkpoint; fresh combined-producer live, consumer, package and AOT checks remain pending.

All evidence paths below are relative to `/home/anon/Projects/MinecraftOfficial/runs`. Raw traces, original classes, logs and binaries remain outside Git. The original private producer is `03fabffd30c774b0a668bf8a3bd8d05bbc71ff15+scale-v1:5549beb9f56ed1cd907d628ea2fdf5771a2af160f03334b93697ff64fbede0a7`; its receipts are not relabeled as final combined-producer results.

## Behavior and callers

`EntityApplier`, `EntityGeometryState` and the existing session clock retain prospective scale separately from cached dimensions. Native attribute resolution clamps the double to `[0.0625,16]`, maps NaN to the minimum, then narrows to float before dimension multiplication. Age/default dimensions precede scaling. Sleeping dimensions and the player's dying body retain their fixed exceptions. Attribute receipt and explicit positioning do not resize early; native pose/age callbacks can refresh dimensions before the deferred phase.

Protocols 766–769 refresh when the applied scale float changes. Protocols 770–777 use actual native attribute dirtiness, including changed double values with the same narrowed/clamped result and modifier changes. Ordinary actors require measured packet-established column membership for the existing tick phase; players retain their native ticking exemption. Freeze/step, removal, replacement IDs and lifetime transitions are covered. The existing motion owner consumes refreshed bounds; this change adds no movement timer or velocity controller.

The local bridge uses `Self` and `PhysicsEngineHolder`, with no synthetic self entity in `EntityStore`. Three default-neutral additive properties are recorded in `Umpk.Physics/PublicAPI.Unshipped.txt`: nullable `PhysicsConditions.Scale`, `PhysicsConditions.RefreshScaleDimensions` and nullable `PhysicsState.DimensionsScale`. A pending refresh survives condition pushes until its phase consumes it; cached body/eye state remains distinct from prospective input. Default null preserves the existing unscaled behavior.

On respawn, protocol 766 copies scale attributes only with keep bit 1. Protocols 767–777 copy complete values with that bit and otherwise copy base values without modifiers. The new local physics owner rebuilds before consuming the incoming lifetime's refresh, preventing stale cached eyes. Other attribute reset policy is unchanged.

## Retained native evidence

| Native campaign | Executed scope |
| --- | --- |
| Original attribute codecs/listeners and dimension callbacks | 23,199 transition rows across all nineteen releases |
| Native transient entity-section callbacks | 1,368 membership observations |
| Original LocalPlayer constructors, body/eye/pose callbacks | 245 rows across five boundaries |
| Original local prospective-fit/pose/dirty callbacks | 48 rows across 769/770/777 |
| Joined scaled remote-player motion | 270 cases and 1,800 complete original `ClientLevel.tickNonPassenger` phases on 769–777 |
| Native dirty-change pilot | 35 cases and seventy complete RemotePlayer ticks |
| Original local constructors and source-selected attribute transfer | 228 respawn-transfer cases across nineteen releases |

These campaigns pin original class origins and source bindings. Their controlled world lookups, output substitutions and exact commands are retained under `ordinary-living-scale-runtime-20261022-v1/native`. They do not execute a complete original LocalPlayer movement tick, full native respawn listener or world traversal. Remote-player ticks are not local-player proof.

Root independently verifies every field and packet body in the five compressed fixtures against their retained native traces: 23,199 ordinary rows, 270 motion rows, 245 local rows, 228 transfer rows and 32 current local-phase rows. It also verifies all 2,337 original delivery receipt hashes. `ordinary-living-scale-runtime-20261022-v1/root-review-v1/review.json` has SHA-256 `d97df0471f29e70edfb23b605cf4db555ac1602b72606e5e23b8cd952b3ae6b6`.

## Runtime checks and integration

There are 1,248 deliberate Client additions, 42 Physics additions and one opt-in Integration theory. Actual framed UmpkClient sessions exercise attribute dispatch, session-clock refresh, membership, joined motion, freeze/step, removal/replacement, local eye raycasts, respawn transfer and placement packet groups. Separate bound-applier cases exercise pose/age callbacks. The native boat contexts remain unknown where hard-collision ownership is missing.

Original valid red campaigns include 195 ordinary failures, thirty self-eye failures with five controls, and 228 respawn failures; transfer-only implementation still failed all 228 on stale eyes. Their successful successors and earlier compile/harness failures remain separate. The final private full suite passes 40,991 cases with twelve normal opt-in skips and seventeen matching totals, including 27,494 Client and 696 Physics cases. Its AOT binary is 25,058,512 bytes, 4,112 above the same original baseline, with exactly one executed passing smoke. That size is not a measurement of the merged producer.

The reviewed preview preserves newer axe, feature and motion changes through clean three-way merges. The final 22-path delivery selects the separately reviewed v3 Scale harness; its manifest is `ordinary-living-scale-runtime-20261022-v1/integration-delivery-b261-v2/manifest.json`, SHA-256 `af95ddf0a38857c8615eb05ef71204d0b4984c39a6bc79c8d76d03381595dd8a`. The combined preview with the separate creative owner fix passes 1,713 Client and 696 Physics focused cases, Integration build, discovery and scoped format. `scale-creative-aggregate-preview-b261-20261022-v1/manifest.json`, SHA-256 `679ff3935519e52436d53ec2f1aad05ef38d42f01d7c7ccb41f350d5ad712cb6`, binds those exact sources and actual TRXs. The later genuine creative-session supplement has its own receipts and deliberate counts.

## Pending acceptance

Fresh scale live legs at 1.20.5/766, 1.21.5/770 and 26.3/777 must execute on the combined immutable producer. They require actual physics, scaled eye targeting, self/UUID-bound cow obstruction and shrink controls, native block/stack/position checks, exact use/swing, real removals, frame replay and owned-process cleanup. The first private 1.20.5 attempt remains a harness failure: console success text reports raw attribute base while the command multiplier affects only its integer result. The corrected v3 harness is built and reviewed but has no passing live row yet.

A declared external vanilla solid-flat fixture is being verified to prevent unrelated generation actors from entering the controlled scenario. It changes no original server class, shared seed or production unknown handling. Final immutable repository, consumer, package, AOT, live and CI results must identify the actual combined producer. Full native local movement and the broader [completion ledger](interaction-completion.md) remain open.
