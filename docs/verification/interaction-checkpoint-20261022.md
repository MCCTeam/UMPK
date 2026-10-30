# Interaction boundary checkpoint, 2026-10-22

The tested immutable producer is `548111813953d8ea8b0e5b00e6c371059b3a91c2`. It adds [pre-position attack charge](attack-charge-readiness.md), [standing modern remote-player motion](modern-player-motion-runtime.md), [protocol-47 falling-block bounds](protocol47-falling-blocks.md), [axe results and enabled-feature permission](axe-use.md), and dedicated axe/motion acceptance legs to the preceding combined interaction checkpoint. These changes supply native timing and bounds to the existing mining and placement owners. The [completion ledger](interaction-completion.md) keeps the remaining behavior and live acceptance open.

All `runs/` paths are relative to `/home/anon/Projects/MinecraftOfficial`. Exact commands, logs, TRX files, binaries, captures and official artifacts remain outside Git. `runs/interaction-boundary-validation-20261022-v1/checkpoint-receipt-index.json` binds forty selected receipt files, including the retained live harness failure and native piston witness. Its SHA-256 is `83be5c1fbeb35d9fc5c0d4e1e28e44358c6f0bcf7fa89d13b6e660a573cfba4d`. Package version `0.9.0-beta.7`, dependencies, DI lifetimes and public signatures are unchanged.

## Immutable repository and package checks

`runs/interaction-boundary-source-20261022-v1` is outside every Git checkout. Validation `source.json` binds all 4,205 tracked source blobs and links to the producer; `results.json` records each exact command, exit, duration and log hash. `finished.json` records no failed gates and unchanged source. No completed gate was rerun merely to write this report.

| Check | Executed result |
| --- | --- |
| Release restore/build | Passed |
| Full unfiltered .NET suite | 40,208 passed; twelve normal opt-in cases skipped; no failures; matching complete output in `/tmp/umpk-test.log` |
| Count gate | All seventeen suite totals match, including 26,736 Client, 6,401 Protocol and 53 Integration cases |
| Release allocation/performance and conformance | Passed in the full suite; evidence-update switches disabled; all 892 Conformance cases executed |
| Full format verification | Passed |
| Canonical datasets and language | Passed |
| Python | 356 extraction and 23 official-tool/RCON tests passed |
| Native reconstruction | All fifteen retained collision, clearance, lifecycle, bed, wake, traversal and context checks passed |
| Fresh generation | All 61 generated files match exactly |
| Source integrity | All 4,205 tracked blobs and links unchanged after repository, consumer, AOT and package checks |

These are 25 recorded commands: restore, build, full-test, test-counts, format, dataset, language, python, official-tools, fifteen reconstruction checks and generate. `trx-summary.json` binds all seventeen actual full-suite TRX files and their executed counts. The twelve normal opt-in skips do not count as live or AOT acceptance.

`runs/interaction-boundary-packages-20261022-v1/results.json` records Release packing without rebuilding, both commit properties pinned to this producer, package verification and isolated fresh-cache consumer restore/build/run. All fourteen primary and thirteen symbol packages pass verification. All thirteen packaged library DLLs match the tested producer; the package consumer constructs protocols 47 and 777 and matches those thirteen libraries. `proof.json` records package hashes and the separate cache. These are local package checks; no packages were published.

## DMCBK, MCC and Native AOT

`runs/interaction-boundary-consumers-20261022-v1` records fresh DMCBK 1,999 and MCC 918 passing tests, all 33 dependency DLL comparisons and unchanged tracked/untracked sibling source and Git snapshots. External local-project targets select the immutable producer. The frozen MCC output is `client/Mcc.Cli.dll`; `client-manifest.json` binds all 576 files, UMPK producer `5481118`, DMCBK `af22688e783fb2876ad55f06e0654a7a4c6c5641` and MCC `ea4d8f011608ad993808afa0e53ded40c0ac3f80`.

The accepted consumer run retains the documented external DMCBK pose-fixture correction from the [preceding checkpoint](interaction-checkpoint-20261021.md#dmcbk-and-mcc-consumers). It receives a real full chunk before the existing synthetic terrain setup. All fourteen focused pose cases and the full suite pass. Original assertions and counts remain unchanged; sibling files are untouched. `fixture-overrides.json` and `.diff` bind the original/replacement sources. The original seven fixture failures and adapter compile failure remain recorded under their original runs.

`runs/interaction-boundary-aot-20261022-v1` records a fresh Release `linux-x64` MinimalBot publish after the repository and consumer prerequisites pass. The binary is **25,062,672 bytes**, below the unchanged strict **25,165,824-byte** limit. SHA-256 is `ada7be6aa840ddc4e45877d1dc9ffc6b4e710d7e630020d1d8532dfb7afb2255`. The `AotPublishSmoke` TRX contains exactly one total, executed and passing test, with zero failures or skips.

Validation `cross-check-certificate.json`, SHA-256 `cfa251ac123c5398053ad6a6f77a972cdeeee06287c4c138466fca99b08bfd22`, verifies all sixteen own producer DLLs remain unchanged, including the meta-package; all fifteen consumer producer DLLs, all 33 consumer comparisons, all 22 locked runtime DLL/PDB artifacts, packaged/consumer libraries and the complete frozen MCC manifest match. It also rechecks all source entries, full-suite TRX hashes, canonical full log and sibling source/Git snapshots. No source/test rerun substitutes for these hash checks.

## Native acceptance in progress

The strict native driver lock is validation `producer-lock.json`, SHA-256 `1d83ee12b8c3f19a9f3dd05ff82f48fe66293d8b68d0285c975e53ae1b33d7fa`. It requires the actual immutable archive, successful Release receipt, all 26,736 Client passes and exactly 22 frozen runtime DLL/PDB artifacts. The six compiled harness source names and producer hashes are checked before native execution. Missing or mismatched prerequisites fail before launching a server.

The dedicated axe/motion legs are committed in this producer. Their normal Integration discovery deliberately increases from 51 to 53 cases; both are opt-in. Nightly discovery enumerates nine axe and nine motion rows, rather than claiming those rows executed. The harness integration separately passes 41 normal Integration cases, preserves twelve normal opt-in skips and passes all 23 official-tool tests. The [live acceptance report](interaction-live-acceptance.md) records the bounded scenarios, source hashes, physics settings, packet/capture guards and native replay requirements.

The first exact-producer axe attempt, `runs/interaction-boundary-axe-live-20261022-v1`, executes one 1.12.2 case and fails its independent block-check assertion. Native `testforblock` succeeds, but its response uses spaces between coordinates and omits the older trailing period. The shared harness expects the older exact format. This is a harness failure before packet-group/replay acceptance, not a demonstrated axe production mismatch. Eleven loaded runtime DLL comparisons pass; source, seeds, siblings and frozen binaries are unchanged. The owned server/launcher exit zero and both ports close. The failed TRX, capture, `failed.json` and manifest remain intact; a narrow source-backed harness correction and fresh attempt are pending. The other eight axe rows and all nine motion rows have not executed at this checkpoint.

The later accepted axe campaign, `runs/interaction-boundary-axe-live-20261022-v3`, passes all nine genuine per-leg TRXs with zero skips: 1.12.2/340, 1.13/393, 1.17/755, 1.20.4/765, 1.20.6/766, 1.21.1/767, 1.21.5/770, 1.21.10/773 and 26.2/776. It verifies 81 actions, 2,539 exact-codec frame replays, fifty aggregate durability increments and all 99 DLL comparisons. The largest capture is 4,337,357 bytes under the unchanged 20 MiB cap. Independent native block/stack/durability and random-tick queries pass with physics enabled. Every native child and launcher exits zero without forced cleanup, every peer observes disconnect, and all nine native PIDs/eighteen ports are independently absent/closed.

This accepted campaign uses one declared test-only overlay: the exact `testforblock` success response changes at protocol 210. All 24 pre-flattening original server language resources establish that boundary. The command, requested state/metadata, coordinates and exact equality assertion stay intact; the other five compiled harness sources and all production code match `5481118`. All six Portable PDB document checksums match their compiled sources. The corrected helper is now retained in the repository. V1 remains the native response-format harness failure; v2 remains a prerequisite guard failure with zero tests/servers. Neither is relabeled as a pass.

`runs/axe-native-acceptance-delivery-20261022-v2/manifest.json`, SHA-256 `3e46bce2c3c647cfcea6d3aaf6a978c713779aa7dafd548b72909f9476f1b037`, binds the nineteen delivery files, exact patch, native resource audit, build/discovery/format logs and PDB proof. The accepted live manifest SHA-256 is `8e6c6c5b609903dcd4910c4baca89aac1798c7c7e4024c9537dac167961766e3`. Root independently verifies all 676 native files, nineteen delivery files, all nine actual TRXs, 99 runtime comparisons and all 24 original language resources in `runs/axe-native-root-review-20261022-v2/review.json`. Its first read-only review incorrectly expected numeric version order from a lexically sorted result index; that failed review remains separate, and exact unique release/protocol coverage stays required.

The first motion pilot at 770 reaches an unloaded native fixture region, so its floor/air preparation fails before capture/actions. It claims no trajectory result. Producer/seed/sibling invariants, normal peer shutdown and strict PID/port cleanup pass. A private harness correction will establish actual native chunk availability and assert setup command success before retrying 770/777; client full-chunk, physics and trajectory assertions remain unchanged. Protocol 777 and the other seven rows have not executed at this stage.

The controlled protocol-47 constructor/moving live comparisons in the [falling-block report](protocol47-falling-blocks.md) remain valid for their separately recorded producer overlays. They reproduce the old missing swing and pass with owned bounds. They do not constitute a fresh complete 75-release run on `5481118`. The earlier basic-matrix 1.8.4 failure remains historical under `e65329e`.

## Original 26.2 client network witness

`runs/piston-native-client-network-26.2-20261022-v1` executes the untouched official 26.2 client entry point against an isolated vanilla server. The fixture reconstructs the retained piston coordinates/materials in a fresh world because the original saved world was not retained. A private virtual display, software OpenGL and null audio permit the offline native client to run without using the desktop session. All 68 libraries and 5,057 official asset objects are hash-verified; existing verified files are reused. No system package is installed. The native client closes normally with exit zero, the server/launcher/display exit zero, the peer observes disconnect, all three ports close, and seed files remain unchanged. Mining is not exercised.

The native server itself logs `ParityBot moved wrongly!`. Its recorded stream has 793,118 bytes, SHA-256 `39c9bc074432e8103067ee0965ce686bfe826c2062c5314a993a1f80ff7a4003`. The separate offline audit accounts for all 1,547 framed packets and asserts decoded fields plus byte-identical re-encoding for 958 selected packets through the exact producer's real bound codecs. These include 580 section updates, 197 block updates, all movement/correction/acknowledgment packets, 148 client-tick delimiters and three received RCON markers. Eight framing controls pass. All 22 loaded DLL/PDB files match the accepted lock.

The native piston event at `(4,100,8)` precedes an actual correction by **100.410402 ms**. The native client first publishes `(5.810000011920929,100,8.5)`, then acknowledges teleport 4 to `(6.310000011920929,100,8.5)`. `runs/piston-native-client-network-offline-audit-20261022-v1/AUDIT.md`, `timeline-v2.json` and `receipt.json` retain the exact packet values, arrival order and clock limits. Its manifest SHA-256 is `5b11ce08eafc98ec7b929959ee6fc6f58520ac79e088e08679568d196f71d6df`.

At the original witness checkpoint, this closes only the missing original-client network observation; the fresh exact-producer MCC comparison remains pending. Proxy arrival timestamps do not establish native main-thread handling instants. No upstream attribution, warning suppression, tolerance change or final piston-parity claim follows from that witness alone.

The subsequent [frozen-MCC comparison](piston-26.2-attribution.md) closes attribution for the reconstructed fixture. Its fresh fourth run passes the strict driver and cleanup, accounts for all 736 framed packets and verifies 413 selected field/byte round trips. The same 27 RCON commands produce eleven identical event-relative packet payloads/values across four tick intervals, including the first X5.810000011920929 publication, X6.310000011920929 correction and the same vanilla warning. Real physics, the full local chunk and 64 post-setup/pre-event client ticks are observed. Original guard, setup and half-close failures remain separate. This demonstrates matching behavior for the recorded case, with no production change or upstream-bug claim; it does not reconstruct unretained world contents or prove all piston layouts.

## Publication and remaining work

Published head `6ba71b4` passes all seven CI jobs in run `39776487495`, covering the tested producer and native axe response-template correction. `runs/axe-native-publication-20261022-v1/completed-ci.json` binds that actual head. Later follow-ups require their own published-head CI result. PR #10 stays open and draft.

Remaining work includes the fresh motion live legs, remaining permission/component and specialized placement/fluid families, broader contextual picking, pose/scale/authority and other moving entity families, local/legacy gaps, riders/pushes and final fifty-protocol/seventy-five-release acceptance. Private scaling experiments are not included in this producer or its passing totals. The full objective remains incomplete.
