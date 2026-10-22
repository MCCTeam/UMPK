# Component-era Adventure mining

Commit `5699999` connects typed `can_break` permission to the existing mining owner for protocols 766–769. Previously, the owner rejected Adventure mode before evaluating the already decoded component. A matching explicit block holder set or available native block tag now permits the normal mining request. Missing or nonmatching permission is a known denial: the held request remains pending and sends no START. Missing tag, property-order/range or full native block-entity-save context remains explicitly unavailable. Protocol 765 retains its flattened NBT path; protocol 770's extended predicate payload is not enabled by this slice.

The current `AdventureModePredicateComponent` reference owns its native last-state/result cache. Tag reload alone does not invalidate a cached decision for the same state; receiving a replacement component creates a separate cache owner. The implementation uses one weak component-keyed cache inside the existing actions owner. It adds no timer, public contract, dataset axis, dependency, package version or DI lifetime.

Commit `6ea3a9f` separately corrects mining target identity for this component family. Native predicate records compare ordered values, while the public wire model's `IReadOnlyList` members previously used reference equality. Equivalent freshly decoded components now preserve progress. A changed tooltip remains a different native identity and restarts the target. The complete effective component map is still compared, and unrelated values retain their existing equality. Public item/component equality is unchanged.

## Native evidence and input order

`/home/anon/Projects/MinecraftOfficial/runs/mining-component-permission-audit-20261022-v1/native-source-audit.json`, SHA-256 `7d56a41988aa95d71f22dad9f5249243f8fce8ab46ed743d12942410ae873714`, binds the original official client artifacts and matching decompile receipts for all seven named releases: 1.20.5, 1.20.6, 1.21, 1.21.1, 1.21.2, 1.21.3 and 1.21.4. The original owners are Player's Adventure restriction, ItemStack's permission entry, AdventureModePredicate's cache and BlockPredicate's short-circuit tests.

The unchanged original-method witness executes 56 cases across four protocol profiles. It checks absent, matching, nonmatching, universal and empty predicates; last-state cache behavior; stack-copy sharing; and equality of independently constructed equivalent components. Every recorded actual outcome equals its independently stated expectation. Original class origins, client/dependency hashes and execution commands remain with `native/original-method-v2/finished.json`, SHA-256 `a63139a902015820d85466522569a609d208afbcddd063aa2648544ae5793471`. This is controlled original-method execution, not a complete native input/world/network run.

The seven-release input-order supplement has SHA-256 `6be24e4f500e2e11941e0bf1a6469e2b8d04b61c1896aed6ad811e07a18aab44`. Its 98 exact method bindings establish click handling before held continuation, a swing after denied start, and retained destroy-target comparison without an `isDestroying` guard. Consequently, a denied click on an equivalent retained target can send two main-hand swings without START. The four affected cache tests now assert exact ordered decoded packets and hand fields rather than the earlier list-reference accident. No production input/controller code changes for this correction.

## Executed tests and integrity

| Check | Result and producer |
| --- | --- |
| Permission red | Four Adventure failures/four Survival controls initially; expanded twenty failures/six controls. Adding only the unconnected helper retains the failures. |
| Identity red | Eight equivalent/restored-component failures and four changed-tooltip passing controls. |
| Focused final additions | All 46 new cases pass: 34 permission and twelve identity. Real UmpkClient sessions receive framed packets through bound codecs, assert decoded fields and byte-identical round trips, use the session tick and verify peer EOF. |
| Adjacent mining | 742 passes, zero skips, on the private exact-`7ace569` aggregate overlay. |
| Full private Client | 28,967 passes, zero skips, with Release build and scoped format passing. |
| Private aggregate AOT | 25,103,776 bytes, strictly below 25,165,824; exactly one executed passing smoke. All 4,238 source entries, 32 primary DLL/PDB files and 251 tested output files remain unchanged. |
| Independent combined integration | The separate Cow/mining overlay on `64d2af5` passes all 29,125 Client, 715 Physics and 41 normal Integration cases, with fourteen exact named normal opt-in skips recorded separately. Release build and scoped format pass. |

The frozen permission and identity delivery manifests are `becb68ae8a1e035dd940d020c7845994841e8f8bd3749bd3dc44008838a3c270` and `e0f16e0e5a551aa3ae01c5ef684d140920b96e98d47a5aaea5bc05fd5bd650be`. Their apply order, six final source hashes, commands, logs, TRXs and private AOT proof are indexed by `combined-evidence-v1/index.json`, SHA-256 `3ea5c407c1571a4df23add8f5e8e133af46be99ef023f2bc493ae0ad937ad2cf`; the combined manifest is `f31a6baedd81da6b0271d984b0c7afa3092a4e04b9707f06f98ec920771c5707`.

Independent review verifies seventeen external evidence files, all 56 original-method outcomes and all 98 source method bindings. Its receipt is `runs/scale-creative-root-integration-review-20261022-v1/component-mining-root-review.json`, SHA-256 `eaae3481de4a7e2e17bc224b9587732430fc8bc9504cbf9f36a4b15142e2b2e0`. The complete combined runtime check is `runs/cow-mining-root-integration-20261022-v2`. Its first preparation stopped before any build because its baseline lookup omitted the `.dll` key suffix; that preparation is not a failed production test. Analyzer/fixture/native-prerequisite failures and the 96-pass/four-fail earlier swing expectation remain separately retained.

Final combined commit-matched repository, consumer, package, AOT and CI proof remains separate. Native live Adventure acceptance, full original outer input/local/world ticks and networking, protocol 770+ payloads, property/NBT permissions, active-use ownership and broader item identity families remain open. No required unknown is relabeled as allowed or denied.
