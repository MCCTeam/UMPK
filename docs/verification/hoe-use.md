# Native hoe-on-block prediction

The existing session-owned block-use sender now predicts the original `HoeItem` result on protocols 498–776. Successful use sends use-on followed by the selected hand's swing. A native pass sends use-on without inventing a swing. Terrain, drops and durability remain server-owned. This is the explicit block-use contract; it does not add the graphical client's automatic air-use or alternate-hand fallback.

## Behavior and caller

| Context | Predicted native result |
| --- | --- |
| Dirt, grass block, coarse dirt or the era's path block; non-bottom face and native air above | Success and packet swing |
| An ordinary target with a bottom-face click or a non-air block above | Pass without swing |
| Rooted dirt from protocol 755 | Success independently of face and the block above |
| Unavailable required block context | Unknown, retaining packet-only behavior |
| Unclassified item era, Adventure permission or modern component patch | Unknown, retaining packet-only behavior |

`InteractionActions.PredictBlockUse` applies existing border, block activation, inventory and cooldown ordering before the tool profile. `VanillaHoeUse` owns the source-backed era and terrain rules. This native tool result does not depend on entity obstruction or a player collision box. Other placement families retain their existing component guard and geometry checks.

Original `HoeItem` ignores legacy NBT and durability when selecting its client result. Ordinary legacy tags and a modern Damage patch therefore do not suppress a second use. Other modern patches remain explicitly unclassified; this slice does not claim complete component/cooldown-group support. Protocols before 498 retain their previous unknown behavior. Protocol 777 replaces `HoeItem` with data-driven `BlockTransformer` components and is deliberately not inferred from 776. Those older and newer tool eras remain open.

The later [terrain-context regression](hoe-terrain-context.md) requires packet-established chunk availability and preserves unknown results after unload or a partial-only arrival. Its additional ten cases and producer identity are recorded separately from the original overlay results below.

## Source and focused runtime checks

The source audit verifies client jars, decompile receipts and relevant `HoeItem`, `Items`, `ItemStack`, `MultiPlayerGameMode` and `InteractionResult` hashes on all 43 named releases from 1.14.4 through 26.2, covering 31 protocol datasets. It checks the native transformation map, registered hoe materials, the rooted-dirt boundary, client/server branch and result forms. `native-source-audit.json` has SHA-256 `48a8ad949cb904ea8bba869013ac47906e249858faf6834b304a21bf20a092dc`. This is original client source evidence, not an executed native-client campaign.

The initial 634 packet-driven cases fail 442 successful-use assertions while all 192 negative/control cases pass. Adding the helper without its production caller retains those failures; connecting the sender passes all 634. Eight further worn-stack cases expose the universal component guard: eight fail while the prior 634 pass. A helper-only change retains those eight failures. Connecting the bounded legacy-NBT/Damage classification passes all 642. Inbound frames assert decoded values and byte-identical round trips through real bound codecs; outbound packets use the production bindings. Cases cover both hands, every registered hoe material, soil/face/cover boundaries, creative mode, cooldown/Adventure controls and adjacent unclassified eras.

All 2,397 focused/adjacent placement cases pass after formatting. The complete Client suite passes 23,366 tests without failures or skips. Normal Integration discovery passes 41 and skips nine, for a deliberate total of 50. The baselines add 642 Client cases and one opt-in theory row; skipped discovery is not live acceptance.

```bash
/home/anon/Projects/MinecraftOfficial/toolchains/dotnet-sdk-10.0.401/dotnet test tests/Umpk.Client.Tests/Umpk.Client.Tests.csproj -c Release --no-restore -m:2 -nodeReuse:false -p:SourceRevisionId=765a27561803927d85002f90b67d544a21f4c2c8 --filter FullyQualifiedName~HoeUsePredictionRuntimeTests
```

Commands run from `/tmp/umpk-hoe-interactions-20261019-v1/source`, an archive outside Git. The official SDK is `DOTNET_ROOT` and first on `PATH`. `checks.json`, the individual command receipts and TRX files retain full commands and hashes. Three scoped format commands pass. The overlay manifest verifies the four exact changed source files and 4,161 unchanged base blobs.

## Native-server acceptance

Three dedicated physics-enabled live legs execute and pass. The server independently verifies dirt/grass becoming farmland, coarse dirt becoming dirt and the tool's exact cumulative damage. Bottom-face and covered-dirt uses leave terrain and damage unchanged and emit no swing. The 1.17 and 26.2 legs also verify rooted dirt succeeds when clicked from below with stone above. Each action captures a further 2.1 seconds; relevant frames assert values and byte-identical replay under the unchanged 20 MiB cap.

| Release and protocol | Replayed relevant frames | Evidence under the shared oracle |
| --- | --- | --- |
| 1.14.4, 498 | 235 | `runs/hoe-interactions-live-20261019-v2/1.14.4-33980224045e44988bb0f650c4a5bd8c` |
| 1.17, 755 | 225 | `runs/hoe-interactions-live-20261019-v2/1.17-2eaeb300c33c43b9a0ea6e0733465367` |
| 26.2, 776 | 197 | `runs/hoe-interactions-live-20261019-v2/26.2-efae97490a4848ed9c3bd2fc5003bca5` |

The successful legs identify base `765a27561803927d85002f90b67d544a21f4c2c8` plus exact overlay manifest SHA-256 `a2a0ced2f74d9964a0f19402528cf40b2bd1851613bee445e900bd28a38660fd`. All eleven loaded UMPK libraries match the tested overlay in every leg: 33 comparisons, recorded in `runs/hoe-interactions-live-20261019-v2/binary-equality.json`. These are overlay-matched results, not immutable acceptance of a later Git commit. Producer DLLs remain unchanged. The TRX guard confirms exactly three executed passes with no skips; every server observes disconnect, exits without forced termination and closes both owned ports.

The first live attempt fails three harness assertions because the conditional RCON `say` command returns an empty response. All three server logs show the unique marker, proving the first durability predicate executed. The corrected witness waits for vanilla's exact `[Rcon] <marker>` server-log text after the same conditional command. It preserves the independent predicate, terrain, durability and packet assertions. Original failures and successful cleanup remain in `runs/hoe-interactions-live-20261019-v1`.

The live command filters `FullyQualifiedName~HoeUseMatchesNativeTerrainDurabilityAndSwing` with `UMPK_NIGHTLY=1`, explicit producer/overlay identity, the verified shared oracle, isolated evidence directories and protocol-appropriate Java runtimes. It uses `--no-build --no-restore`; the manifest identifies the actual loaded producer. Complete commands are retained in `live-v2-commands.json` and each leg's `producer.json`/`commands.json`.

Focused evidence, reviewed source, failed attempts, commands and the source audit are retained in `runs/hoe-interactions-focused-20261019-v1/manifest.json`, SHA-256 `646e8c9469bdb10ddf71c81b4a7c9ad2d01913bbddd80aaebb85bc65b8041d83`. Complete repository, consumer, package, Native AOT and CI acceptance of the new immutable producer remain pending. This slice does not close other tool/fluid families, all older tool eras, protocol 777 or the separate 1.9 falling-block placement defect.

The first immutable producer `2da7161cb799bffb27af25b1c6eddb33340f5224` passes 23 of 24 repository/reconstruction gates, including 36,822 .NET tests, nine opt-in skips, 356 Python tests, all 17 counts, 61 identical generated files and 4,166 unchanged source blobs. Full solution format verification fails only the live test file's import ordering; the earlier project-only format did not catch that rule. The consumer driver rejects the failed prerequisite before building siblings, so consumer/AOT/package stages are not passes. `runs/hoe-use-validation-20261019-v1` retains that failure. A fresh isolated full-solution formatter repairs only the import order; corrected immutable acceptance requires a new producer.

## Immutable checkpoint validation

Immutable producer `412e29d5fa1802205b80ed83661ca1634d9715b6` passes all 24 repository/reconstruction gates: 36,822 .NET passes, nine normal-suite opt-in skips, 356 Python passes, all 17 deliberate suite totals, 61 identical generated files and 4,166 unchanged archived source blobs. Its source archive is outside every Git checkout. The interrupted format attempt retained no exit receipt; the October 21 continuation verifies and reuses the four completed restore/build/full-test/count receipts, retains that interrupted log, and executes the remaining twenty gates. No interrupted or skipped required scenario is counted as passed.

Fresh DMCBK and MCC runs pass 1,999 and 918 tests. All 33 dependency DLL comparisons match that tested producer; sibling source hashes, HEADs and Git status remain unchanged. The MCC output is frozen with its complete file manifest. Native AOT publishes at 24,976,288 bytes, strictly below 25,165,824, with SHA-256 `60930f7cc3b534a0180eae5a139a13e02dc06e005d7c4dcae205a72b64c5d7c9`. The TRX guard confirms exactly one executed passing smoke test and no skips. Publishing leaves the tested producer DLLs unchanged. Fourteen primary and thirteen symbol packages pass verification; thirteen package libraries and thirteen fresh-cache consumer libraries match the producer.

Receipts are under `runs/hoe-use-{validation,consumers,aot,packages}-20261019-v2` in the shared oracle. Their original directory date is retained; `resume.json` records the later continuation. The previous `2da7161` v1 run retains its format failure and refused consumer prerequisite. These checks identify `412e29d`; they do not establish acceptance of the later terrain guard, legacy hoe extension or a later published head. The three physics-enabled hoe server legs above retain their exact earlier overlay identity.
