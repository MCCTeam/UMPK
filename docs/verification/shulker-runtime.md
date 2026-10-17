# Modern Shulker geometry

The client tracks Shulker lid bounds from attach-face and peek metadata on protocols 770–777. Placement prediction reads the current bounds. The geometry owner uses the existing session tick and honors loaded columns, frozen worlds and granted steps. Mount callbacks preserve exact passenger coordinates, reset the attachment face and apply the native pose refresh. After the first eligible tick, block-coordinate changes reset the peek target. Shulkers ignore spawn, motion and teleport velocity updates; direct teleport velocity updates preserve remote players' pending motion targets.

The measured releases are 1.21.5, 1.21.6, 1.21.7, 1.21.8, 1.21.9, 1.21.10, 1.21.11, 26.1, 26.1.1, 26.1.2, 26.2 and 26.3. The native geometry fixture contains 90 scenarios per release, including all six attachment faces, five scales and three positions. The edge fixture contains 16 scenarios per release, including sleeping poses, scale limits, mounts, vehicle transfers, signed peek values and ignored velocity updates.

| Check | Observed result |
|---|---|
| Native geometry inputs | 1,080 scenarios; original helper and measurement hashes verified |
| Native edge inputs | 192 scenarios; original client classes verified and mount-event outputs recorded |
| Connected geometry regression | All 1,080 cases initially failed because geometry and snapping were absent; the connected cases now pass |
| Connected edge regression | Velocity updates initially changed immovable entities; the transfer regression also exposed a stale peek watcher after a pose refresh |
| Session-loop tests | Eight protocol cases cover loaded columns, freeze/step behavior, removal and entity-ID reuse |
| Placement tests | 64 cases cover closed, opening, distant, differently attached and scaled Shulkers |
| Release build | Zero warnings and errors |
| Full Client suite | 20,620 passed |
| Full solution suite | 33,941 passed, zero failures, seven opt-in skips after the citation-check correction |
| Test-count gate | All 17 suite totals match |
| Format, dataset and language checks | Passed |
| Generated-output comparison | All 61 generated files match the checkout byte for byte |
| DMCBK and MCC consumers | 1,999 and 918 tests passed; 33 binary comparisons match the exact producer |
| Native AOT | Publish and fake-server smoke passed; the native executable is 24,534,656 bytes against a 25,165,824-byte budget |
| Live vanilla matrix | 120 stationary placement scenarios passed across protocols 770–777 |
| Live missing-swing baseline | The previous client placed nine blocks without swings in 15 scenarios; the fixed 1.21.5 client sent the nine expected swings |
| Captured packet checks | 17,356 byte-identical round trips across the eight fixed live legs; another 2,160 on the baseline |

The Client baseline increases by 2,424 from the committed 18,196: 1,080 standalone geometry cases, 1,080 connected cases, 192 edge cases, 64 placement cases and eight session-loop cases. The three remote-player teleport regressions found by the first full run were reproduced separately and fixed by separating direct velocity assignment from motion-target updates.

Use `/home/anon/Projects/MinecraftOfficial` as the evidence root on this workstation. The original geometry inputs are in `runs/shulker-geometry-all-20261016-v2`; the final edge inputs and preserved helper sources are in `runs/shulker-runtime-edges-20261017-v5`. Their `results.json` hash is `85f50ea8a92660a5bc5d5ee929ceba568a9d003a6fba50743b61f1e4f9239463`. The edge fixture hash is `503a306dec37521fa8c46e69795135badd695d2dc1644e3fc1adf1e9c149b117`. The source audit hash is `139f5f810fac927135b548c78899f181433b583a5e2df74a793f1729ee4359eb`.

`runs/shulker-runtime-validation-20261017-v2/results.json` records the exact commands, exit codes, durations and log hashes for SDK 10.0.401. Its `source.json` records the source hashes, and `generated-comparison.json` records the 61 equal outputs. `runs/shulker-runtime-proof-20261017-v1` preserves the native audits and regression evidence. An independent comparison verified all 65,880 operation states in the original geometry fixture with exact float and double bits.

The exact runtime producer is commit `32f4ac1a99d8ec0a0d1cd9a6e8613019376d2dff`. All 4,007 tracked files and links in its isolated checkout match Git. `runs/shulker-runtime-producer-20261017-v1` records its restore, build, 20,620 Client tests and 15 producer DLL hashes. `runs/shulker-runtime-consumers-20261017-v1` records the external project references, test commands, binary comparisons and immutable MCC client. Those consumer sources use DMCBK commit `af22688e783fb2876ad55f06e0654a7a4c6c5641` and MCC commit `ea4d8f011608ad993808afa0e53ded40c0ac3f80`, including their existing local changes. No sibling source commits were created. The AOT proof is in `runs/shulker-runtime-aot-20261017-v1`; its executable SHA-256 is `c4e805d468120a0dbdde09dcaef65c4d668c87702d18b70c6aa1368d87593db9`.

The live pilot is in `runs/shulker-placement-fixed-20261017-v1` for 1.21.5. `runs/shulker-placement-matrix-20261017-v1` covers 1.21.6, 1.21.7, 1.21.9, 1.21.11, 26.1, 26.2 and 26.3. Each leg tests closed and open Shulkers on all six attachment faces, distant controls and a half-scale open Shulker. Independent server queries verify the actor position, attachment, peek, obstruction, destination block and selected stack count. Captures verify spawn IDs, lid transitions, attachment metadata, actor removals, use/swing order and byte-identical packet round trips. Protocols 770–776 send nine swings for accepted placements and none for six obstructed placements; 777 sends no placement swing under its native policy. Every leg verifies peer disconnect, owned process exit and both ports closing. Alias releases were measured in native probes but were not run as separate live servers.

`runs/shulker-placement-baseline-20261017-v1` uses the prior immutable MCC client built from UMPK commit `6098caa`. It reproduces the missing nine swings while the same 15 native block and stack outcomes remain correct. `runs/shulker-runtime-final-20261017-v1` preserves the completed capture audits, helper sources, source archive and receipts. `runs/citation-coverage-validation-20261017-v1` records the successful full checkout gates after the separate [citation-check correction](citation-coverage.md). The original guard failure and three velocity regressions remain recorded in the earlier runs.

The native probes execute constructors and individual metadata, position, scale, pose, mount and peek callbacks with controlled world inputs. They do not execute complete entity/world ticks or native rider positioning. The session tests exercise UMPK's actual client loop over framed packets. AOT validation covers publication and the fake-server smoke, not a native executable connected to vanilla. The live tests cover stationary stone placement; local-player lid pushes, general player/rider placement, specialized placement and the modern collision-aware interpolation connection remain unfinished.
