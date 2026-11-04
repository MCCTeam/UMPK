# Ordinary cube terrain availability

Ordinary cube placement now requires packet-established destination terrain before it can predict a replacement or successful placement. A block update can create public storage in a column that the native chunk cache has not received. That stored AIR or STONE, and the effective getter's valid default VOID_AIR, cannot establish terrain availability. The existing placement owner now checks destination membership for the modern prediction path as well as the legacy pre-send path.

The production change is one condition in `InteractionActions.Placement.PredictCubePlacement`: destination storage and packet-established membership must both exist before destination replacement and clearance queries. Missing membership returns the existing conservative Unknown/USE-only behavior. The change preserves the public placement contract, known received AIR and VOID_AIR, legacy pre-send policy, activation and permission owners, collision queries, ACK ownership and received world/stack state.

## Source boundary

The retained native-source audit covers 1.14.4, 1.16, 1.17, 1.19, 1.20.5, 1.21.5 and 26.3, protocols 498, 735, 755, 759, 766, 770 and 777. It separates the missing-cache getter, the empty-chunk write result and the original BlockItem placement path. The source-derived native missing-cache write outcome is Fail; the library keeps Unknown because its terrain context is unavailable. This correction does not claim those outcomes are identical.

Source inventory and class/caller bindings are retained in `.scratch/ordinary-cube-destination-membership-source-20261104-v1/SOURCE-AUDIT.json` (`87baa6b53a49816623ff0af7f00501ca4838c7299d889076b1bd6e24d05e07ac`) and its reviewed proposal (`c8cdf99bf8b5f0374365183f94f4714038249d35d728dcb93e97f378724aa1f3`). No original native generic-cube missing-cache getter/write/place experiment ran for this slice. Early flattened datasets without named source proof remain explicit native-acceptance gaps; the existing caller's conservative availability guard does not manufacture proof for those eras.

## Connected runtime acceptance

Ninety-six framed session cases exercise WEST and NORTH destination edges at modern boundaries 498/735/755/759/766/770/777, with twelve legacy controls at 47/315/340. The six modern contexts are synthesized AIR, synthesized STONE, unloaded-then-synthesized AIR, received AIR, received VOID_AIR and received STONE. Every case establishes public destination storage before comparing packet membership and the effective getter. The tests preserve exact prediction and USE/swing assertions, await the completed public action after applicable ACK and retain the received world/stack state.

| Stage | Executed result |
|---|---|
| Baseline cube tests | 54 passes and 42 intended prediction failures, zero skips |
| Missing destination membership before correction | 36 false Success/packet-swing predictions and six false Success/local-swing predictions |
| Corrected final caller | All 96 cube cases pass within the 353-case joint focused run |
| Adjacent regression | All 3,885 pass |
| One unfiltered final Client suite | All 38,861 pass, zero skips; exact old 38,508 plus ladder 257 plus cube 96 name multiplicities |

The baseline causal assertions require conservative Unknown with no predicted swing; the old producer incorrectly predicted Success. These are production decision failures after established fixture preconditions. The preceding ladder fixture setup failure and its one-barrier correction are preserved separately; no cube result or assertion changed to make the run pass.

## Producer and evidence

The actual build used admitted source-equivalent commit `0eb1cd30b4a09e2a157c9ded99e25a238c35cae7` plus the ordered ladder and cube overlays at `/home/anon/Projects/MinecraftOfficial/runs/ladder257-cube96-managed353-20261104-v3/source`. It compiled one test assembly and three Client stages using external SDK 10.0.403 with NuGet audit enabled and actual Csc processor count one. It retained the other twelve producer libraries and all twenty frozen references; it did not compile Data.Java, Protocol, TestKit or another producer.

The exact final combined DLL/PDB/XML are retained under `/home/anon/Projects/MinecraftOfficial/runs/ladder257-cube96-managed353-20261104-v3/stage-producers/cube/`: `0e7dbe7de53e9e67f9805f28a18d53e820d962ce4cc9eb0b8ce495a78e3e0c2e`, `2ba8324605816233001c926166080eccd8fdf49f24e06d5ac143356c6e3c5a1d` and `0726022beacb1a46c484436ae53cf301562190346cefd8d4046fbc417ac7eae6`. Portable PDB checks match all 233 Client and 383 test compiler documents. XML stayed unchanged between the intermediate ladder and final cube stages and differs from the admitted baseline XML `600f8945…`.

The ladder-only intermediate passed its focused 257 cases. Adding those cases gives an expected baseline of 38,765; adding these 96 gives 38,861. There was one final combined full suite and no standalone full 38,765 run. The full counter comparison preserves every old name multiplicity and the exact 353 new cases. Three documented corpus-path display keys account for 127 unchanged fixture occurrences; source/corpus hashes remain pinned.

`.scratch/ladder257-cube96-managed-preparation-20261104-v3/ACTUAL-MANAGED353.json` (`ad6b64405480dddeeed6ec4ead90c448e51288e4db8ded200031b2cdb44f53cd`) indexes every exact argv/log/TRX/source/PDB/reference/format pin. The completed command transcript is `/home/anon/Projects/MinecraftOfficial/runs/ladder257-cube96-managed353-20261104-v3/completion.json`; raw evidence stays outside Git. `PROMOTION.json` separates the exact ladder-only and final caller bytes for the two code intents.

Cleanup `0da7737abe06d2f66c99dbc57d70bc949dcc402414d8cbede09d6898908065a7` records all 38 observed HOST identities absent, MainPID zero, original cgroup removed, and unchanged 4,469 source files, twenty references, 39 frozen producer inputs and 136 final runtime files. Only Client DLL/PDB/XML changed in the runtime. The job took 425.98 seconds, peaked at 3,668,824,064 bytes under the unchanged 4 GiB/200% CPU/no-swap/no-core cap and recorded zero memory max/OOM events.

## Remaining acceptance

An original native ordinary-cube missing-cache getter/write/place witness, including known full-chunk AIR and VOID_AIR controls, remains unexecuted. Dedicated live availability cases and the final 50 general/75 basic matrix have not run for this producer. Source-equivalent final thirteen-library admission, full seventeen-suite repository gates, fresh consumers, packages, strict AOT and published-head CI remain separate requirements. The ladder live-40 pack is also unexecuted. See [the completion ledger](interaction-completion.md).
