# Filled jukebox activation

## Current acknowledgment defect

A later source audit finds a defect in the published 759–761 projection. Native `BlockStatePredictionHandler` retains the current local state, a separate server state and the last mutation sequence. A server block update before acknowledgment changes only the retained server state. Acknowledgment without any server update restores the originally captured state. Full-chunk replacement overwrites the current state but leaves the pending prediction available for later acknowledgment. The published projection clears on an early block update and does not restore on acknowledgment. The common prediction owner is being connected through the session callers; this lifecycle is unfinished.

The 206 passing cases below remain historical execution results. `BlockUseSession.Use` automatically acknowledges 759+ before returning, so the earlier 761 expectation of an empty jukebox after that helper does not prove the native pending interval and must be corrected. The source-only bindings cover 29 named releases and nineteen datasets, 759–777, in `runs/sequenced-block-prediction-preparation-20261024-v1/REPORT.json` (`a1a124ef8bcd851ef6c17a1bdaa18b16c1742d1a42e0fe2f8e72b42b7f5fc473`). A new framed session run against the unchanged `392b107` Client executes all 258 cases: 45 expected behavior failures and 213 passing controls, with no skips. Independent root review verifies the exact identities, eleven compiler references, twelve PDB documents and unchanged source inputs in `runs/sequenced-block-prediction-red-recovery-20261024-v1/root-independent-functional-red.json` (`379bbd932e7abcf39fd7ef44cf28d3053d5bb09eb3f03371f79bd58a8274d55b`). The original run remains failed overall because its sealer mistakenly froze a generated compiler receipt as an input; successful compilation rewrote that receipt with the two corrected import source hashes. This is the sole mismatch among 1,170 paths. The recovery audit reruns no tests and changes no old receipt. The original-method witness has reached native chunk bootstrap but has no accepted observation rows; its harness failures remain separate from the C# behavior failures. Production GREEN, native acceptance, modern 762–777 instance/cache context and empty insertion remain unfinished.

## Behavior and boundary

UMPK now predicts filled-jukebox activation and the following repeated use through the existing session-owned `PlaceBlockAsync` caller. The bounded native rule covers 34 protocol datasets, 47–761, and fifty named releases through 1.19.3. Protocol 47 retains the 1.8.9 client profile. The source inventory also covers the remaining sixteen datasets and twenty-five names through 26.3; these newer branches require native block-entity instance context and remain unknown. Empty-jukebox insertion is outside this slice.

The earlier code sent use without the native successful activation swing. Returning success alone also failed: vanilla clears `has_record` locally, so a subsequent click must use the locally empty state until an authoritative observation replaces it. The new `ClientState.JukeboxPrediction` owns this projection separately from the received world, inventory, published events and server-observed use outcomes. Prediction queries remain read-only. The action commits the projection after the use packet writes successfully and before the remaining captured-session swing; cancellation before writing cannot consume it.

Single and batched block updates, full/bulk columns and touched partial sections invalidate the relevant projection before publishing their events, even when the received state ID is unchanged. An unrelated block update or untouched partial section preserves it. Unload removes affected entries. Same-world respawn preserves the projection; world replacement, reconfiguration and disconnect clear it. World identity and observed state identity remain explicit. The received world snapshot can remain available after disconnect while its local interaction projection is cleared.

The helper uses existing `List<BlockPos>` and `List<int>` representations. It reserves both capacities before appending and removes aligned entries together. Lookup is linear; no throughput improvement is claimed. Public signatures, dependencies, package versions, DI lifetimes, generated data and pin fixtures are unchanged.

## Source and producer identity

All evidence paths below are under `/home/anon/Projects/MinecraftOfficial/runs/`. Raw official source, binaries, logs and captures remain outside Git.

| Evidence | Location | SHA-256 |
| --- | --- | --- |
| Seventy-five-release source bindings, 713 input pins | `jukebox-activation-preparation-20261024-v1/native-source-bindings.json` | `31f3e539b3898167637676dbbd5df6766e94959bdbdf04a34102258e2df4d90b` |
| Modern native-instance context boundary | `jukebox-activation-preparation-20261024-v1/modern-instance-context.json` | `a0eff757bdd4da02e8404cd53fba9359a4f6873fedfcf8384483cfcd6b59b750` |
| Nine reviewed postimages against `39f0ef15d863d9ef4368d54b7429dac2dc6bbbd7` | `jukebox-final-integration-20261024-v2/REPORT.json` | `99a7a718c7d29096f1a0c0fddc40dee4ff7448958bd242a12789e56131511ef7` |
| Producer and source origins | `jukebox-final-integration-20261024-v2/source-origins.json` | `a1d5856c6d954954fae51e23f3adab131082e4ab958484c95955d6898f336aff` |
| Final runtime/native completion | `jukebox-runtime-compaction-20261024-v1/completion.json` | `5a28e0caf5b29a43f8580eafa6e32f77c6063f869d5765d78103daae5a183804` |
| Actual unit and owned-process cleanup | `jukebox-runtime-compaction-20261024-v1/cleanup.json` | `d647ad54d1fb5f715a3f4d083de762e4477bdb6e7e6ce5785be1fc90f622c927` |

The private producer uses the completed immutable `8efa26f` dependencies, the six previously tested Client production overlays, and these five jukebox production postimages. This is an explicitly bound development producer, not a fresh full-repository or consumer certificate for the later Git head. Actual compiler references, source inputs, portable-PDB source identities and all eleven native compiler library references are pinned in the completion. The tested Client DLL is `7f07d4a59922492d56b01b2cf65bbf90d856e0c002de21fe1a0a120e24b2cee7`; its PDB is `571b05ca7d6ead9dad817743057b741eb350b30736ffaf6416000e24ecc5d712`.

The delivery report's prose says projection commit follows the complete use/swing group. The reviewed implementation and tests place it after the successful use write, before the captured-session swing. The immutable delivery receipt is preserved; this report records the actual ordering.

## Executed runtime and Native AOT checks

The focused inventory contains 206 real-session cases: 179 activation/context cases, eleven repeated-use cases and sixteen lifetime cases, including the protocol-47 bulk-column observation. It covers selected hands, all fifteen positive legacy metadata aliases, game modes, sneak/cooldown/item continuation, missing terrain, pure repeated queries, authoritative rearming, chunk observations, retained/replaced worlds, disconnect and same-client reconnect. Decoded values and byte-identical bound-codec round trips remain asserted. Teardown checks peer-observed EOF and the cleared projection directly.

| Phase | Actual result |
| --- | --- |
| Original focused red | 206 executed: 117 expected failures and 89 passing controls |
| Stateless activation only | 179 passes and 27 expected repeated-use/lifetime failures |
| Projection helper, state owner and world-observation changes | The focused inventory reruns after each production-file change; the remaining 27 cases fail until the public action commits the projection |
| Corrected final runtime | All 206 pass, with zero skips |
| Original adjacent inventory | All 6,593 identities across thirty existing classes pass, with their original multiplicities |
| Source and formatting | Seven previously checked PDBs/1,089 documents reused unchanged; the compacted Client adds a fresh 214-document PDB check; scoped formatting passes |
| Strict Native AOT | 25,157,376 bytes, strictly below 25,165,824; 8,448-byte margin |
| Native smoke | Exactly one test executes and passes; a skipped or undiscovered test is rejected |

The final native executable is `jukebox-runtime-compaction-20261024-v1/publish/MinimalBot`, SHA-256 `b34b90c9a66f5b0a1d684731a2f0698b69f26315f1636fcb68c8b28abade2ac0`. The actual eleven native library references match the tested producer and the nine-package dependency graph is unchanged. Publishing preserves the tested and protected producer outputs. Replacing only the projection dictionary with the existing paired-list representation reduces the preceding executable by 12,560 bytes; this whole-binary comparison does not establish the cost of every jukebox feature or a CPU performance claim.

The exact argv, working directory, environment, exit code and output hashes are retained in `commands/{green,adjacent-green,publish-native,native-smoke,scoped-whitespace,pdb-source-documents}.json` below the final job. `HANDOVER.json`, SHA-256 `2d880e70080620052d499fa027afa3e1e1f3a54da75ffa87481ef5868599174d`, records the exact launch. It runs `/usr/bin/python3 /home/anon/Projects/MinecraftOfficial/runs/jukebox-runtime-compaction-20261024-v1/run.py --input-manifest-sha256 7da32c5b6cba0915c0994892218c3ae88ebaf3e7cbad5ca2489494519d67802e` in a retained user unit limited to 3 GiB, 200% CPU, 1,800 seconds and no core dumps. The actual unit exits zero; peak memory is 1,869,131,776 bytes. All twenty observed owned PIDs are absent after cleanup. Seventy-four samples report no OOM event; the empty final cgroup no longer exposes final counters, which are recorded as unavailable.

The deliberate addition is 206 Client tests: 34,171 becomes 34,377. Integration remains 59. The seventeen-suite expected total becomes 48,164; this is a baseline calculation, not a new full-suite execution claim.

## Post-integration check

The nine actual shared source postimages match the reviewed delivery. A separate source-bound reuse of the proved Client/test binaries executes the same 206 focused identities again: all pass, with zero skips and no rebuild. `jukebox-post-integration-focused-20261024-v1/completion.json` has SHA-256 `6a6cb45841934b911142c79de39d884817de277878b66a3ede84c3f063341a8b`; cleanup is `86444cf8ba800af8e5eee708b68fcadde7bddbacc7f1634d87d9b3cab21ea4f8`. Actual unit exit is zero, peak memory is 424,448,000 bytes and all four observed PIDs are absent. The six resource samples report no OOM; final memory counters are unavailable after cgroup removal.

Independent root review rehashes the 158-file final evidence manifest and all 713 original native input pins, parses the actual passing XML and compares all nine shared source hashes. Its durable `root-actual-review.json` has SHA-256 `fcb125d5cdfb766ec1b8dda06e7164a0cd3e0a8a360a04ff5870b8a76dabaf54`. The earlier critical runtime/AOT review is retained durably in the same job, with unchanged SHA-256 `28bf21acd88748b9eb70a42819970767baed9a68fa3a169ed3bf237f91840acb`. This verifies the focused integrated source/binary binding; it does not turn the reuse into a new shared-checkout build or final compatibility certificate.

## Preserved failed attempts

`jukebox-runtime-combined-20261024-v2` retains the valid 117-red/89-control and stateless phases, followed by the helper's missing registry-namespace compile failure. `jukebox-runtime-combined-resume-20261024-v1` fixes only that import and retains its final 202 passes/four fixture failures. The fixture correction preserves the received world on disconnect, uses the monotonic session tick and supplies the protocol-761 resource-key respawn without inventing an unreceived numeric registry.

`jukebox-runtime-fixture-continuation-20261024-v1` passes all 206 focused/6,593 adjacent cases, source checks and formatting, then fails the unchanged native size guard at 25,169,936 bytes. Its smoke does not run. These failed attempts remain failed. The separate helper-only compaction reruns the affected focused/adjacent, PDB, format, native publish and executed-smoke checks; their passing result is indexed above.

## Evidence retention

After the successful jobs, sixteen historical `/tmp` input paths disappeared for an unknown reason. Actual Oracle source, compiled outputs, TRX files and native artifacts remain available. `jukebox-retention-addendum-20261024-v1/REPORT.json`, SHA-256 `5e6f7c29f6d6b25391a6bbe1f2c48e093744d5409a4b4f4b344780b32865b2ed`, records the loss: three exact byte sequences were recovered and thirteen unique historical serializations remain unavailable. All 2,544 still-present original frozen inputs rehash correctly. This does not establish a current complete unchanged 2,560-file retention check. Old paths and receipts were not recreated or rewritten.

The related native-lectern retention addendum is `lectern-retention-addendum-20261024-v1/retention-addendum.json`, SHA-256 `1af5967215075be190f0ae14628f82cbd101d55a1178817f6955ad82b845d64d`. Of eighty-two missing historical temporary inputs, twenty-five exact entries were recovered and fifty-seven remain unavailable; actual native captures, runtime binaries, source and results remain. A new lectern source audit has its own hash, `d9dea8892b56b85656ddf69c145ab5610fda4a2588ce28c4e4486e9b6653dfe4`, and is not presented as recovery of the lost original audit. All new jobs copy their consumed temporary inputs into durable Oracle directories before sealing.

## Remaining acceptance

Native-server jukebox acceptance, including independent record/state observations and repeated use, has not run. Empty/insertion and protocol-762–777 native-instance context remain unknown. The final committed producer still requires the complete repository, native/version matrix, consumer, package, strict-AOT and published-head CI gates. This bounded runtime slice does not close those requirements or the overall interaction objective.
