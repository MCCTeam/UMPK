# Lore wire preservation

The Lore codec now retains the received component bytes when their modeled content is unchanged. This fixes the original native rich and translated Lore field-order round trips without making byte layout the semantic item identity or DATA hash. Provenance uses weak object references and separate legacy/modern codec owners. A canonical encoded snapshot prevents stale byte replay after an in-place mutation of opaque hover NBT; copying a component into the other era uses that era's encoding. Plain string lines keep the existing direct path.

The text model cannot represent every native translation argument kind. Numeric/Boolean arguments become text in the modern decoded model, while the legacy native stream can compress homogeneous numeric arguments into NBT arrays whose arguments the model does not read. Those received values retain their original bytes, but their semantic identity is explicitly unavailable. The internal completeness hook lets the mining owner stop an unresolved comparison. The existing hashed-stack owner omits the whole unavailable component for native resynchronization. No public API, dataset feature, package dependency or DI lifetime changed.

| Check | Executed result |
|---|---|
| Existing original native Lore evidence | Reused the accepted 24-row native campaign and its eight actual rich-frame byte failures. |
| Primitive source boundary | Reused the supplemental 14-row native witness at protocols 766 and 777; Boolean versus numeric one normalizes to the same Byte on the stream, while primitive versus string remains unequal. |
| Focused failing checks | The original 20 checks executed with the old Protocol DLL: 14 failed and six controls passed. The four added legacy array checks then failed against that same DLL. |
| Corrected codec | All 24 focused checks passed, including literal native set-slot bytes, field assertions, mutation guards, cross-era encoding, numeric arrays and unavailable-hash handling. |
| Adjacent regressions | All 341 component/hash checks passed. |
| Full Protocol suite | All 7,246 checks passed with no skips; its exact multiset equals the original 7,222 checks plus the 24 deliberate additions. |
| Build and formatting | Normal SDK builds compiled the test assembly and Protocol owner only. Both changed files needed no formatting changes. All 653 authored Protocol source checksums occur in the tested PDB's 656 documents. |
| Producer and cleanup | All 39 original producer files and 20 frozen references stayed unchanged. Only the Protocol DLL/PDB/XML changed in the test runtime. All 25 observed host PIDs exited; the owned service is inactive, its main PID is zero and its cgroup is removed. |

The accepted continuation took 57.15 seconds and peaked at 731,828,224 bytes under the unchanged 4 GiB limit. It used the frozen source checkpoint `d9a210c`; the newer client placement fix is outside this Protocol-only run. The authored Protocol preimage is unchanged at the newer checkpoint. The tested Protocol DLL is `12a0a309b587e5fafc58ae9b28894b6438242bfa0da45f1f04f8532df1b85b27`.

Retained failures remain separate: the first owner build missed four existing Microsoft.Extensions transitive references in the external build override, and a source-only preparation stopped when the newer client source no longer matched the old frozen client. The accepted continuation restored those existing reference DLLs and retained the matching source archive. Neither failure was relabeled as a pass, and the valid failing-test prefix was reused.

The compact receipt is `.scratch/lore-wire-preservation-20261104-v2/ACTUAL-PROTOCOL24.json`; raw commands, compiler inputs, results, source manifests and cleanup are under `/home/anon/Projects/MinecraftOfficial/runs/lore-wire-managed24-20261104-v3/`. Client mining integration, allocation/AOT checks and the final repository, consumer and live matrix remain separate requirements. This report does not claim complete Lore identity or final vanilla parity.
