# Creative mining through the client session

The eight cases in `CreativeMiningSessionRuntimeTests` prove that framed packets and the actual `UmpkClient` clock reach the creative-mining fix. They supplement the separate 335 owner-level cases in [creative mining restrictions](creative-mining-restrictions.md). The deliberate addition is eight cases here and 343 across both deliveries.

## Source and scope

The private source uses archive `f0a84e3e1c1ff01b828c44bb81ae2fd46d0d8b8c`, SHA-256 `6bcdf1c11e92da632a2de543cc2f8030682a8e1521bfd17afae774100aa3df98`, plus the unchanged two-file production overlay from `/home/anon/Projects/MinecraftOfficial/runs/creative-mining-delivery-20261022-v1`. That delivery manifest is `b82c649b509cb6950de564d4bf0b7489c8946643129b13f0a5c71dccf2e47ba8`. This test supplement adds one test file and this report. It changes no production file, dataset, generated output, public API, dependency, DI lifetime, baseline, or package version.

The test uses `FakeJavaServer` and the bound Java connection over an in-memory duplex transport. Login, configuration where applicable, join, position, chunk, ability, game-mode, player-info, inventory, tag, and acknowledgment frames pass through the real client dispatcher. The existing injectable `ITickSource` drives the actual session clock with physics enabled. The test neither constructs `ClientSessionServices` nor calls the mining loop or advances state directly. It does not establish live-server compatibility, original native client execution, TCP behavior, or server-owned world effects. The separate original-method native witness remains indexed by the creative-mining restrictions report.

## Assertions

| Protocol | Named native profile | Initial restricted creative packet group |
|---|---|---|
| 47 | 1.8.9 | swing, START, swing |
| 393 | 1.13 | START, swing, swing |
| 757 | 1.18.1 | START, swing, swing |
| 758 | 1.18.2 | START, swing, swing |
| 766 | 1.20.6 | START, swing, swing |
| 769 | 1.21.4 | START, swing, swing |
| 770 | 1.21.5 | START, swing, swing |
| 777 | 26.3 | START, punch, punch |

Each case requests one explicit `DigBlockAsync` on stone with a diamond sword. The server assigns a UUID different from the requested profile, and the own-player information frame uses that assigned identity. Assertions verify both identities and packet-populated creative/instabuild/equipment state. A real full chunk establishes terrain membership, floor updates let enabled physics settle, and component-era tag frames establish the pickaxe rules needed by the subsequent survival assertion.

The captured START has the exact target, face, action, and sequence shape. Every outbound frame is decoded through the actual bound codec and re-encoded byte for byte; incoming relevant frames assert decoded fields and byte-identical re-encoding. The full chunk also has structural round-trip assertions. This is runtime fixture coverage; it does not replace the original native serializer witnesses of the existing codecs.

The restricted attempt completes its captured packet group and preserves stone in observed state. Protocols with block-change acknowledgment keep the public task pending until the matching real ACK arrives; a client-loop invocation before ACK proves the wait does not hold the owner loop. A following idle tick sends no new mining group. A later explicit survival request with a diamond pickaxe emits STOP on tick ten and never earlier, proving the retained four-tick delay. The test then starts an unbreakable request, disconnects, and verifies request cancellation, cancellation of the clock, and peer-observed EOF. It does not infer a successful server block change from local completion.

## Validation and retained failures

All receipts live outside Git under `/home/anon/Projects/MinecraftOfficial/runs/creative-mining-session-20261022-v1`. The exact commands, working directories, source hashes, complete logs, and TRX files are retained there.

| Receipt | Executed result | Meaning |
|---|---|---|
| `session-identity-red` | 8 failed, zero skips | Original mining owner produces two packets where each native-backed assertion requires three; assigned identity and legacy login setup are correct. |
| `session-helper-only-red` | 8 failed, zero skips | Adding the unused restriction helper cannot close the runtime defect. |
| `session-green-attempt2` | 8 passed, zero skips | The unchanged frozen production overlay fixes all eight full-session cases. |
| `session-adjacent-green` | 148 passed, zero skips | The new cases and existing attack-input, cooldown-session, and core tick failure tests pass together. |
| `session-test-format` | exit zero; zero files changed | The new test passes scoped `dotnet format --verify-no-changes`. |
| `compiled-session-source-check` | three selected documents match | Both production overlay sources and the new test match the actual portable PDBs. |
| `compiled-session-all-source-check` | all 483 documents match | All 203 Client and 280 Client.Tests compiled documents match their current source hashes, including generated build documents. |
| `compiled-owner335-all-source-check` | all 483 documents match | Read-only audit also verifies every compiled source document from the separately frozen 335-case delivery. |

The Release focused command is `dotnet test tests/Umpk.Client.Tests/Umpk.Client.Tests.csproj -c Release -m:2 -nr:false --no-restore --filter FullyQualifiedName~CreativeMiningSessionRuntimeTests --logger trx;LogFileName=session-green-attempt2.trx --results-directory /home/anon/Projects/MinecraftOfficial/runs/creative-mining-session-20261022-v1/trx-session-green-attempt2`. The adjacent filter is `FullyQualifiedName~CreativeMiningSessionRuntimeTests|FullyQualifiedName~AttackInputTickPipelineTests|FullyQualifiedName~CooldownSessionRuntimeTests|FullyQualifiedName~CoreTickFailureTests`. Argument arrays in the corresponding command JSON files preserve shell-independent quoting. The SDK is the retained official .NET 10.0.401 toolchain.

Earlier attempts remain separate. Attempt one failed compilation because of incorrect fixture record property names. Attempt two had six genuine packet-count failures and two legacy login setup failures; attempt three repaired only that setup and produced eight genuine failures. The first attempted green copied the owner with an old timestamp, so MSBuild reused the original DLL; its packet failures are a build-harness fault. `stale-incremental-attempt.json` records source/DLL timestamps and hashes. A forced no-incremental Release build then succeeded with zero warnings/errors before the accepted green; the source/PDB checks independently confirm the compiled overlay. No assertion was weakened.

The Client PDB SHA-256 for the accepted session run is `c3371e72b7d9a2fb2931b5fe066bb7d321f8ebac618362ea04844331172483b3`; the Client.Tests PDB is `875b5befd45a6d1a7f091adc1627211baa6d5d31ac83d89024592c38c4447767`. The test source SHA-256 is `406aebdacbb0037b2d0e327848af13dc0c57cdb5c130d28fab2d97814ef3babd`. The producer source hashes remain `981585874a189cbd78863ce9a64a41f5884ca3f678f31e244d5a50898cf1cd90` for the mining owner and `1e739a37dc9cee7afbc2e6c7a8db2cc9520daf7de78873834d96c0b6cbc4deb5` for the restriction helper.

## Remaining acceptance

This supplement executes no native JVM or live server and touches no sibling checkout, shared artifact, shared seed, or network port. The full Client suite was not repeated for these eight new cases: the separate frozen 335-case delivery already passed 27,071 private-base Client tests, and the combined immutable producer gates must run the integrated total. Fresh merged-producer repository gates, live creative negative acceptance, consumer checks, Native AOT, package checks, and published-head CI remain required. The original 335-case delivery and its historical counts remain unchanged.
