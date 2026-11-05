# Native item cooldown ownership and player ticks

Tool block use must consult the cooldown that vanilla associates with the held stack. A 1.21.2+ cooldown packet names an identifier group; UMPK previously stored its hash as an item ID, then queried the numeric held-item ID. This allowed a local hoe/shovel swing while the native item group was cooling down. The stored cooldowns also never expired on the session clock and survived replacement of the native player.

This change gives `SelfState` one internal native interval owner. Legacy packets retain their numeric item keys; modern packets retain exact `Identifier` keys. Modern lookup reads the effective modeled `UseCooldown` component, including prototype defaults, patch overrides, component removal, and the default item identifier when its optional group is absent. The production block-use predictor uses that owner before interpreting a tool result. A cooldown keeps the existing packet-only block-use behavior. Successful block use does not start a cooldown locally: native `ItemStack.useOn` does not call the air-use/finish-use component side-effect wrapper.

## Native sources and scope

The evidence uses the retained official 1.9, 1.14.4, 1.21.1, and every named release from 1.21.2 through 26.3. `native-source-manifest.json` records the SHA-256 of 18 verified client jars, their artifact manifests and completed source metadata, and each inspected source file. The completed source metadata's artifact SHA-1 also matches its official jar receipt. No jars or decompiled source enter Git.

| Behavior | Native source and implication |
|---|---|
| Numeric item identity | Original 1.9 `adp` and 1.14.4/1.21.1 `ItemCooldowns` key intervals by item. Protocol 47 has no corresponding cooldown packet. |
| Exact modern group | 1.21.2 `ItemCooldowns.getCooldownGroup` and 26.2 equivalent use the effective `UseCooldown.cooldownGroup`, falling back to the built-in item identifier. The packet listener removes only on duration zero and otherwise adds the supplied signed duration. |
| Interval arithmetic | Both original eras compute the end counter with Java int addition, compute duration with int subtraction before float conversion, query the remaining fraction at partial tick zero with float arithmetic, and retire entries after incrementing the counter when `end <= tickCount`. Negative durations, float rounding, and counter wrap therefore cannot be replaced with a nonnegative decrementing duration. |
| Native phase | `Player.tick` advances cooldowns after inherited living/input movement and attack-strength bookkeeping, before the local player's movement publication. The existing UMPK session tick supplies that phase even with physics and input disabled. `TickRateManager.isEntityFrozen` exempts players, including when no world steps remain. |
| Player lifetime | `Player` constructs its cooldown owner as an instance field. The packet listener creates a new `LocalPlayer` for every respawn, including keep-data masks; configuration reentry drops the player. World storage replacement alone is a distinct lifetime and keeps the cooldown owner. |

| Local-player tick gate | Protocols and named releases | Observed source behavior |
|---|---|---|
| Current native column | 767 lower control (1.21.1); retained older profile | `LocalPlayer.tick` checks the current native chunk. A block update that merely synthesizes a cached column does not establish packet-received terrain. |
| Unconditional player tick | 768 (1.21.2, 1.21.3) | Inherited player ticking runs without a terrain gate, including before the first position packet. |
| Player load timeout | 769–773 (1.21.4 through 1.21.10) | The player decrements its 60-tick load timeout before checking readiness, so its first inherited tick occurs at tick 60. The counter starts with the player, before initial positioning. A latched native load is retained after unloading. |
| Connection load status | 774–777 (1.21.11 through 26.3) | The native local player consults connection-owned load status. This band does not inherit the earlier player's 60-tick fallback. |

The existing `MovementReadiness` owner now supplies both native player tick eligibility and the placed-player movement result; no new timer or readiness tracker is introduced. Its reset runs for a new player even when terrain storage is disabled. `EntityMembershipApplier` observes native chunk headers as core self context independently of terrain/entity storage. This allocates no retained terrain world and changes no DI lifetime. The protocol-47 chunk rules remain in the existing availability owner.

## Runtime checks

`CooldownRuntimeTests` sends real bound cooldown and inventory packets, asserts decoded values, verifies byte-identical re-encoding, and calls the production block-use action. Its 48 numeric/default-group cases cover every dataset from 107 through 776. Eighteen additional cases cover explicit/default component groups, a controlled modeled prototype, removal, replacement, and namespaces at protocols 768, 776, and 777. A custom tool patch remains unknown after cooldown removal; it is not promoted to a proven tool result. Protocol 777 remains an unknown tool-result control.

The deliberate additions are 66 packet/action cases, 89 real-session cases, 50 native-counter comparisons, and 15 readiness boundary cases: 220 in total. The existing five readiness cases keep their assertions; the old current-column control is corrected from protocol 768 to its source-backed lower control 767. Seven existing direct-terrain fixture files explicitly mark their intended known native columns; the new chunk-context negative and positive scenarios still use actual bound chunk/unload packets.

`CooldownSessionRuntimeTests` uses `UmpkClient`, a framed in-memory server, and the existing session tick source. Every fired tick is acknowledged after the complete owner-loop tick. Keep-alive barriers capture the whole use/swing packet group without adding ticks. Native chunks, unloads, cooldown packets, inventory, tick freeze/step, positions, respawns, and configuration reentry use bound codecs with decoded assertions and byte-identical round trips. The tests distinguish actual native terrain from block-update-only cache entries and verify Inventory-only sessions with no terrain or entity storage. The surviving public cooldown projection also proves session teardown and player replacement cleared the owner.

The original 1.9 and 26.2 cooldown methods were executed for 28 intervals and 140 observations per era. Their observations match exactly. The 1.9 execution reuses the retained unsigned jar; all 2,905 original class entries are byte-identical to the official jar. The modern witness overrides only group selection to use a controlled key; interval add/query/tick methods execute from the original jar. This is a counter-arithmetic witness, not a claim to have executed the whole native player loop. Fifty focused C# cases compare literal original traces at counters zero, 2^24, int max minus one, and int min, plus simultaneous expiry of multiple entries. Packet duration zero is tested as removal separately from the native helper's direct zero-duration add.

## Evidence and retained failures

The producer source base is `3132e9943d7ed5d128453a8f0bca3ce93a2e0eeb`, archived with SHA-256 `b3f55dc7440be44634ed17be0a9cfda70896ec4ae9ff55ca702f0c3dc006819f`. All builds and tests run in a private archive outside Git with official .NET SDK 10.0.401 and `-m:2`. The durable evidence directory is `/home/anon/Projects/MinecraftOfficial/runs/item-cooldown-runtime-20261021-v1`; its manifest identifies the final overlay, test receipts, exact commands, source/binary hashes, native helpers, and source citations.

| Check | Executed result |
|---|---|
| Original identity red | 9 failed and 39 passed; the modern group packets caused a spurious swing. |
| Connected identity/component green | 1,184 passed, including the original item applier and hoe regressions. |
| Initial session red | 34 failed and 1 passed across 35 real-session cases before the clock/lifetime hookup. |
| Inventory-only red | 6 failed; the 50 native-counter comparisons passed in the same run. |
| Packet availability hookup | Native tick/readiness/counter checks passed; the remaining 8 failures were the still-unconnected lifetime clears. |
| Native lifetime clear red | Eight initial real-session lifetime failures plus two reconfiguration failures; separate wire-fixture failures are excluded. |
| Inventory-only readiness reset red | 5 failed and 12 passed; a terrain-disabled respawn retained old readiness. |
| Final focused and adjacent checks | 2,295 passed, zero failures or skips. |
| Initial full client fixture failures | 24,026 passed and 33 failed across seven direct-terrain fixture classes; explicit native-column fixture context repaired their setup while retaining every assertion and test count. The focused fixture reruns passed 145 and 26 cases. |
| Final full client suite | 24,059 passed, zero failures or skips, including 220 deliberate new cases over base 3132. |
| Full solution format verification | Exit zero; the existing workspace-load warning remains recorded. |

The early 771 missing-terrain expectations were invalid: a complete per-release source audit established that the old timeout persists through 773. Those expectations and the failed compile/configuration queued-read/767 absent-sea-level harness attempts remain separately identified; none is native acceptance evidence. Some early command receipts also retained a stale `SourceRevisionId=412e29d` build metadata argument from the reused driver; their actual source archive is the hashed 3132 base, and the final green runs stamp the correct source revision. Final source and binary hashes identify the tested producer independently of that historical argument.

## Compatibility and remaining acceptance

The existing public `SelfState.ItemCooldowns` and `ItemCooldownChanged(int ItemId, int Ticks)` signatures remain unchanged. The public dictionary projects applied durations onto actual known item IDs and retires those entries on the native player tick. It does not invent integer hashes for custom identifier groups. A modern group that names a registered item can emit the existing item event; an unrepresentable custom group remains internal, so consumers of the int-only API cannot inspect that group through a fabricated ID. Tick expiry does not invent a new public event. Legacy packets retain their numeric item identity and existing event contract; signed durations now follow the native interval rules above.

The owner uses the two native key domains and the existing public int projection; no generated generic table, reflection in production, dependency, DI lifetime, package version, public signature, or timer is added. The final immutable producer must still pass the strict Native AOT size/smoke checks. This focused change does not claim all tool/component behavior, block activation, native graphical level-load completion, live server compatibility, or the whole vanilla interaction objective is complete. The combined shovel rerun, final immutable full repository/package/consumer/AOT checks, and live acceptance remain owned by the parent completion ledger.

The frozen manifest has SHA-256 `c2c637c29c76c1dedc8c3ef0705017d47644d4c1797866f0bff0d42392469d80`. Its source-overlay identity is `22892ee8a508a0558b517d0afc41baabd0f8e00c5c706fff8076235a2d07be67`; the deterministic overlay archive has SHA-256 `bd2aa68644320927f755112538b4e462c1d10c4ef4a910063e9f2dcfc613b7fe`. Root integration applies the two `ClientState` clears and one placement precheck narrowly, preserving the independently developed falling-block tick context and shovel caller.

The combined root archive over `fb3a8e4` passes all 3,326 focused cases and all 26,050 Client cases, including the unchanged 1,955-case shovel suite and its nine previously failing modern cooldown cases. Normal Integration passes 41 and skips ten opt-in cases; the launcher/RCON suite passes all nineteen. Full solution formatting and source-integrity checks pass, preserving 4,156 other base blobs. `runs/interaction-combined-focused-20261021-v1/manifest.json` binds 44 evidence files, SHA-256 `d0fd5c8164e63a02977b4282f0f103dde998b27d02040dda8e2f3ddd4ba18dc6`. The combined archive contains all three interaction slices; the cooldown commit's deliberate count change remains exactly 220.
