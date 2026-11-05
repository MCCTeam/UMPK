# Boat bounds and block interactions on protocol 771

Minecraft 1.21.6/protocol 771 now uses the existing boat motion, geometry, membership and interaction owners for canonical uncontrolled boats, chest boats, rafts and chest rafts. Placement uses their current bounds, and view mining stops when a moving boat intervenes. The implementation keeps the protocol-770 offset-box behavior separate from 771's target-rooted interpolation handler. Twenty canonical names share the source-bound dimensions and have caller controls.

The existing local position and metadata observers now accept the supported boat profile alongside the existing Cow profile. This supplies the local context needed by boat ticks without installing a Cow controller on 771. Sleep, unsupported pose, mounts, leash state, unavailable terrain and relevant local predictions still invalidate unsupported context. Physics-disabled placement retains its unknown self-body. The implementation does not introduce another motion clock or velocity owner.

| Acceptance | Executed result |
| --- | --- |
| Original 1.21.6 methods | 28 rows across four representative families; 56 native codec body round trips; original actor ticks and entity-manager membership; source-bound target-rooted queries, partial updates and velocity clearing |
| Failing runtime stages | Baseline: 81 controls pass and 56 causal assertions fail. Profile-only: 124 controls pass and 13 causal assertions fail. Both stages use the same 137-case set |
| Connected callers | All 137 combined boat cases pass, followed by 11,164 adjacent and 39,874 unfiltered Client passes, zero skips |
| Deliberate count change | The full multiset is exactly 39,809 minus one old unsupported-771 case plus 66 new cases, net +65. The new positive family controls replace that obsolete negative. Every other old multiplicity remains; only the three declared display-path mappings of 121/3/3 occurrences apply |
| Compiler and source identity | One retained test compiler and retained profile compiler precede one fresh observer Client compiler. Independent Root replay checks 4,506 source files, 237 final Client and 388 test PDB documents, all reference and producer pins, exact raw results and cleanup |
| Owned execution | All five continuation commands meet their limits, including the single full-suite run. All seven memory-event keys are present and zero in 779 raw samples and the final state; all thirteen observed host identities are gone |

Client DLL `d97ea2ce4652c18ffeeecc26053a9ed54f574ff836f574716e9a80bdd39f0d22` and PDB `f2a02d2ac1d38868c0d404b115c221eadf169c560e7481b085bcee95dd1144ae` retain their actual private observer compilation. XML `0b6b339a115a36af048941fdd14baad083b454d9cfc221adbdf3556381c1a1e3` is byte-identical to the previous Client XML. Publication copies the exact tested source; it does not claim a new compilation at the publication commit.

The first managed wrapper remains failed. Its baseline and profile-stage test counts were correct, but the profile evidence guard expected both mining cases to fail at their initial packet-count assertion. They instead reached the continuation phase and failed an exact type assertion: expected Swing, observed PlayerAction. An offline correction binds only those two actual assertion/caller locations, preserving all other baseline/profile assertions and multiset guards. The failing diagnostic does not expose the numeric PlayerAction value, so no numeric action observation is claimed. Root separately admits the two causal prefixes; the continuation reuses their test/profile binaries and performs only the missing observer compilation, green, adjacent and full checks.

The native evidence uses original actor methods and the real client entity manager with controlled AIR/STONE lookup callbacks and constructor-equivalent world fields. It does not prove native TCP/chunk decoding, rendering, local vehicle authority, arbitrary peer collision or riding. The near local-prediction regression proves conservative managed invalidation, rather than a native observation that an unknown cell is blocked. Unknown context remains unknown.

| Independent receipt | SHA-256 |
| --- | --- |
| `.scratch/ROOT-BOAT771-NATIVE28-ADMISSION-20261105-v1.json` | `a60e941558dd86f04195c611595bdcfdc3f2044ecd58e4d98b9973d5bf0a712c` |
| `.scratch/ROOT-BOAT771-RETAINED-CAUSAL-PREFIX-ADMISSION-20261105-v1.json` | `5aefb006d8fe3396a49d5bf4efab416c0201685fbb28d6049cd6aca3444aecbc` |
| `.scratch/ROOT-BOAT771-MANAGED137-ADMISSION-20261105-v1.json` | `0133a67a9fb14495fbefec2cc646ecfe13e03be90b609a658cf6d6935f0d148f` |

Exact commands, source snapshots, compiler origins and raw results are indexed by `/home/anon/Projects/MinecraftOfficial/runs/boat771-observer-continuation-managed137-20261105-v2/CONFIGURATION.json`, SHA-256 `486717d4a13d5bf340702887174aa53ba129af6b880ddebb9df86bcfe98b5f3a`. The completion has SHA-256 `dad6bff1e25f8da1a326feb018c618a62c603db8808addcaa82eb9198c67146b`; cleanup has SHA-256 `8c305a523fe91a2df82eadc0c5218382766c76515045703c9b7bd10eb265e43a`. The actual composite index is `.scratch/boat771-observer-composite-actual-audit-20261105-v1/ACTUAL-BOAT771-MANAGED137.json`, SHA-256 `96732481043adac10e8c2bd114adc0b590dba4a1ae76cc4e8e59f8c22f1e00a6`. The original failed attempt stays at `runs/boat771-native-backed-managed137-20261105-v1/`.

Fresh full-repository, consumer, local Native AOT, package, live and published-head CI checks for Client d97 remain pending. The repository total 54,567 is a forecast until all seventeen suites execute. The [770 report](boat-interactions-770.md) keeps its earlier Client ed12 results and original identities. Other unimplemented bands, authority/riders, wider entity behavior and the [completion ledger](interaction-completion.md) remain open.
