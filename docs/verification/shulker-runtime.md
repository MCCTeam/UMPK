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
| Full solution suite | 33,934 passed, one existing citation-count guard failure, seven opt-in skips |
| Test-count gate | All 17 suite totals match; the gate fails because the citation test failed |
| Format, dataset and language checks | Passed |
| Generated-output comparison | All 61 generated files match the checkout byte for byte |

The Client baseline increases by 2,424 from the committed 18,196: 1,080 standalone geometry cases, 1,080 connected cases, 192 edge cases, 64 placement cases and eight session-loop cases. The three remote-player teleport regressions found by the first full run were reproduced separately and fixed by separating direct velocity assignment from motion-target updates.

Use `/home/anon/Projects/MinecraftOfficial` as the evidence root on this workstation. The original geometry inputs are in `runs/shulker-geometry-all-20261016-v2`; the final edge inputs and preserved helper sources are in `runs/shulker-runtime-edges-20261017-v5`. Their `results.json` hash is `85f50ea8a92660a5bc5d5ee929ceba568a9d003a6fba50743b61f1e4f9239463`. The edge fixture hash is `503a306dec37521fa8c46e69795135badd695d2dc1644e3fc1adf1e9c149b117`. The source audit hash is `139f5f810fac927135b548c78899f181433b583a5e2df74a793f1729ee4359eb`.

`runs/shulker-runtime-validation-20261017-v2/results.json` records the exact commands, exit codes, durations and log hashes for SDK 10.0.401. Its `source.json` records the source hashes, and `generated-comparison.json` records the 61 equal outputs. `runs/shulker-runtime-proof-20261017-v1` preserves the native audits and regression evidence. An independent comparison verified all 65,880 operation states in the original geometry fixture with exact float and double bits.

The native probes execute constructors and individual metadata, position, scale, pose, mount and peek callbacks with controlled world inputs. They do not execute complete entity/world ticks or native rider positioning. The session tests exercise UMPK's actual client loop over framed packets. Fresh MCC consumer tests, live-server checks and AOT validation for this batch remain pending. Local-player lid pushes, general rider placement and the modern collision-aware interpolation connection also remain unfinished.
