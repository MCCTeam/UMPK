# Modern slab placement and merging

Default slab placement and matching single-slab merges now use the existing session-owned placement predictor on all 39 modern profiles, protocols 393–777. The finite source inventory covers 101 registered slab names, 1,924 item/profile rows and 51 named releases. Legacy slab placement remains Unknown because its mutable block-bounds precheck needs separate native evidence. This report covers runtime result/send/swing decisions; native server acceptance remains open.

The internal slab profile admits only the exact source-backed names whose existing Unknown item contract names the matching placed block. It resolves clicked replacement, adjacent fallback and the resulting top, bottom or double collision shape. Collision reads use the canonical collision lookup rather than outline shapes. The protocol-777 collision references are supplied by the separate [collision correction](slab-777-collision.md).

The existing owner preserves activation, hand, cooldown, Adventure, enabled-feature and component ordering. Obstruction is a post-send Fail on these modern profiles. Success uses a packet swing through 776 and local-only swing on 777; Unknown sends the existing packet without inventing a swing. Missing native terrain, body/membership/physics, arbitrary component patches and unsupported item families stay Unknown. Observed blocks and inventory remain server-owned.

Native cursor comparison uses float hit offsets on 393–404 and absolute double hit-position subtraction from 477. Matching slabs can merge at the clicked cell or an adjacent destination; different or double slabs retain their native replacement behavior. Waterlogging does not replace the selected collision shape. A generalized internal collision overload accepts the selected boxes while retaining the existing native epsilon/grid rules and entity predicates. The existing whole predictor becomes internal for typed owner-thread tests; no alternate controller, test-only production state or public API is added.

## Source and runtime evidence

The private producer is an exact archive of `e6e755df2d02a4d70de31c8cf217adb07d2e7bb0` plus five reviewed files: three production and two test files. Evidence is retained at `/tmp/umpk-slab-placement-runtime-20261023-fm_w37vh`. The review patch SHA-256 is `7600a88d1db957b757284dbf6c62e3a6576eb7696bf8ec790fd52ad340193234`; the 102-file delivery manifest is `caff44bd385047c33742efd674c174014209bc99fb810671472a4da90cb68ca8`.

`source-bindings/native-source-bindings.json` retains complete original method bodies, source paths/lines, artifact and decompile receipts; `runtime-index.json` maps the production decisions to those roles. `modern-source-proof.json` pins all 727 actual native inputs and reconciles every admitted item/profile row. No new Java, native measurement or Minecraft server ran for this source-backed implementation.

| Check | Executed result |
| --- | --- |
| Frozen-base public caller red | 38 expected failures and one protocol-777 packet-only control |
| Frozen-base typed 777 red | Six expected result failures and two Unknown controls; only predictor visibility changes, with no slab behavior overlay |
| Final slab cases | 2,344 passed, zero skipped |
| Adjacent placement/activation/tool/collision/lifetime cases | 6,703 passed, zero skipped |
| Exact-final full Client suite | 32,574 passed, zero skipped |
| Final scoped format | Zero changes; final diagnostic verification has no warning/error |
| Separate AOT with the final three production files | 25,132,640 bytes, strictly below 25,165,824; exactly one executed passing smoke |
| Source/binary proof | All five final source documents match tested PDBs; normal and AOT PE/PDB identities match; all 24 final normal runtime DLL/PDB files remain unchanged through the full test run |

The 2,344 additions exercise every admitted item/profile row plus merge, half-shape obstruction, water, hand/activation, Unknown, teardown and cursor boundaries. Cursor expectations are literal source-backed truth data rather than another copy of the production formula. Tests observe typed Result, SendPacket and Swing independently on the session owner, then call public `PlaceBlockAsync`, assert exact packet fields/order, absence of Punch, ACK identity and completion, and unchanged observed world/inventory. Bound incoming frames assert decoded values and byte-identical round trips. The one outgoing use packet on 777 alone is explicitly not result/swing proof.

The separate AOT binary SHA-256 is `e21235c7cbdf7372d29a4a380a8d7e1ca67196e526084550687c0d9d8e5bcfed`. All 138 normal DLL/PDB paths protected during that early publish/smoke remained unchanged then. Later intentional expanded test builds have separate snapshots; that historical invariant is not presented as equality to every final test binary. The final three production hashes still match the early AOT overlay.

Initial restore/import/IPC attempts remain harness failures, separate from valid red evidence. Four expanded-run failures exposed missing legacy Respawn fields in the new test only; the repair sends and round-trips the era-correct packet without changing expected production results or shared defaults. The intermediate full run and the exact-final rerun after the cursor truth-data review both pass all 32,574. Commands, environment, exits, durations, raw logs, TRX, source and binary manifests remain outside Git.

## Integration and limits

Root applied the exact five-file patch after documentation-only `6365be9` and independently passed all 2,344 focused cases with SDK 10.0.401. The deliberate Client baseline increases from 30,230 to 32,574; the other sixteen suites are unchanged. Root source/hash/XML checks and command receipts are retained under `runs/modern-slab-root-integration-20261023-v1` in the shared oracle. Original user report additions and logs remain unstaged.

This bounded runtime pass does not close legacy slab prechecks, custom component/permission contexts, broader entity/rider geometry, native slab server outcomes or final exact-producer acceptance. Fresh merged repository, consumer, package, strict AOT and published-head CI checks remain required. The [completion ledger](interaction-completion.md) keeps those requirements open.
