# Legacy ordinary-block placement precheck

Ordinary cube placement now rejects a known native obstruction before sending on protocols 47, 107, 108, 109, 110, 210, 315, 316, 335, 338 and 340. Protocol 393 remains the adjacent post-send control. This bounded change covers the legacy ItemBlock controller decision; specialized items, uncertain context and complete native/live acceptance remain open. Protocol 47 keeps its existing 1.8.9 client profile.

The existing session-owned placement predictor runs the precheck after block activation and before cooldown or Adventure item use. A known false replacement, self collision or eligible entity obstruction returns the existing pre-send rejection result. A successful precheck continues normal item use. A failure predicted after sending keeps its separate meaning. No public signature, dependency, version, DI lifetime, dataset or generated pin changes.

Snow-layer prechecks keep the clicked cell, while actual use resolves the original face and destination separately. Activation and spectator behavior, secondary use, unavailable legacy offhand and custom-component uncertainty retain their native ordering. Packet-established terrain is required for a known precheck. Missing terrain/body information and out-of-height prechecks remain packet-only Unknown; these tests do not establish native result or swing parity for every failed height-boundary write.

## Source and private runtime proof

Evidence is retained under `/home/anon/Projects/MinecraftOfficial/runs/legacy-cube-presend-runtime-20261023-v1`. Its immutable base is `2925b5180ce25b3fd50666ce569f4720be9934be`. The runtime patch SHA-256 is `fe9d8cbe8f82c6f6220b643e7d9941425d9862ab403c6232867d3d8d934f3988`; the ten-file source manifest is `538e3e19b1adb7606695ad5e755909ed04f99cd9c1e824d6c315408c0a665ddf`; the 108-file delivery manifest is `862c04422314bd01ddeb2693333b70f48013c6d6f43a14c2b4dc51596d01ea30`. `completion.md`, `final-proof.json` and `state-final-tested.json` carry final results; the earlier source-backing draft retains its historical pending wording.

The retained original controller, ItemBlock, world and input bindings cover 24 named legacy releases and the adjacent 1.13 caller. All 99 selected source hashes, 75 role bindings and selected official artifact/decompile receipts were independently checked. Legacy controllers perform activation, then the ItemBlock precheck, before creating the use packet; 1.13 performs the relevant item use after sending. No new native experiment was needed for this source-backed decision.

| Check | Executed private result |
| --- | --- |
| Valid focused red | 63 expected failures and 110 passing controls |
| Final real-session focused cases | 173 passed, zero skipped |
| Adjacent placement/lifecycle cases | 2,945 passed, zero skipped |
| Full unfiltered Client suite | 30,230 passed, zero skipped |
| Scoped format | Ten owned files, zero changes |
| Native AOT | 25,132,640 bytes, below the unchanged strict 25,165,824-byte limit |
| AOT smoke | Exactly one executed passing test |
| Invariance | All 24 normally tested DLL/PDB hashes, 22 runtime copies and sibling source/Git snapshots preserved |

Session tests drive public placement through actual bound codecs, asserted decoded values and byte-identical round trips. Existing storage-only fixtures explicitly receive retained original full chunks where known terrain is required; their shared default behavior is unchanged. Existing known-obstruction assertions become stronger zero-send assertions on the applicable legacy profiles. Independent PDB document checks bind all ten final source files to the tested binaries; the separate AOT Client PDB binds the same two production files. The private AOT binary SHA-256 is `a19d466cd21b318fb321f312fd096afcc85f21fe023c24f917820d8c93f63d94`.

Invalid initial metadata/analyzer attempts, the first adjacent 169 failures and first full Client 28 failures remain retained. Fixture repairs account for native no-send expectations and explicit terrain. One first-full failure measured the unchanged reconnect backoff at 399 ms against its existing 400 ms minimum; neither that test nor its production code changed. The subsequent complete Client run passed. Failed attempts are not red/green substitutes.

## Shared-branch integration

The exact reviewed runtime patch was applied to `1548788d954ab8e85fa0d9d06c3a75154a436c11`, together with the directly affected maintained Basic live fixture patch `84139775e84a30fbf0b16e0894742e01ee684d9cb9a219b9a36a488d95e5c7bd`. That fixture now requires zero use/swing frames for an occupied destination below 393 and retains the exact single-use assertion on 393+. Independent native block and stack assertions, capture cap/tail, replay, disconnect and cleanup are unchanged. This changes no live scenario count.

Root independently executed `dotnet test tests/Umpk.Client.Tests/Umpk.Client.Tests.csproj -c Release --filter FullyQualifiedName~LegacyCubePreSendSessionTests`: all 173 passed. Normal `dotnet test tests/Umpk.IntegrationTests/Umpk.IntegrationTests.csproj -c Release` compiled the maintained change and passed 41 tests with sixteen named opt-in skips, 57 total. The deliberate Client baseline increases by 173, from 30,057 to 30,230; Integration remains 57. SDK 10.0.401 is the retained local toolchain. Exact root logs and TRX are indexed by the integration receipt under `runs/legacy-cube-presend-root-integration-20261023-v1`; the first sandbox attempt failed before testing because MSBuild could not create its pipe and is retained separately.

Full combined repository, consumer, package, AOT, published-head CI and native/live checks require a fresh immutable combined producer. These focused results do not close specialized placement, the protocol-315 ambient bat/squid geometry failure, slab merge rules or the final fifty-protocol/seventy-five-release matrix. Existing user report additions and untracked logs remain unstaged.
