# Mining interaction runtime

This report records direct mining-rule fixes developed from base `677ca1a5885ed3355abe332708cb51b1bc32084b`. These focused results do not close the complete interaction ledger. The original source, probe output and raw logs remain outside Git.

## Held-tool target identity

Pre-flattening controllers retain a destroy target when only a damageable current stack's separate wire durability changes. Item and NBT must still match. An Unbreakable stack or nondamageable subtype keeps metadata in the comparison. From protocol 393, damage belongs to the compared tag or component identity. `VanillaMiningItemIdentity` now supplies this rule to the existing `SameDestroyTarget` caller; it ignores stack count without ignoring meaningful tag/component changes.

The source audit covers all eleven pre-flattening datasets. Original item-registry/max-damage calls identify 50 damageable types on 1.8.9 and 52 from 1.9 through 1.12.2. The internal legacy list contains that measured union; it excludes horse armor, buckets and block subtypes. `native/controller-sources.json` under `/tmp/umpk-mining-identity-20261019` records controller/stack source and verified artifact hashes, with SHA-256 `093d62adda22da07bde8551c03c7f464d5707bf51bc78364aa94992c935df47f`. The original-call receipt index has SHA-256 `43207a3ede598f1528c32620cd47b2eaf321b01e4de0e0595cbfbdaafd49409c`. Representative sources are `1.8.9-client-decompiled/bda.java:225` and `1.12.2-client-decompiled/bsa.java:231` under the shared oracle.

Fifty-four new runtime cases feed bound inventory codecs through the production applier and scheduled mining actions. The first valid run fails 17 durability cases and passes 37 controls. Adding the helper alone retains those failures; connecting the caller passes all 54. A worn diamond pickaxe then retains its six-tick progress, with START/STOP and seven swings, while changed subtype/NBT/components still restart. These are packet-populated component tests, not live-server observations.

```bash
/home/anon/Projects/MinecraftOfficial/toolchains/dotnet-sdk-10.0.401/dotnet test /tmp/umpk-mining-identity-20261019/source/tests/Umpk.Client.Tests/Umpk.Client.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~MiningItemIdentityRuntimeTests' -m:1 -nr:false
```

`final-source.json` records the archived base and exact owned overlays. An initial MSBuild IPC failure remains a harness failure, separate from the valid red/green runs. Complete repository, consumer, AOT and live acceptance for the new producer remains pending.
