# Native tick control and held mining

Verified on 2026-10-17 with UMPK `82a8d9f69de29eb504f0073c40fc03a1afaef810`, DMCBK `af22688e783fb2876ad55f06e0654a7a4c6c5641` and MCC `ea4d8f011608ad993808afa0e53ded40c0ac3f80`.

## Behavior

Protocols 765–777 now decode ticking-state and ticking-step packets. The level owns rate, freeze and granted-step state; level replacement, configuration reentry and session teardown reset it. Same-level respawn retains it. Packets received without a level have no effect, including when terrain tracking is disabled.

The default session clock uses vanilla's float accumulator, fractional carry and ten-tick frame catch-up limit. Slow world rates slow player input and held mining. Rates above 20 TPS retain the native 20 TPS client cap. Frozen worlds retain 20 TPS player input; granted steps temporarily use the world's rate. Host-provided game-tick sources retain their existing ownership. There is one default frame timer.

World-tick borders and piston block entities advance only on eligible world ticks. Remote players remain exempt from world freeze, as in vanilla. Millisecond border interpolation through protocol 773 keeps its existing clock.

## Native evidence

The committed `tests/Umpk.Protocol.Java.Tests/Fixtures/TickControl.native.json` contains deduplicated original packet, listener, manager and timer measurements for 21 releases from 1.20.3 through 26.3. There are 92 state/step cases and 20 timer scenarios per release. Original official jar SHA-1/SHA-256, raw measurement hashes, helper sources, original class digests and recorded native class origins were verified for every release. The original runner disposed its temporary unsigned jars, so their archive hashes were not recomputed in this audit.

These are original method measurements with controlled world/thread inputs and bypassed level, listener and Minecraft constructors. They do not run a complete graphical client, complete native ticks or every entity-freeze predicate. Sources and receipts remain under `/home/anon/Projects/MinecraftOfficial/runs/tick-state-clock-all-20261016-v2`.

## Live before and after

The pre-fix MCC snapshot used UMPK `eb52b8d`. At a declared 5 TPS it produced 49 client ticks and 55 bounded swings in about 2.4 seconds. Bare-hand dirt sent START and FINISH 0.699839 seconds apart, although its 15 native progress updates require 14 intervals, or 2.8 seconds at 5 TPS. On 1.21.11, both the old and fixed runs produced exactly 14 tick-end markers between START and FINISH; the old cadence was too fast. The baseline also left world-tick borders unchanged during normal ticks and granted steps. The baseline has no harness error and all owned processes and ports were cleaned up.

Four fresh fixed runs use normal MCC `/dig`, `/dig stop` and view commands on isolated offline vanilla servers. A temporary read-only plugin samples the public session tick, border and inventory state on the session owner. Each leg checks 20 TPS, 5 TPS, the 20 TPS client cap at 40 TPS, freeze, exactly three independently observed world steps, continued frozen input and resume. Empty-hand obsidian remains intact; a subsequent dirt attack completes and an independent server block read confirms air. Captures verify swing cadence (the handless punch packet in 26.3) and, where available, one swing per complete tick-end group. Marker edges have an explicit eight-swing tolerance; the atomic client-tick samples use a three-tick tolerance. Dirt START-to-FINISH timing has a 0.35-second tolerance.

| Version | Protocol | Border clock | Client ticks in slow sample (~2.4 s) | Dirt START to FINISH | Result |
| --- | --- | --- | --- | --- | --- |
| 1.20.3 | 765 | Milliseconds | 12 | 2.800734 s | pass |
| 1.20.5 | 766 | Milliseconds | 12 | 2.800144 s | pass |
| 1.21.11 | 774 | WorldTicks | 12 | 2.799956 s | pass |
| 26.3 | 777 | WorldTicks | 12 | 2.799997 s | pass |

Counter-border legs 774 and 777 advance once per eligible world tick, stop while frozen and advance exactly three ticks when granted. Server game time independently advances exactly three ticks in every leg. Every owned client/server exits with code 0; all recorded PIDs are absent and all eight ports close. No scenario movement warnings occur.

All 33 consumer DLL comparisons match the tested producers. Actual MCC process mappings confirm the expected UMPK Client library. Selected live controls, swings, mining actions, movement and tick-end frames decode with frame-exactness and reencode byte-identically: 3,066 selected frames from 4,679 play frames. Captures and exact commands remain under `/home/anon/Projects/MinecraftOfficial/runs/tick-mining-live-fixed-20261017-v1` for the first three releases and `/home/anon/Projects/MinecraftOfficial/runs/tick-mining-live-fixed-20261017-v3` for the final 26.3 leg; consumer hashes and commands remain under `/home/anon/Projects/MinecraftOfficial/runs/tick-control-consumers-20261017-v1`.

## Repository checks

The focused run passes 2,436 client, 943 protocol/allocation and 54 corpus/cross-layout cases. The exact committed full run reports 30,809 passed, seven opt-in skips and the pre-existing `VanillaCitationTests.EveryCitation_ResolvesInTheDecompiledTrees` count-guard failure at line 138. All 17 suite totals match the deliberate baseline. Test and count commands exit 1; neither gate is reported as passed. The citation resolution assertion passes before the count guard fails. The earlier intermittent sound-name allocation failure did not recur in these runs; its cause is still unproven.

Release rebuild has zero warnings/errors. Full format, dataset, language and generated-output checks pass; all 61 generated files reproduce exactly. Native AOT publish and smoke pass at 24,510,000 bytes against the unchanged 25,165,824-byte budget. DMCBK passes 1,999 tests and MCC passes 918. Forty conformance pin files were regenerated through their environment-gated commands and all 160 changed lines reviewed. Two tick-control allocation rows measure 24 B with 32 B limits; all 70 existing rows remain unchanged. Single-layout tick controls remain outside the multi-layout witness catalogue; four corpus-body tests cover both boundary protocols without weakening the different-neighbor guard.

The first sandboxed full-build attempt exited without diagnostics. A provenance-audit setup error assumed mappings existed for unobfuscated 26.x releases; the corrected audit checks mappings only when the official metadata advertises them. The first standalone capture-decoder build used an internal test helper; it now uses the public frame-exact Decode API. The first 26.3 live attempt completed its actions but the checker incorrectly expected the older FINISH ordinal 2; the capture contained the correct native ordinal 3. The checker now derives ordinals from each release's original Action enum. A second 26.3 run passed its clock and completion checks but its capture audit counted the removed swing packet instead of its replacement punch packet. The final fresh run derives both expectations from matching vanilla source. No production changes were needed for either harness error. Failed attempts are retained in the evidence.

## Remaining scope

This proves the listed vanilla versions and scenarios. It does not establish all protocol representatives, public-server behavior, customized mining rules, dynamic mob geometry, scaling, riders, native mob interpolation or specialized placement. [Classic 1.8 geometry and placement](classic-entity-placement.md) now have runtime integration and separate acceptance runs. Shulker phase measurements still need runtime integration and live validation.

The evidence manifest is `/home/anon/Projects/MinecraftOfficial/runs/tick-control-proof-20261017-v1/proof.json` (SHA-256 `989cc3a31b6e59d1f96f8e10f2e324529ac284625ade5c0e9608597c8f219d21`). Raw logs, probes, binaries, captures and official artifacts remain outside Git.
