# Sheep and villager collision classification on 1.11

The protocol-315 ambient movement owner previously treated any sheep or villager as an unsupported hard collision participant. That discarded a tracked squid’s current geometry and world membership, including when the peer was far from the placement. The next public placement request then retained an Unknown obstruction result and omitted its predicted swing. Original 1.11 collision getters return null for these two families through their registry-bound inheritance chains. The existing owner now uses that classification before deciding whether a peer makes movement uncertain.

This change applies only within the existing protocol-315 movement profile. It preserves placement obstruction and picking as separate predicates. Boats and unaudited peers retain the conservative failure path. Removing or replacing such a peer does not restore an already lost squid position. No public API, dataset, dependency, package version, DI lifetime or clock changes.

## Executed runtime proof

The fourteen new tests send real bound spawn, metadata, terrain and interaction packets through a session with physics enabled. Sheep and villager cases cover near and far peers, current bounds and chunk membership, and the public stone-on-iron-door placement’s exact use/swing sequence. Boat, unaudited-zombie and lost-position controls preserve the previous uncertainty. Packet decoded values and byte round trips remain asserted.

| Stage | Actual result |
| --- | --- |
| Unchanged production | All fourteen execute: eight expected failures and six passing controls; zero skips. Four failures lose geometry, and four receive the use packet without the required swing. |
| Classification helper alone | The same eight identities fail; six controls pass. This proves the production caller must use the helper. |
| Connected caller | All fourteen pass, with no skips. |
| Existing regressions | All 2,137 original selected identities pass. These cover ambient movement, placement, doors, membership and entity lifetimes. |
| Source and references | All 211 actual Client compiler sources match the reviewed producer and two overlays. Five PDBs verify 452 documents; actual Client/test/Native AOT references are retained. |
| Formatting | A separate exact three-file `dotnet format whitespace --folder --verify-no-changes` passes. The original continuation’s earlier formatting pass covered only the test; that scope remains explicit. |
| Native AOT | ELF size 25,157,376 bytes, strictly below 25,165,824 by 8,448 bytes. Exactly one smoke test executes and passes. All eleven actual ILC references and tested producer hashes match. |
| Cleanup | The runtime exits successfully; all 34 observed owned PIDs are absent. The separate formatting run exits successfully and its two observed PIDs are absent. No native server starts in either run. |

The deliberate Client baseline increases from 34,641 to 34,655 for these fourteen additions. The resulting seventeen-suite total is expected to be 48,442; a fresh complete run on the combined producer remains required. The tested Client DLL is `62ef39c0aea1bdec4a2ccf179fecbf93923ab2cd62b6edbc1d4ae13ddf2ab270`, with PDB `55eaae93b1e594dc9f56a0bafb8e0dd1147adce2f1cdbac40c060353e93fddd0`. Its source is the exact `392b107` Client plus the two reviewed production files, using the retained `8efa26f` dependencies. This scoped proof is separate from a full repository, package or consumer certificate.

## Evidence and commands

Retained evidence lives under `/home/anon/Projects/MinecraftOfficial/runs/`. The source audit is `legacy315-null-hard-peer-preparation-20261024-v2/native-source-audit.json` (`3df348d0a6b997683d0cb17e6bdeb32af645fe7da17e547cb310c558dbe8ae4f`). It binds the original registry, sheep/villager/squid inheritance chains, null getters and world collider collection. Native hard-box nonparticipation does not imply placement or picking nonparticipation.

The actual runtime is `legacy315-null-hard-peer-runtime-import-continuation-20261024-v1`. Its `HANDOVER.json` (`dbf40658633bb8227542f49c7c416470271eded8a46e9e32da67fb413830a236`) records the exact retained `systemd-run` launch, input seal and pinned .NET 10.0.401 SDK. `commands/*.json` retains every restore, compiler, test, publish and smoke argv with its actual exit and log hash. The unit uses `Type=exec`, 3 GiB, 200% CPU, a twenty-minute deadline and disabled core dumps. The prior missing-import compiler failure is preserved; only the import changes in the continuation, and its completed twelve-project restore metadata is reused.

`legacy315-null-hard-peer-final-delivery-20261024-v1/REPORT.json` indexes the postimages and original evidence. Independent root audit `root-independent-runtime-audit.json` is `73be68f68c5b7e9a04ab1a37c240b5c90110953571b69763273f8fe7784a7eda`; shared-source correspondence `root-shared-integration.json` is `efbca47f6dcb3f6e58f7a55757da2de81e667c926cada8f9445dd28a5d4278b9`. The additive three-file formatting receipt is `format-three-execution/completion.json` (`0c76c3362521d65a75c1daac8e1eb5c3e27fc9e2158f20e102db7369ef4f0b5c`). The runtime cleanup is `8a696ff8193aaf52093b262d53879f09633e16a654b7f45f5b3207199468ce69`. Successful tests and AOT are reused through exact source/binary correspondence; no extra post-integration test execution is claimed.

## Native acceptance still required

The retained whole 1.11 door test remains failed. Its corrected native position/on-ground setup passes, followed by eight partial scenarios; the ninth places stone successfully but misses the required swing. The new classification fixes a source-proven contributor to that result. A fresh run against the tested new Client is still required to establish whether the complete eleven-scenario row passes. Its original failure and partial results are not rewritten.

The retained squid-context audit (`door315-retained-squid-context-20261024-v1/REPORT.json`, `5401a827e9879e3ce8dac09fcb85a8bb0e91ed0a8ee8452aff10371e1959e504`) finds water in the relevant loaded query cells. `IsDry` applies to bats; the squid path permits liquids with empty collision shapes. This rules out the earlier dry-water explanation without proving the first actual runtime invalidation branch. Final native acceptance, combined repository gates, consumers and the full release matrix remain open.
