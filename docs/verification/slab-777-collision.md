# Native collision references for the new 26.3 slabs

The general collision lookup now returns the native shapes for all 198 states of the sixteen wool slabs, sixteen concrete slabs and poplar slab introduced in protocol 777. Previously 132 half-slab states fell back to a full cube; the 66 double states had the correct fallback geometry but lacked explicit measured bindings. Protocol-776/777 oak slabs remain controls. Slab placement, merging and result/swing rules require separate runtime acceptance.

The maintained extractor supplements ordinary canonical shape references from the retained original collision grids. DataGen produces the normal runtime table; no runtime repair list or placement-only fallback is introduced. Existing physics and pathfinding callers consume the corrected general `JavaBlockShapes.GetCollisionShapes` lookup. Public signatures, feature axes, dependencies, versions, DI and generated pins are unchanged.

## Native evidence and reviewed data delta

The retained original 26.3 dump is `runs/composite-grid-extraction-20261016-v1/native/standalone/all-v2/26.3/collision-grids.json` under `/home/anon/Projects/MinecraftOfficial`, SHA-256 `0a3a3a697422b36c8d06d02a7815798d4baadb5b6cc8dcf746dea59d272aec62`. It contains 35,723 states and 338 groups and agrees with the already-correct canonical collision grids. Original `SlabBlock.java:33–55` supplies top `[0, .5, 0, 1, 1, 1]`, bottom `[0, 0, 0, 1, .5, 1]` and full-cube double shapes. Source registrations, artifact hashes, completed decompile receipts and unchanged Java measurement sources are pinned by the extractor. No native campaign was repeated.

Exactly 33 new reference rows use existing pool indices `[26, 26, 27, 27, 21, 21]`; only their names leave the unmatched list. All 1,196 previous rows, `shapes.json` and other protocols are unchanged. Generated changes are confined to 198 `BlockShapeReferencesV777` entries, 132 half-state `Solid` bit removals and matching base flags for 33 blocks. All double flags, other packed fields and generated members are unchanged. Root and an independent reviewer checked the field-level diff. The finite selection belongs to extraction tooling and does not alter the public lookup contract.

The extractor rejects changed existing references, null/incomplete entries, missing native states, new shape-pool entries and artifact/schema/geometry mismatches. Its twelve new Python tests include these negative controls. The 210 C# regressions assert decoded slab type/waterlogged values, public AABBs and explicit shape coverage for every new state plus twelve oak controls.

## Executed private proof

The frozen private producer is the exact `2925b5180ce25b3fd50666ce569f4720be9934be` archive plus the six-file reviewed correction. Evidence is under `runs/slab777-collision-runtime-20261023-v1`; its 279-file manifest SHA-256 is `7fba0243c69304a4bc79942272bed335f6a1d12396539b74802a52df0e07dfec`. The patch is `2612e02dc613b4ba3195b804f4d96e138c09a47b9b180a616baf4ed103e68bd7`; its source manifest is `cb9c85c5e93867563ab1f328a26be19b6941722db32c60799d23472fd6ec9d3c`. Raw commands, reports, generated-field review and logs remain outside Git.

| Check | Executed result |
| --- | --- |
| Geometry-first red | 132 wrong-shape failures, 66 binding-only failures, twelve passing controls |
| Corrected slab cases | 210 passed, zero skipped |
| Data.Java / DataGen | 2,488 / 257 passed |
| Physics / Pathfinding | 715 / 1,057 passed |
| Final full Client | 30,057 passed, zero skipped |
| Extraction Python | 368 passed, including twelve deliberate additions |
| Dataset, language, scoped format and reconstruction | Passed |
| Fresh generation | All 61 files byte-identical |
| Native AOT | 25,132,640 bytes, unchanged; strict limit remains below 25,165,824 |
| AOT smoke and producer invariance | Exactly one executed pass; all 22 normally tested DLL/PDB hashes unchanged |

The first full Client attempt retained one global `UnobservedTaskException` assertion failure. Its original handler stored neither exception details nor stack, so its origin cannot be attributed. An isolated run and the subsequent unchanged full 30,057-case run passed. Other setup/compiler/orchestration attempts remain separately recorded. No assertion, timing budget, teardown code or expected count was weakened. Private AOT uses an isolated source/output tree and pins the actual patched source, separate from the base revision property.

## Integration and reconstruction

The exact six reviewed files were applied after the legacy pre-send commit `5fd6b08c518cb005faf89bfaecbf3ffa4a222a79`. The deliberate Data.Java baseline increases from 2,278 to 2,488; Client remains 30,230 after its separate 173 additions. Root independently passed all 210 focused slab cases, all twelve Python guards and the maintained reconstruction on the combined source. Root receipt and command logs are under `runs/slab777-collision-root-integration-20261023-v1`. Fresh immutable combined repository, consumer, package, AOT and published-head CI checks remain separate from the private slice.

```bash
dotnet test tests/Umpk.Data.Java.Tests/Umpk.Data.Java.Tests.csproj -c Release --filter FullyQualifiedName~Slab777CollisionShapeTests
python3 -m unittest discover -s tools/extraction -p test_native_slab_shape_refs.py -v
python3 tools/extraction/shape_extractor.py --native-slab-refs --data data/java --official /home/anon/Projects/MinecraftOfficial --measurements /home/anon/Projects/MinecraftOfficial/runs/composite-grid-extraction-20261016-v1/native/standalone/all-v2 --check
```

`runs/slab777-collision-reconstruction-inputs-20261023-v1` freezes the 23 declared inputs and 57 observed reads; its seven-file manifest is `3af2637d8c64681544b8ef53c1a9bd803ec6a63cb4e3354de7f8bb29f4cce0cd`. Root's combined-source check is additional to the unchanged 25 repository gates and fifteen earlier reconstruction checks. Successful collision reconstruction does not close slab item use, all contextual outlines, native live placement or the final version matrix. User report additions, logs and sibling source/Git state are preserved.
