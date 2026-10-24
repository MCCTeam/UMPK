# Additional native menu activation

The existing block-use owner can now predict the original client result for nineteen more registered menu keys. This source-backed change adds the missing use-then-swing behavior while keeping server menu opening, locks, recipes, blocks and inventory changes separate. It adds no public API, dataset, feature axis, dependency, DI lifetime, package version or generated fixture change.

## Exact native scope

| Registered keys | Applicable profiles | Client decision |
| --- | --- | --- |
| `trapped_chest`, `ender_chest`, `dispenser`, `dropper`, `hopper`, `brewing_stand`, `enchanting_table`, `anvil` | 47–777 where registered | Success. Original client branches do not require a server menu to open. |
| `chipped_anvil`, `damaged_anvil` | 393–777 | Success through the registered anvil implementation. |
| `barrel`, `smoker`, `blast_furnace`, `loom`, `cartography_table`, `stonecutter`, `grindstone` | 477–777 | Success. Smoker and blast furnace inherit the original furnace method. |
| `smithing_table` | 477–578; 735–777 | False/pass through 1.15.2; success from the 1.16 menu implementation. |
| `crafter` | 765–777 where enabled | Success after the existing native feature-permission check. |

From protocol 766, default block use runs only for the main hand; offhand use can continue through the existing item owner. Protocol 777 retains local-only placement swing and emits no serverbound swing. The existing session owner still handles secondary use, enabled features, border, cooldown, Adventure, captured packet lifetime, ACK waits and unknown item components. Packet-established terrain remains required: storage synthesized by block updates is not native chunk availability.

The production change stays in `BlockUsePrediction.cs`. One exact-key classifier is shared by menu-result and terrain membership checks. One internal profile bit records the smithing-table pass boundary. No second sender, timer or state owner is added. Protocol 47 keeps the existing 1.8.9 client profile; the other 1.8 names contribute source observations only.

Original ender-chest methods can read block entities and the block above, but every client return in those branches is still true/success, including missing block entities and obstruction. The predictor therefore does not claim an open menu or invent missing NBT. Lectern, jukebox, campfire recipe, cake/candle and other conditional interactions remain separate unfinished work.

## Source evidence

`/tmp/umpk-additional-menus-20261024-v4/expanded-native-source-bindings.json` has SHA-256 `484fa179b264c0b64c63310e5217361689f4451ad6e43a80dad7960946515337`. It binds 1,107 exact registrations across all 75 supported named releases and 50 datasets, with 1,995 current source and artifact/completion-receipt inputs and no unclassified return. The audit follows inherited native method owners and proves that the modern class ancestry has no item-interaction override before the already audited default block fallback. It retains original method bodies and registration lines, including the later `BlockItemIds` identity bindings.

The verified 75-release official client jar hashes from `runs/menu-activation-audit-20261022-v1` are reused. Current artifact and decompile receipts, original source hashes and jar sizes were checked again; jar contents were not rehashed by this source-only preparation. No helper JVM or native server was started for this audit. The existing controller, input, result and callback evidence remains pinned to its original receipt.

## Real session and native-publish evidence

The private job is `/home/anon/Projects/MinecraftOfficial/runs/interaction-additional-menus-20261024-v4`. Every tracked Client file before the one-file patch matches `19594f2ff938bbe3ae8602114cb80880cd2b27a8`. The tested binaries remain an explicitly recorded hybrid: the retained c977 Client metadata with the honeycomb/range source changes, frozen c849 dependencies and the separately tested compacted Protocol pair. The sample retains the original 2ed5514 comparison metadata. This is not a normal immutable 19594 producer certificate.

The 213 new cases connect a real `UmpkClient` to the framed in-memory server, complete actual login/configuration/play, populate inventory and terrain through bound packets and invoke public `PlaceBlockAsync`. Ninety-nine profile/hand cases traverse every applicable registered menu key and minimum/default/maximum state boundaries. Another 114 context cases cover secondary use, both hands, Adventure, cooldown, permitted legacy NBT versus unknown effective components, occupied destinations, absent terrain, unload and respawn. The tests assert exact use/swing order, cursor, hand, ACK behavior, unchanged blocks/stacks/NBT and peer-observed disconnect. Incoming and observed outgoing feature frames retain decoded-value and byte-identical round-trip checks. These tests make no locomotion or TCP claim.

| Executed check | Result |
| --- | --- |
| Valid focused red | 213 executed: 160 missing native-result failures and 53 passing controls. |
| One-file production change | The existing profile and both production classifier callers change together. |
| Focused green | The same 213 identities execute and pass, with zero skips. |
| Adjacent regressions | 5,933 pass across eighteen existing menu, door, trapdoor, slab, mining, input, cooldown and placement classes. |
| Actual compiler references | Nine Client and eleven focused-test UMPK references match the intended frozen inputs in each applicable compilation. |
| Scoped format and source identity | Whitespace verification passes; all 221 actual PDB source documents match, comprising 213 Client and eight focused-test documents. |
| Native AOT | The unchanged publish profile produces a 25,165,568-byte ELF, strictly below 25,165,824 by 256 bytes; all eleven actual ILC inputs and the original nine-package graph match. |
| Native smoke | Exactly one test executes and passes; zero skips. |
| Invariance | All 268 tested output files, two native input files, 24 frozen dependency files, 117 original outputs and 4,293 original accepted source entries remain unchanged. |

The native binary SHA-256 is `95a08e956c8100cbe6de317b35087a157900aec936f9f0817ad1ef69b3aa5dbf`. The actual-file functional index is `/tmp/umpk-additional-menus-20261024-v4/functional-receipt-index.json`, SHA-256 `56b66768118ba212d3a0d5bc23ebd4dc3053957490e1f28bcc2fb77b0762c418`. It points to complete command arrays, logs, TRXs, actual references, PDB checks, source postimages and invariance manifests. The exact production/test delivery patch has SHA-256 `da3bd194b49dc250088ab9997ba2f8961fe69fe03f49fbfede957adaf3fa7566` and passes `git apply --check` against the current shared source.

The entire v4 driver remains failed after 58.64 seconds because an extra CPU observer required a `csc.dll` command line and observed no compiler. This SDK also supplies the `csc` executable apphost. The actual MSBuild policy receipts verify `CscEnvironment=DOTNET_PROCESSOR_COUNT=1`, Release optimization, warnings as errors and disabled compiler sharing; the SDK task forwards that property as its environment. No actual compiler-process CPU observation is claimed for v4, and its overall failure is not relabeled. The independently checked runtime, formatting, reference, PDB and native-publish receipts above remain their actual executed results. No extra AOT compaction was applied or needed.

Earlier fixture failures remain separate. Attempt v1 executed 213 cases with 170 failures: the 160 intended native gaps, six wrong legacy Unbreakable expectations and four missing legacy respawn fields. The original axe/item sources prove that those known legacy-NBT survival controls pass; modern effective-component patches remain unknown. An unlaunched v2 parenthesis correction was caught before compilation. Attempt v3 executed 213 cases with 162 failures: the same 160 native gaps and two fixture difficulty mismatches. Native 1.14 removes difficulty from respawn, so the fixture now represents its absent field as zero while preserving exact decoded fields and byte round trips. All 213 case identities remain intact; no production change fixes or conceals a fixture error.

The root actual-file review independently parses the four TRXs, compares all 213 red/green identities, checks all eighteen adjacent class totals, rehashes 2,303 indexed paths and all 221 PDB source documents, and checks the actual ELF and protected native inputs. Its receipt is `/tmp/additional-menu-root-actual-review-20261024-v1.json`, SHA-256 `b361aeaf7b79b103e97a03d9a8753d2b08edf2d07f5a31bbacd452f46da16da7`.

## Remaining acceptance

This slice has not run native server acceptance for the nineteen new menu keys, a fresh complete immutable-producer gate set, current producer-matched consumer/package checks or published-head CI. The 213 deliberate cases increase the Client baseline from 33,349 to 33,562 and the seventeen-suite total from 47,135 to 47,348; the seventeen normal opt-in skips remain unchanged. Those totals describe intentional test additions, not an executed new full-suite pass. The broader conditional block-use, mining, geometry and final version acceptance inventory remains unfinished.
