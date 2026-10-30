# Legacy mob type width

## Result and placement impact

The native spawn-mob type changes from an unsigned byte to a VarInt at Minecraft 1.11/protocol 315. UMPK previously retained the byte reader through 1.12.2/protocol 340. Reading a wide type could change its identity and shift subsequent fields. This was exposed by the real-session placement tests: an unknown type of 999 became 231 before the obstruction decision. The three unknown-type fixture inputs remain 999; the fix corrects the packet reader and writer.

The internal `AddMobWire` profile and existing codec timeline now select the VarInt layout at 315. Protocols 315–316 retain the V1_9 metadata table; 335–340 retain V1_12; all retain pre-1.14 position packing. Protocol 210 remains the unsigned-byte control and 393 the adjacent VarInt control. There are no public signature, package, dependency or DI changes. The protocol-47 client profile remains based on 1.8.9.

This packet correction has passed isolated protocol/conformance checks and normal shared-source integration/full formatting. The resumed placement runtime, live acceptance and final consumer checks are separate prerequisites; this report does not close them.

## Native evidence

The boundary proof is `.scratch/legacy315-spawnmob-varint-boundary-20261025-v1/PROPOSAL.json`, SHA-256 `649b5f8f90b240289a3b827f089f66e03a2333ead02c2eb7fa5fb9f992efe7c5`. It binds eight named releases and sixteen original-versus-retained packet/buffer class pairs. In 1.10.2, `fq.a(eq)` reads `readByte() & 255`; in 1.11, `ft.a(et)` reads `et.g()` and `ft.b(et)` writes `et.d(int)`, the native VarInt methods. The later bindings retain that width.

The controlled original-class witness is `runs/spawn-mob-varint-codec-20261025-v1/native-addmob-witness/native-packet-witness.json` under `/home/anon/Projects/MinecraftOfficial`, SHA-256 `21ac2acb72532b46670d0fef30ffebfd837ccb2f71270081eea4d72310ce29a8`. It checks types 0, 65, 127, 128, 255, 256, 999 and -1, with sixteen native writes and eight native reads. UUID, position, angles, raw velocity shorts and complete bytes agree. Reencoding attaches an original empty `kn(null)` metadata owner because the native reader and writer use different metadata fields. No actor/world construction, method substitution or bootstrap replacement is involved. This is a controlled packet-codec witness, not a live wide-type capture. Fifteen retained native type-65 frames provide a separate real-traffic control; their one-byte type does not distinguish the width boundary.

## Executed checks

| Check | Result | Exact evidence |
|---|---|---|
| Focused codec regression | 37 failing cases and 27 passing controls before the fix; the wire-only stage retains the same failures; all 64 pass after timeline integration | `runs/spawn-mob-varint-codec-20261025-v1/results/` |
| Full Protocol suite | 6,491 pass, zero failures or skips | `runs/spawn-mob-varint-codec-20261025-v2/results/full-protocol-green/` |
| Cross-layout rejection | All 50 pass before pin generation | `runs/spawn-mob-varint-codec-20261025-v3/results/focused-cross-layout-green/` |
| Full conformance, update switches off | All 892 pass, zero failures or skips | `runs/spawn-mob-varint-codec-20261025-v3/results/full-conformance-green/` |
| Actual producer closure | 647 authored/generated-table source inputs, three SDK-generated inputs, 650 matching PDB documents and 174 actual compiler references | `v1/targets/protocol-final-actual-sources.json`, `v1/commands/protocol-final-pdb.log`, `v1/targets/protocol-final-actual-csc-references.json` |
| Cleanup | All nineteen observed owned PIDs absent after the final job | `runs/spawn-mob-varint-codec-20261025-v3/cleanup.json`, SHA-256 `5447f2921bcde68148f5aa6f1e248a0d548fbb929dd8dbc9bccb405657eeef71` |

The SDK is the retained .NET 10.0.401 toolchain. Each exact argument vector, working directory, timeout, exit code, raw-log hash and resource receipt is retained in the corresponding run's `commands/*.json`. The sealed `run.py`, input manifest, launch grant and actual unit receipts identify the complete invocation. The final conformance command is:

```text
/home/anon/Projects/MinecraftOfficial/toolchains/dotnet-sdk-10.0.401/dotnet vstest /home/anon/Projects/MinecraftOfficial/runs/spawn-mob-varint-codec-20261025-v3/fixture-root/runtimes/full-conformance-green/Umpk.Protocol.Java.Conformance.dll --logger:trx;LogFileName=tests.trx --ResultsDirectory:/home/anon/Projects/MinecraftOfficial/runs/spawn-mob-varint-codec-20261025-v3/results/full-conformance-green
```

The final retained unit is `umpk-spawn-mob-varint-codec-20261025-v3.service`, actual invocation `2b7bee32153d405bbd178f0d85c5b184`. It uses a 4 GiB memory limit, 200% CPU, no swap and no core dumps. The final frozen Protocol DLL SHA-256 is `39e7decd912f2e578b5347defdeda9ef16a0d1d5b4dc4ecba963a17f2b0ee755`; its PDB is `e7a56d0cd9aa7019574cd4dda16b38da28c5675ce757c06411a6e507cb702ce5`. The handoff `v3/handoff/independent-proof.json` has SHA-256 `13298fa8a774651a27e5ba85d66bb13b6427bf84a8f34fb9219cc294e49ddc1f`. Other frozen producer pairs remain unchanged. Root audit `.scratch/spawn-mob-varint-actual-root-audit-20261025-v3.json`, SHA-256 `b067cc5452707a8d9a904b50fe4368826320b76c980985f61bc14e2cbf4e70f3`, independently verifies 1,561 physical records/PDB documents, actual TRX counts, the twenty-two delivery preimages and owned cleanup.

The deliberate addition is 64 Protocol tests: 6,427 becomes 6,491. Client remains 34,771 at this checkpoint; the seventeen-suite baseline total becomes 48,622. These are expected totals for the integrated source, not a new full-solution execution claim.

## Normal integration and formatting

`runs/spawn-mob-varint-normal-integration-20261025-v2` verifies the uncommitted twenty-two-file integration over `8bc91ed`. This is an explicit source snapshot, not a claim that unchanged HEAD contains those files. All twenty-two postimages match both the delivered files and the shared checkout. Documentation and `logs/` are excluded. Normal implicit project globs compile Protocol, Protocol.Tests and Conformance without replacing or adding Compile items. Two offline restores retain their actual thirteen-project union. Frozen dependency guards prevent compiling Data.Java, Client or sibling projects.

All five authored C# paths pass full formatter verification. Separate reversible whitespace controls prove the actual two/one/two included paths through diagnostic output; those controls are selection checks, not gameplay failures. No formatter correction is needed: the resulting delta is zero bytes. The formatter loads dependent project workspaces and explicitly skips formatting referenced projects; this is not a metadata-only workspace claim.

The normal builds pass another 64 codec, fifty cross-layout and 892 full conformance cases, with zero skips and all pin-update environment switches removed. All sixteen generated pins remain unchanged. Actual compiler/PDB closures are Protocol 647 user inputs plus three generated inputs/650 documents/174 references, Protocol.Tests 183 user inputs including the SDK test entry point plus three generated/186 documents/198 references, and Conformance 42 including that entry point plus three generated and one verified RegexGenerator document/46 documents/200 references. Full 6,491 is reused from its completed isolated producer rather than rerun.

Root audit `.scratch/spawn-mob-varint-normal-integration-actual-root-audit-v2.json`, SHA-256 `2e2a4bbd31dc8b291e8e5567d3767e619e88a9ca9522693bf07777e6deda4aff`, rehashes 1,172 physical evidence records, checks all 882 PDB documents, actual TRX results, zero formatter delta and the shared postimages. Actual invocation `4b337e2670594ee7befc6e746858a8a3` completes in 148.19 seconds under 4 GiB/200% CPU/no swap/no core dumps. Peak memory is 3,502,563,328 bytes with no OOM event. Cleanup `649c29be69a1ab8cd1de55ee46dc425bc54c2bed45b7ddbe4da24ba2182e8c36` proves all forty-eight owned PIDs absent. The exact command vectors and raw logs remain in that run's `commands/` directory.

Normal integration v1 remains a failed harness attempt. Its first two formatter scopes and offline restores pass, then the selection control stops before Conformance because it assumes an initial using directive in a namespace-only file. V2 preflights the exact using-or-namespace offsets on all five actual files and comment-header variants, then proves selection through actual formatter output. The short valid scopes/restores repeat deliberately in fresh paths; no gameplay source, assertion or count changes. V1 cleanup `3850f1e3435ec5d4d46ff16fa52696096a0bb4c1ca749b278f065bdef1e3000d` retains its twenty-one absent owned PIDs.

## Generated pins and retained failures

The existing environment-gated tests generate sixteen changed pins: five codec identities, one timeline and ten cross-layout witnesses. Root reads the complete final diff. The 315–316 identity moves to `AddMobV1_11`; 335–340 keep their codec identity with VarInt type width. Metadata hashes and pre-1.14 position packing remain unchanged. The additional 315–316 synthetic witness carries type 999 with metadata that this era can encode; its forward/reverse comparison distinguishes the width boundary without adding intentional twins. Corpus/synthetic counts describe the actual chosen witness. Every other fixture file and packet row is unchanged.

The original attempts remain separate. Version 1's full suite fails because its isolated fixture root omitted the unchanged allocation-budget file: a 73-row theory collapses to one missing-prerequisite row, yielding 6,419 executions. Version 2 restores that exact existing file and passes all 6,491; no allocation limit changes. Its full conformance run then records 885 passes and seven failures because the new wide-type factory uses a richer metadata value unsupported at 315. Version 3 changes only that factory to the existing encodable legacy metadata, passes all fifty rejection checks before regeneration, regenerates through the documented gates and passes all 892 conformance cases. No assertions, expected counts, pins or limits are weakened to conceal either failure.
