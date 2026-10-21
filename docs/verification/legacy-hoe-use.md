# Legacy hoe-on-block prediction

`VanillaHoeUse` extends the existing session-owned block-use result to the eighteen protocol datasets from 47 through 490. Dirt and grass become successful tool uses when the selected face is not the bottom face and the block above is native air. Coarse dirt also succeeds, while podzol passes. Protocol 107 adds the native grass-path target. Successful predictions finish the captured use/swing packet group; pass and unknown results retain packet-only behavior. Terrain changes, drops and durability remain server-owned.

The legacy `minecraft:grass` name means the grass block only before flattening. The flattened `minecraft:grass` plant never becomes a tillable block through that alias. Pre-flattening dirt uses packed metadata: zero is dirt, one is coarse dirt and two is podzol. Unused soil metadata remains explicitly unknown; accepting a packed value in the protocol codec does not establish its native state normalization. Protocol 47 retains the 1.8.9 client profile for all of its named releases.

The positive tool cases require packet-established terrain through the existing availability owner. Protocol 47's shared missing-column storage is already modeled for legacy bed geometry, but those aliases are not connected to the tool predictor in this slice. A tool target without proven native terrain therefore remains unknown; these results do not close every legacy world-context case.

## Native source evidence

The retained original-client audit covers all 31 named releases below 1.14.4 and verifies client SHA-1/SHA-256, the matching decompile receipt, the native hoe registration, all five registered hoe materials and every referenced transformation target. Legacy variant enums and the original item-stack, player and world classes are included in the source manifest. This audit establishes the production terrain/result decision; it does not execute a native client or establish live acceptance.

| Boundary | Original client source | Decision |
| --- | --- | --- |
| 1.8.9, protocol 47 | `zv.java`; `agf.java`; `wn.java`; `bda.java` | Boolean tool result; dirt variants and grass; edit precheck; the controller sends legacy block placement before evaluating the item result |
| 1.9, protocol 107 | `adn.java`; `akt.java`; `zj.java` | Enum success/pass/fail result, grass path and selected hand |
| 1.13, protocol 393 | `asv.java` | Native map keyed by the separate flattened soil blocks |
| 1.13.2, protocol 404 | `asz.java` | Same bounded flattened terrain predicate |
| 1.14.3, protocol 490 | `bbx.java` | Same bounded map before the previously implemented 1.14.4 profile |

The audit is `runs/legacy-hoe-focused-20261021-v1/native-source-audit.json` under the shared oracle, SHA-256 `552cacfc9162943db3e7dd39223bf4b1c89118f9311cbc0bf312130afc207d64`. Its per-release bindings retain the obfuscated names rather than presenting them as readable MCP names. The original permission guard is owned separately: Survival and Creative use the game-mode edit permission; the existing Adventure path stays unknown. Applicable cooldown checks run before this tool profile. Original hoe methods ignore damage and unrelated legacy NBT when selecting their client result.

## Focused packet-runtime proof

The new `LegacyHoeUsePredictionRuntimeTests` contains 464 cases. All 209 expected missing-swing assertions fail on the original helper, while 255 negative and unknown controls pass. Extending only the existing helper passes all 464. After formatting, 1,612 focused and adjacent cases pass, covering this slice, modern hoes, block use, mining item identity and legacy Adventure mining. These are isolated archive results, not acceptance of a later immutable producer.

The cases exercise every applicable dataset and registered hoe material, both supported hands, dirt/grass/coarse dirt/podzol/path distinctions, the flattened grass plant, bottom-face and covered-block passes, Creative mode, damage/NBT, Adventure/cooldown guards, explicit unused-metadata unknowns and protocol-47 offhand rejection. A real full-column codec establishes terrain availability before each action. Block, inventory, game-mode and cooldown frames pass through their bound production codecs, assert decoded values and round-trip byte for byte. Full-column bodies also rebuild through the structural encoder. These are constructed wire witnesses, not newly recorded corpus traffic.

Protocol 47 sends and asserts `ServerboundLegacyBlockPlacePacket`, its held stack and byte cursor coordinates. Later protocols assert the selected hand in `ServerboundUseItemOnPacket`. Both paths assert the subsequent swing only for successful tool results and retain unchanged world/inventory state. The protocol-47 swing has a valid zero-byte body; resolving and executing its bound encoder is required even though its body is empty. Before flattening, worn tool damage travels in the slot's damage field; from protocol 393 it travels in NBT `Damage`. The fixtures assert each actual representation.

Two initial packet-alias fixture failures and a later zero-byte-swing/flattened-damage fixture failure remain separate from the corrected red/green evidence. No production behavior was changed to accommodate those harness mistakes.

```bash
/home/anon/Projects/MinecraftOfficial/toolchains/dotnet-sdk-10.0.401/dotnet test tests/Umpk.Client.Tests/Umpk.Client.Tests.csproj -c Release --no-restore -m:2 -nodeReuse:false -p:SourceRevisionId=412e29d5fa1802205b80ed83661ca1634d9715b6 --filter FullyQualifiedName~LegacyHoeUsePredictionRuntimeTests
```

Commands run from `/tmp/umpk-legacy-hoe-20261021-v1/source`, outside Git, with the official SDK as `DOTNET_ROOT` and first on `PATH`. The reviewed helper and test source, source manifests, exact commands, logs and TRX files are retained with the audit under `runs/legacy-hoe-focused-20261021-v1`. Its 284-file manifest has SHA-256 `0b38f78b7f517d6686bc24d3af15541ddb2cde3e98ee477db84e0c88ada09ce8`; 4,165 archived base blobs remain unchanged. The final post-format source-matched receipts again enforce exactly 209 expected failures/255 controls and then 464 executed passes, with no skips.

The deliberate count change is 464 new Client cases, together with removal of the obsolete protocol-490 unknown-era row from the earlier hoe test. Protocol 777 remains an unknown data-driven transformer era. The combined root archive over `a72c573` passes all 1,115 hoe cases and the complete 23,839-case Client suite without skips. Full solution formatting exits zero. Normal Integration discovery passes 41 and skips nine, retaining its total of 50; the four additional opt-in release rows do not change normal-suite discovery. The old protocol-490 unknown-era row is removed while 777 remains. The expanded live harness checks legacy soil metadata, native durability representation, independent server predicates and actual protocol-47 use packets. Those native-server pilots and final immutable repository, consumer, package, Native AOT and published-head CI checks remain pending. This slice does not close other tool/fluid families or the separate falling-block placement defect.

The combined root proof is retained in `runs/legacy-hoe-combined-20261021-v1/manifest.json`, SHA-256 `ee695477a78834d070ee2b4f056f0c1fed57a8283fe0aa46af5b2eabe49d669b`. It binds twenty files, five exact overlays and 4,163 unchanged base blobs. The strict guard verifies individual TRX outcomes, including all nine normal opt-in skips; the test adapter reports zero in its aggregate `notExecuted` field even though those nine individual outcomes are `NotExecuted`. This receipt anomaly does not turn skipped scenarios into passes.
