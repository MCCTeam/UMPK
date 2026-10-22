# Crafting table, chest, and furnace activation

The existing typed block-use owner now predicts the native client result for `minecraft:crafting_table`, `minecraft:chest`, and `minecraft:furnace` across the 50 supported protocol profiles. The legacy `minecraft:lit_furnace` registry entry uses the same furnace implementation through protocol 340. These client activations produce success even when the server cannot open a menu. Prediction remains separate from server-observed `UseBlockOutcome` and does not open a menu or change blocks, stacks, or block-entity data.

## Native decision and runtime owner

`BlockUseActivationProfile` carries a separate menu-availability bit. This enables the three menu families from protocol 47 while preserving the existing lever/button band from protocol 498. The existing sender keeps its pre-send permission, predicted result, and swing policy. `InteractionActions.Placement` adds one guard requiring packet-established chunk availability for these new successful menu predictions: a block update can create storage without establishing native terrain. Such context remains `Unknown` and sends only the use packet.

| Native boundary | Behavior retained |
|---|---|
| 47, profile 1.8.9 | Boolean client success; legacy `block_place` precedes swing. Offhand is rejected before main-hand fallback. Other 1.8 names retain their source audit without selecting a different protocol-47 client profile. |
| 107 | Both supported hands can activate; native cooldown checks occur after a successful block activation. |
| 315 | Native listener removes the explicit stack parameter; the menu result remains client success. |
| 393 | Flattened states retain the same menu decision. |
| 477–498 | Crafting-table activation calls inherited client menu/stat callbacks before returning constant true. The original local-player hierarchy inherits the no-op menu/stat methods; no callback changes this result. |
| 573 onward | Native block methods return `InteractionResult.SUCCESS` on the client; server-only menu outcomes remain separate. |
| 766 onward | Inherited item interaction falls through to default block use only for the main hand. An offhand pass can continue to ordinary item use. |
| 768 onward | The controller's spectator consume behavior remains unchanged. |
| 777 | Menu result is known success; the existing placement swing is local only. Data-driven item transformer results remain outside this slice. |

Secondary use suppresses activation when either supported hand has an item. Sneaking with empty hands still activates. A successful activation precedes item cooldown and Adventure item-use permissions. In older controllers it also bypasses the conditional block-item placement precheck, so a held stone stack can activate a chest even when the adjacent placement destination is occupied. Held-item feature permission and the existing border checks retain their own pre-send ordering. This change does not classify other menu blocks, doors, or fluid interactions.

Missing or wrong block entities, chest obstruction, lock failure, and server menu-provider availability do not turn these native client activations into pass or fail. The original client methods return before those server checks. For the unconditional crafting-table methods at protocols 477–498, the audit follows the exact inherited `openMenu` and stat overloads and verifies that the local client hierarchy does not override them. The newer menu hierarchy has no item-interaction override; the inherited fallback remains distinct from an item effect.

## Original source evidence

`/home/anon/Projects/MinecraftOfficial/runs/menu-activation-audit-20261022-v1` retains the audit, original sources, `.complete.json` and artifact receipts. It verifies both SHA-1 and SHA-256 of every official client jar used and the matching decompile receipt for all 75 supported named releases, which map to 50 protocol datasets. The audit manifest is `80d56fe203540e08a5a3b453d850af072c0084fc6c17382cf0d4ff7ecf13771d`.

`native-source-audit.json` (`ff9a88695328810e4bc747bfb70b464ba47a40548df9bf5ef96d0236e78bbb6b`) identifies each release's registered menu classes and source hashes. `method-classification.json` (`73069dbc4ed8358804feefe0af897addd04f203b954353e4b725b2bb775fd66f`) retains 249 original menu method bodies, including the named legacy lit-furnace aliases. `context-classification.json` (`ae7b5d9c6130b6125c92e29298bcb6993e4aac5338c11ef3bc3d5415da94f977`) retains the original controller and input methods for every named release. `callback-verification.json` (`c887f9e095f36fc993edd7c668589399695aefcc9f2c6b3e74ad5ab2de4442c0`) records the inherited callback and override proof. The bounded lock-context appendix records the original 1.20.6 lock serializer and container checks.

Examples of the original method owners are 1.8.9 `afy.java:8`, `afs.java:302`, and `ahb.java:139`; 1.14.4 `CraftingTableBlock.java:25`, `ChestBlock.java:221`, and inherited `AbstractFurnaceBlock.use`; and the later `useWithoutItem` implementations. Legacy obfuscated symbols are bound through each release's registry and signature flow; they are not claimed as readable community mappings. No missing runtime boundary fact remained after these constant client returns and callbacks were established, so this slice starts no new native helper JVM or live server. Its native evidence is the original source, not a newly executed native-method measurement.

## Private source and runtime evidence

The private archive is `f0a84e3e1c1ff01b828c44bb81ae2fd46d0d8b8c`, SHA-256 `6bcdf1c11e92da632a2de543cc2f8030682a8e1521bfd17afae774100aa3df98`. Only `BlockUsePrediction.cs` and `InteractionActions.Placement.cs` change in its existing source entries; the new test and this report are additive. The private producer is an explicit overlay on that archive and is not identified as the immutable commit itself. No public API, dataset, feature axis, dependency, DI lifetime, package version, pin, generated file, or baseline changes here.

`MenuActivationSessionRuntimeTests` adds 93 deliberate cases. They connect an actual `UmpkClient` to `FakeJavaServer` over its framed in-memory duplex transport, complete login/configuration/play, and dispatch the real packet bindings through the owning appliers. A real full chunk establishes membership in positive cases. Missing-terrain controls send block updates without a chunk and assert both the synthesized storage and absent native column membership. Public `PlaceBlockAsync` drives the existing owner and sender. The test does not construct session services or call a prediction helper directly.

The cases cover protocols 47, 107, 340, 393, 477, 498, 573, 765, 766, 768, 776, and 777 where their source or wire boundaries matter. They assert exact use/swing order, target, face, hand, cursor quantization, ACK behavior, blocked and missing chest context, legacy lit furnace, secondary use with empty/nonempty hands, Adventure, cooldown, held block placement prechecks, and missing chunk availability. Incoming relevant packets assert decoded values and byte-identical bound-codec re-encoding; full chunks also round-trip structurally. Every observed outbound frame is decoded and re-encoded byte for byte. Primary cases explicitly verify disconnect and peer EOF; all fixtures dispose their owned client and in-memory server.

The locked-chest control at protocol 766 uses a constructed framed NBT context with native chest block-entity type ID 1 and the original `Lock` string schema. The ID comes from the extracted native registry because the runtime static registry snapshot does not expose `block_entity_type`. The fixture proves that packet-populated NBT does not suppress the client swing. It is not captured native traffic or a live proof of server lock refusal.

| Retained receipt | Executed result | Scope |
|---|---|---|
| `focused-red-attempt2` | 3 failures, 3 passing controls | Missing menu swings through real sessions before the production change. |
| `focused-minimal-green` | 6 passes | The initial profile change fixes that native decision. |
| `expanded-context-red-attempt2` | 9 failures, 74 passing controls | Successful activation incorrectly asserted terrain after block updates without native chunk arrival. Protocol 777 remains a wire control because its local-only swing emits no packet. |
| `terrain-helper-only-red` | Same 9 failures and 74 controls | An unconnected terrain classifier does not fix the production caller. |
| `focused-context-green` | 83 passes | The existing owner now applies the terrain guard. |
| `focused-held-block-green` | 93 passes | Ten additional source-backed held-block placement-precheck controls pass with the same production code. |
| `adjacent-session-and-tools-green` | 4,151 passes | Existing activation, hoe/shovel/axe, cooldown-session, attack-input and core tick failure checks plus the initial 83 menu cases. |
| `full-client-final-green` | 26,829 passes | All Client tests on the final private source: base 26,736 plus 93 deliberate cases, including the ten later controls. |
| Scoped production and final test format | Exit zero, zero files changed | Both production files and the final test pass `dotnet format --verify-no-changes`. |
| `compiled-all-source-check` | All 482 documents match | The actual portable PDBs match all 202 Client and 280 Client.Tests source documents. |

Every accepted test receipt has zero skips. Exact command arrays, working directories, source hashes, complete logs, and TRX files remain under `/home/anon/Projects/MinecraftOfficial/runs/menu-activation-runtime-20261022-v1`. The SDK is the retained official .NET 10.0.401 toolchain. The focused command is `dotnet test tests/Umpk.Client.Tests/Umpk.Client.Tests.csproj -c Release -m:2 -nr:false --no-restore --filter FullyQualifiedName~MenuActivationSessionRuntimeTests`, with separate named TRX output. The final full Client command uses the same project and Release options with no filter. Receipt JSON preserves the exact logger and results-directory arguments.

Earlier attempts remain separate: a fixture property typo failed compilation; the first locked-context attempt failed because it queried a registry absent from the static snapshot; and initial test formatting required import ordering. Static audit classifiers initially confused overloaded calls, the 1.11 parameter removal, and an obfuscated 1.14 guard; their failed scripts and classification notes remain preserved. The first full run observed 26,819 passes for the earlier 83-case source while the final ten controls were being added; it is superseded and is not accepted as the final source/PDB receipt. The fresh final 26,829-case run starts after all C# edits and format operations stop. No assertion, expected count, or native budget was weakened.

The accepted Client DLL SHA-256 is `3285cd2d629b053e9f3aff327f08b1ed715c8e50ccc733eccef3ba57ac0ef905`, and its PDB is `d3af27864c3f987c7b7aff2951a48ea2d1c4859332172029f2228414244ce914`. Client.Tests DLL is `748f7c93bcb9a0923cb6f3a2341b7b519e0d89a70011be72c045ef4ff32eac8f`, and its PDB is `b6a07276884eb59cee00154404813b06ae20b925f0ab9a7aeedcbc4b6fb1cff2`. The source/PDB audit records each compiled document hash independently.

## Remaining acceptance

These are source-backed full-session runtime tests over an in-memory transport. They make no native live-server, graphical menu, movement-phase, TCP, or server menu-opening claim. No shared checkout, sibling source/Git state, shared seed, game port, or RCON port is written or used. The final combined immutable producer still needs repository gates, fresh consumer and Native AOT/package checks, published-head CI, and the scoped live interaction acceptance. The broader placement and activation inventory remains open.

## Merged immutable checkpoint

The merged [menu and view-targeting checkpoint](menu-pick-checkpoint-20261022.md) at `e60c36047d168660b8ffd758f489e300e6836a91` now passes all 25 repository gates, fresh 1,999 DMCBK/918 MCC consumer tests with 33 matching dependencies and unchanged siblings, strict Native AOT with one executed passing smoke at 25,079,072 bytes, and thirteen exact package/isolated-consumer libraries. All 22 locked runtime DLL/PDB files remain unchanged. The earlier overlay results above retain their original sources and counts. Native menu/picking acceptance, broader inventory and published-head CI remain separate.
