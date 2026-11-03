# Enchantment DATA hashes

## Behavior

Modern inventory clicks hash enchantment DATA, while received inventory packets use the component stream form. Native stream decoding accepts final levels 0..255. The DATA form used for a click accepts final levels 1..255 and first reduces duplicate holders to their last entry. A received zero remains represented; clicking that known value fails before sending a frame. Missing holder information remains an unavailable hash and follows the existing whole-component omission policy. Those outcomes are distinct.

The change affects the existing `EnchantmentsCodecImpl.Hash` owner for `minecraft:enchantments` and `minecraft:stored_enchantments` at protocols 770–777. It leaves wire admission, raw entry order and duplicates, generic `HashOps.Map`, public contracts, package versions and dependencies unchanged. The pre-770 hash wrapper retains its existing behavior.

## Original-method evidence — 2026-11-03

`enchantment-data-hash40-20261103-v1` reused the retained genuine enchantment registry setup and verified vanilla jars. One helper compile and two serial original-method JVMs covered 1.21.5/770 and 26.3/777: both actual component types, ten patterns each, 40 rows. The helper called original stream decoding, `TypedDataComponent.encodeValue` with `RegistryOps<HashOps.CRC32C_INSTANCE>`, original `DataResult.getOrThrow`, and `HashCode.asInt`. It replaced no original method. This is an original-method witness; it did not run native client input, network sessions or a server.

All 32 successful DATA hashes and eight errors agreed across both versions and component types. Zero and positive-then-zero decoded successfully on the wire, then failed DATA encoding with `Value 0 outside of range [1:255]`; `getOrThrow` raised `IllegalArgumentException`. Earlier invalid levels overwritten by a positive final value hashed as that final value. Reordered maps agreed.

| Native map | Hash |
| --- | ---: |
| Empty | -982207288 |
| unbreaking1 | 323656802 |
| unbreaking2 | 182816843 |
| unbreaking255 | 1892810400 |
| unbreaking1 and fortune1 | 1439783631 |

The exact helper, input pins, commands, original class-load origins, raw rows and output hashes are indexed by `.scratch/enchantment-data-hash40-preparation-20261103-v1/ACTUAL-HASH40.json` (SHA256 `f2a39b8e05bb7c155db3837d95e048fb5ce0d74993b0ec3faeb156d25369657d`). Raw evidence lives under `/home/anon/Projects/MinecraftOfficial/runs/enchantment-data-hash40-20261103-v1`. All four observed host PIDs were absent and the exact owned unit was stopped.

## Runtime acceptance

The managed capsule used a fresh immutable archive of `72873c02d3d5be508fcf7fcb728558ff8d8e6a7d`, two new test files, the frozen producer references and normal SDK project builds through external overrides. Both test assemblies compiled before RED. The Protocol suite observed 96 passes and 74 causal failures across 170 new cases; the Client suite observed four passes and 16 causal failures across 20 new cases. One Protocol source-file change then compiled through the normal project, and the same 190 cases passed without skips. The unfiltered Protocol assembly passed all 7,129 cases without skips.

The 170 Protocol cases exercise bound set-slot decoding, decoded values and byte-identical inbound re-encoding, then the actual bound container-click DATA hash or known error across all eight modern protocols. They include nested container components and distinct unresolved holder IDs. Missing holder IDs cannot be merged into an invented known map or hashed using an empty identifier.

The 20 Client cases include 12 cases that substitute an encoding sink for transport I/O and eight real connected session cases. The connected cases install the retained two-enchantment configuration fixture through actual configuration frames, populate inventory through bound set-slot frames, invoke public `InventoryActions.ClickAsync` on the client owner and inspect peer-received container-click bytes. Duplicate values produce the literal native hash. Zero raises before any click frame reaches the peer; a subsequent peer-observed keepalive confirms that no partial frame was emitted and that the connection remains usable. Local cursor and pending prediction remain applied, matching the native prediction-before-hash ordering. Session disposal verifies peer EOF.

The candidate Protocol DLL is `3f9f4b3281b6f938ba7da8079c176dca8760993269b09d038554a45d525a971e`; its PDB is `0845ac69402d70ebe3b8a7ee50b15fa4314dbecb428cb2960526b2b4891a26a9`. The unchanged Client DLL remains `cc068a96416e060124a9381e6432880bc9011ff63db472879b67d0a9d6a9f3a6`. Exact commands, normal compiler item lists/binlogs, test names/TRX, binary hashes and cleanup are indexed by `.scratch/enchantment-data-hash40-preparation-20261103-v1/ACTUAL-MANAGED190.json` (SHA256 `2e611c4d5a91a67cc48358459fce73b0564dd4d7969745c9ce1c7bc21cc7ebde`). The job completed in 48.73 seconds under 4 GiB and 200% CPU; all 25 observed host PIDs were absent and the exact unit was stopped.

Normal SDK builds requested one Csc processor. Actual compiler apphost processes were observed, but the collector's `csc.dll` matcher did not capture their per-process environment; this observation limit does not establish an actual one-processor value. Pre-CoreCompile item lists also omit late SDK global configuration inputs; normal build tasks/binlogs retain the execution. No build or test was rerun to repair either reporting limitation.

## Deliberate counts and remaining checks

These tests add 170 Protocol and 20 Client cases: Protocol 6,959→7,129 and Client 36,131→36,151, increasing the 17-suite expected total 50,462→50,652. Counts for later independent additions must be combined separately. Selected formatting for the three changed C# files loads all 47 projects, analyzes their three owners in both phases and changes zero files; all other source bytes are unchanged and all five observed host PIDs exited. Receipt: `.scratch/hash3-normal-format-root-20261103-v1.json`. Full formatting, full Client, final producer consumers, local AOT, package runtime, current-head CI and the required live release matrix remain separate acceptance gates. This bounded inventory-hash correction does not close other item component or interaction families.
