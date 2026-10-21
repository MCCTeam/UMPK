# Hoe use requires received terrain

A block update can create a column in UMPK's storage before a full chunk arrives, or after the server unloads it. The hoe predictor previously treated that column as native terrain and emitted a successful-use swing. It now requires the session's received-chunk availability before returning a known hoe result. Missing or unloaded context remains unknown and sends only the requested use packet.

The existing `EntityMembershipApplier` observes chunk availability for terrain sessions even when entity tracking is disabled. It remains ahead of `WorldApplier`, which still owns terrain storage. The recognized-hoe branch in `InteractionActions.PredictBlockUse` consumes that availability; block updates do not establish it. No second chunk tracker, public API, dependency or timer is added.

## Original client source

The verified 1.14.4 client jar has SHA-256 `b3b2a798e2d67b566008fe4a03767ae2c7ff3f8c7ba6751e7b71fc7299672d0a` and SHA-1 `8c325a0c5bd674dd747d6ebaa4c791fd363ad8a9`. The decompile receipt identifies that jar and Mojang mappings. `native-source-audit.json` records the checked artifact receipt and exact source hashes.

| Original source | Observation |
| --- | --- |
| `ClientPacketListener.handleBlockUpdate`, line 843 | Dispatches the update through `MultiPlayerLevel.setKnownState`. |
| `MultiPlayerLevel.setKnownState`, line 320; `Level.setBlock`, line 172 | Resolves the native chunk and returns false when its setter returns null. |
| `ClientChunkCache.getChunk`, line 67; `EmptyLevelChunk`, lines 31–38 | Missing chunks resolve to the empty chunk, whose getter returns void air and whose setter returns null. |
| `ClientChunkCache.replaceWithPacketData`, line 84 | A partial chunk cannot establish a missing native chunk. |
| `HoeItem.useOn`, line 43 | Reads the native clicked block and the block above before returning success. |

These observations come from original client source. This check does not execute a native client or establish complete client view-range/cache behavior. Protocol 47's different empty-chunk behavior remains under its existing owner; this change does not alter it.

## Connected regression

The ten new cases use the real protocol-498 chunk, forget-chunk, block-update and inventory bindings, dispatch them through the session applier chain, and call the production block-use sender. Decoded assertions check the chunk's coordinates, full flag, dimensions and air state. Chunk bytes round-trip through both the bound relay encoder and the structural encoder. Outgoing use and swing packets must have real bound encoders.

| Received traffic before the dirt update | Entity tracking enabled and disabled |
| --- | --- |
| No full chunk | Unknown, one use packet, no swing |
| Full chunk | Known successful use, use followed by swing |
| Full chunk followed by unload | Unknown, one use packet, no swing |
| Full chunk, unload, then full reload | Known successful use, use followed by swing |
| Partial chunk before any full chunk | Unknown, one use packet, no swing |

All cases deliberately prove that cached dirt and a storage column exist. Those facts alone cannot prove native terrain availability. Existing positive hoe fixtures now state their supplied terrain availability explicitly; their behavior assertions remain unchanged.

The test-only overlay on `412e29d5fa1802205b80ed83661ca1634d9715b6` executes ten cases: eight fail and two pass. Registering the existing availability owner for `Terrain || Entities` leaves six swing failures and four passes. Connecting the hoe guard passes all ten. Two earlier harness compilation failures, first a registry tuple member name and then an xUnit analyzer requirement, remain separate from the valid red/green runs.

The complete Client suite passes 23,376 tests without failures or skips, a deliberate addition of ten cases. The focused and adjacent filter passes 2,521 tests without skips. It includes all 642 existing hoe cases, the ten new cases, placement/block-use, legacy membership and feature-composition regressions. The Client project format check exits zero; it reports a workspace-loading warning, so full solution format verification remains a separate final gate.

```bash
/home/anon/Projects/MinecraftOfficial/toolchains/dotnet-sdk-10.0.401/dotnet test tests/Umpk.Client.Tests/Umpk.Client.Tests.csproj -c Release --no-restore -m:2 -nodeReuse:false -p:SourceRevisionId=412e29d5fa1802205b80ed83661ca1634d9715b6 --filter FullyQualifiedName~OnlyReceivedTerrainCanSupplyTheHoeContext --logger 'trx;LogFileName=green.trx' --results-directory /tmp/umpk-hoe-terrain-context-20261021-v1
```

Commands run from `/tmp/umpk-hoe-terrain-context-20261021-v1/source`, an isolated archive outside Git. The pinned SDK is `DOTNET_ROOT` and first on `PATH`; command receipts record the complete arguments, exit codes and source hashes. This evidence identifies the base commit plus the four reviewed source changes, rather than claiming acceptance of a later Git commit. Required final repository, live, consumer, package, Native AOT and CI checks must identify the final immutable producer.

The retained evidence directory is `runs/hoe-terrain-context-20261021-v1` under the shared oracle. Its manifest indexes the valid and failed command receipts, TRX files, reviewed source, original source audit and producer DLL hashes. `final-source.json` verifies four modified source files and 4,162 unchanged base blobs.

The durable evidence bundle is `runs/hoe-terrain-context-20261021-v1/manifest.json` under the shared oracle, SHA-256 `bbc7f1557f310eb94901d76bdbe9f4594d2e82be415649ea3e739eaa17ba2ebc`. It retains 40 files, including the failed harness attempts, valid red/green TRX files, command/source/binary receipts and the native source audit. `final-source.json` verifies four changed source files and 4,162 unchanged base blobs.
