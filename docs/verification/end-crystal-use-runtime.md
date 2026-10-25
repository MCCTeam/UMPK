# End-crystal use prediction runtime

The scoped runtime now predicts the default end-crystal item's native result and swing policy when the required world and entity context is known. The connected caller preserves packet-only behavior for unknown context. This report covers source-backed item-use prediction and framed session tests; it does not report a live Minecraft server or original client-method execution.

## Producer and scope

The completed job is `/home/anon/Projects/MinecraftOfficial/runs/end-crystal-focused-runtime-20261025-v6`. Its immutable handoff is `handoff/producer-binding.json` (SHA-256 `f57ddbc06dc85cc02747f98bdf07a9f67146a789390d313c14058140d58fbb83`), and the independent root audit is `/home/anon/Projects/UMPK/.scratch/end-crystal86-actual-root-audit-v6.json` (SHA-256 `fabaff9d603f6e05c79bcfba8baa8a4add7582d0cd1d8d78ff1f839e2515dd84`). This private producer starts from actual Client `cd505f53` and Data.Java `26075707`, then adds the three end-crystal production files. It excludes the separately validated Shulker and proposed Wolf changes. Exact compiler source maps identify the producer; the retained source-root revision metadata is not a current Git HEAD build claim.

| Library or assembly | DLL SHA-256 | PDB SHA-256 |
|---|---|---|
| Baseline Client | `cd505f535bace3da6cc6d6789caa68630411c50a009477db7159284f61d7bfe9` | `68d76bc877d73ca9fdb6a2790f6d28dd9624508ca6e2f67a3df83d9a9d0510bb` |
| Final scoped Client | `5bb75ec0bf0cc5c9e6b912df05e6567def054d2c4b91eeed2d6f105a35f6aed1` | `16a6ec7066faab365ee170554aa16ce08531842ed9797300c1b34deac0e5a9ff` |
| Same test assembly at every stage | `8b250bb5f546f9fca13d4dfdf122951d03ce5cf20c93a92f898fe614b2d53bf7` | `37f61fb180aefc6b2e5afcbfe15d48116fed9e4464d82f224961e9a6110caab1` |
| Data.Java | `2607570748fa96468a9eededfa4e24714f434bb5eebf64ee034b48109840610a` | `0bb321a407392b31ebe5b2e714a63f8764be6a2dd9ca44a8dc52d8038e844ce4` |
| Protocol.Java | `39e7decd912f2e578b5347defdeda9ef16a0d1d5b4dc4ecba963a17f2b0ee755` | `e7a56d0cd9aa7019574cd4dda16b38da28c5675ce757c06411a6e507cb702ce5` |
| Physics, before the separate Shulker change | `9937d26df5b7c7b1127849b7102a3bf6144ef7e39dd9d5cc717ce7d00b513702` | `990d3c5ac3361ba031e5dd4351a0b0dd4d159698c230f715b50d80111e81adad` |

Tests compiled once against the baseline Client and Data.Java. The same test DLL/PDB ran against the baseline, each disconnected production-file stage, and the final caller. Data.Java, Protocol.Java, Physics and all other dependency pairs remained unchanged. The authored implementation adds no public signature, dependency, DI lifetime, package version, dataset value or feature axis.

## Native source coverage

The retained source binding covers **75 named releases and 50 protocol datasets**: 65 named-release rows have the item, while the ten protocol-47 release rows do not. The retained protocol-47 runtime profile remains based on 1.8.9. The 49 item-supported protocol representatives produce 49 catalog cases; the protocol-47 absence assertion is a separate case. These source counts are not live-run counts.

`native-source-bindings.json` is `/home/anon/Projects/MinecraftOfficial/runs/end-crystal-use-source-preparation-20261025-v1/native-source-bindings.json` (SHA-256 `8c21cfaad570edcee2a1c806afeb5bc9517adb4bd478dca3b033b3e713eb8d45`). Its 1,119 pinned native inputs are indexed by `/home/anon/Projects/MinecraftOfficial/runs/end-crystal-use-source-preparation-20261025-v1/native-source-inputs.json` (SHA-256 `a4bc873657818ba6d82cb47402df32020cf8b4a0401c99d4af29ed30903c4583`). Existing official artifacts and source were reused. This slice launched no JVM, original game method or Minecraft server.

The implementation keeps the measured era boundaries: protocol 107 starts item/offhand support; protocols107–340 allow the two air-or-replaceable cells and retain the older Adventure failure phase; protocol 393 changes to one required air cell and the default ItemStack Adventure pass phase; protocol 755 starts the modeled section-storage query; protocol 759 changes section-search arithmetic; protocol 766 requires care with effective components; protocol 777 uses the existing local-only successful swing policy. The ordinary scenarios do not constitute a deep 758/759 section-edge witness.

The entity check models the original null-source body query and its non-spectator predicate. Permanently nonbuilding entities, no-physics actors and shared vehicles are not skipped by substituting ordinary placement or hard-collision predicates. An item or XP orb with unowned current bounds therefore remains unknown, including a packet coordinate that appears far away. Legacy local-player chunk-list membership is not inferred from remote-player evidence.

## Executed acceptance

| Stage | Executed | Passed | Expected native-result failures | Meaning |
|---|---:|---:|---:|---|
| Baseline | 86 | 11 | 75 | Every failure is at the retained native-result assertion; all 49 catalog failures are present. |
| Profile file only | 86 | 11 | 75 | Exact baseline failed-identity Counter retained. |
| Query file added | 86 | 11 | 75 | Exact baseline failed-identity Counter retained. |
| Production caller connected | 86 | 86 | 0 | Use/result/swing, acknowledgment and peer-disconnect assertions execute. |
| Adjacent regressions | 648 | 648 | 0 | Exact retained 592 plus actual 56 XP-picking identities; zero skipped cases. |

The 86 cases consist of 49 catalog cases, 19 clearance/permission/missing-context cases, 13 body-query cases, two legacy local-membership cases, two outside-build-height cases, and one protocol-47 item-absence assertion. Catalog cases exercise obsidian and bedrock with both hands. The fixture keeps physics enabled for geometry claims and uses the session's controlled tick source. The tests assert real bound-codec decoded values and byte-identical reencoding, the exact use/swing group, cursor and hand values, modern sequence acknowledgment, unchanged observed inventory/block state, and peer-observed EOF.

Before execution, the fixture was audited against the actual frozen `Support/BlockUseSession.cs` (SHA-256 `a6651d281c3eedaa2d1529bbb42432ee453b4979c4273b0b186b60402fc440df`). Two setup block writes in the unload and respawn branches needed an explicit keepalive application fence before immediate reads. Only those two bounded barriers were added. The other mutation paths already fence through the helper or the first prediction barrier. There are no nonempty synthetic entity-metadata entries and no default serializer-hint versus decoded wire-ID equality in this fixture. The audit is `/home/anon/Projects/UMPK/.scratch/end-crystal-focused-binding-preparation-20261025-v6/fixture-ordering-audit.json` (SHA-256 `cd07417af7222207bb518e020f8cb81f320f1f50280017f68f9017ce4a9372c9`).

The test compiler closure is 327 user inputs including the SDK program, plus three generated inputs, 330 checked PDB documents and 11 UMPK references. The Client stages use 212, 213 and 213 user inputs plus three generated inputs, with 215, 216 and 216 PDB documents and nine UMPK references. All **977** PDB documents across the four builds match their source bytes. Compiler and runtime Data.Java are the same frozen `26075707` pair.

## Commands and retained receipts

The exact bounded invocation was:

```text
systemd-run --user --unit=umpk-end-crystal-focused-runtime-20261025-v6.service --slice=app.slice --property=Type=exec --property=RemainAfterExit=yes --property=MemoryMax=4294967296 --property=MemorySwapMax=0 --property=CPUQuota=200% --property=LimitCORE=0 --property=RuntimeMaxSec=900 --property=KillMode=control-group --property=TimeoutStopSec=20 --property=WorkingDirectory=/home/anon/Projects/MinecraftOfficial/runs/end-crystal-focused-runtime-20261025-v6 /usr/bin/python3 /home/anon/Projects/MinecraftOfficial/runs/end-crystal-focused-runtime-20261025-v6/run.py --input-manifest-sha256 385c0b5c61f0fbcdeb38e00960fa895ee243ef6e1955630666feb2bfb27e82cc --root-grant-sha256 9b9f9c6c09e349eaaca08bea9864de0a44ae4dd7a7dabe516d9e3b04a4e6cf1c
```

Every child command's complete argv, exit status, output pin and resource-sample pin remains in `commands/*.json` under the job. The compact command index is `/home/anon/Projects/UMPK/.scratch/end-crystal-additive-overlap-20261025-v1/crystal-child-command-receipts.json` (SHA-256 `a5ec6ef8660afb773e58fa1b6651a070cbb782bf8d2a034d3aa64f266c2a36e2`). It records one Release test compilation, three Release Client compilations, their PDB inspectors, four exact 86 focused runs, and the exact 648 adjacent run. Existing 60-file/12-project restore metadata was reused with its original `ProjectUniqueName` paths. No dependency restore/build, full suite, Native AOT publish or server leg was repeated.

The whole job completed in 87.870 seconds under retained 4 GiB memory, 200% CPU, zero swap, zero core dumps, a 900-second deadline and single-processor Csc limits. Memory peak was 1699151872 bytes. Cleanup receipt `/home/anon/Projects/MinecraftOfficial/runs/end-crystal-focused-runtime-20261025-v6/cleanup.json` (SHA-256 `31221d85d17b045cf977292f442773f7a86690ec624a2d5f7a493297f8835a10`) proves all 36 observed processes absent. No Minecraft server or RCON port was owned by this in-memory session job.

## Remaining acceptance

The four authored files passed the later joint formatter and normal implicit-project integration check recorded below. Additive integration must preserve the accepted Shulker Physics pair and owner changes; the older end-crystal Client DLL must not replace that whole producer. The future Wolf Placement changes are separate ordinary-obstruction hunks and must retain the earlier crystal-item dispatch. One final combined full/count/AOT/live/consumer/repository gate remains required after that source tree is frozen.

This result does not prove live crystal/block/stack outcomes or an original native client-method call. Required dedicated native/live item-use boundaries remain open. Unsupported rider, multipart/oversized body, deep section-edge, effective component/Adventure predicate and legacy local-membership contexts retain explicit unknowns. The predictor does not simulate server crystal creation, dragon effects or the native client's temporary stack transition; inventory remains with its existing server-observed owner. Full-suite counts and AOT size for the final composite producer are not inferred from these focused results.

## Joint normal integration

The later joint Shulker/crystal normal integration passes against the `c2586776e565b1bc01b5d75d162bad28aaeb33c2` archive plus the exact 17 Shulker and four crystal postimages. This preserves the new Shulker Physics behavior while adding crystal-item prediction. All 20 authored C# formatting scopes have zero delta; the 21-file delivery also contains the Physics project file. Normal implicit source discovery and source/reference/PDB guards pass for Physics, Client and Client tests. The four exact checks pass 28 Shulker cases, 86 crystal-item cases, 2,447 Shulker-adjacent cases and 648 crystal-adjacent cases: 3,153 distinct identities after the shared 56 XP-picker cases are counted once.

The actual normal Client pair is DLL `aec5523dd34c653054b3fc3dfd1825b5abeb59cd07aed173dcdc884adebd09c1` and PDB `18a365563bee4b6b822d80fdc4f8d4bc5e260d1862e243a4b0d9045c0bd0e14d`; the Physics pair is DLL `7862ef86b26cdb5e6f63b8552d58b4fa832cee4ae3c0856844c03894221bc77c` and PDB `eb1e59e76a9002ffdc45a542e90dba75500209f5883c32967a9cac84e57cc613`. The proof is `/home/anon/Projects/MinecraftOfficial/runs/shulker-normal-integration-20261025-v1/handoff/producer-binding.json`, SHA-256 `b40a0cda648399646b4eda7b7a53ea61416b59f05171bb05ab19942d0b6298e6`. This bounded check does not certify final combined full-suite, Native AOT, live-server or consumer acceptance.

Independent root audit `db67212d90c3ace5e4b03a7ef07f53109cde68c357da3852816d8b3911a76a94` verifies all 4,333 frozen source files, 574 PDB documents, compiler references, twenty-one shared postimages, exact scenario counters and fifty-four absent owned PIDs.
