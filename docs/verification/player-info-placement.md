# Player-info lifetime and spectator placement

Full-cube placement excludes spectator players after the raw entity-box query and before the voxel obstruction test. This order matters: a small raw overlap can populate vanilla's player-info cache even when the voxel intersection is empty. A distant player must not populate that cache merely because UMPK enumerates tracked entities.

The session tick resolves remote-player info before motion, including sessions with terrain and local physics disabled. Missing entries retry. From 1.19.3, a found entry remains attached to that player instance after tab-list removal or replacement; entity removal, replacement and session reset discard the owning cache.

| Protocol | Repeated add | Spectator lookup |
|---|---|---|
| Through 759 | Replace the info object, including an unchanged profile | Read the connection's current entry |
| 760 | Retain the info object, original profile and constructor payload | Read the connection's current entry |
| 761 onward | Retain the info object and original profile | Cache the first found entry on the player |

Player-info updates for an unknown UUID are ignored. A modern add starts in survival mode and remains unlisted until an update sets the listed flag. The legacy add supplies its game mode, latency and display name together. On 760, a repeated add updates those mutable fields while retaining its original opaque profile-key payload.

## Native evidence

`player-info-lookup-all-20261017-v1` under `/home/anon/Projects/MinecraftOfficial/runs` contains 440 unchanged native getter observations across 44 releases from 1.14.4 through 26.3. The probe allocates owned objects without constructors and supplies UUID/profile, singleton/connection, map, cached-info and game-mode fields. It executes the original spectator predicate, player-info lookup and connection lookup. It does not execute packet handlers, constructors, networking or full entity/world ticks.

The extractor checks observation order, spectator results, cache transitions, release coverage, aliases and artifact/measurement hashes. Its fixture preserves all 44 release receipts and deduplicates the observations into two query lifetimes. Every Java helper source matches the original measurement hash. A fresh persistent-tool run on 1.19.2, 1.19.3 and 26.3 reproduced all three measurement files byte for byte in `player-info-lookup-persistent-pilot-20261018-v1`.

The packet-handler source audit covers the same 44 releases. In 1.19, the add branch constructs a replacement unconditionally; 1.19.1 adds a missing-entry guard; 1.19.3 uses `putIfAbsent`. The modern constructor initializes survival mode, while list membership is controlled separately. Source hashes and handler locations are recorded in `.scratch/player-spectator-20261018/ingestion-source-audit.json`. These are source observations, not native packet-handler executions.

## Local verification

The 282 deliberate new Client cases comprise 128 game-mode placement cases, three bounded-query cases, 44 native getter replays, five cache ownership/type checks, four unknown-update cases, 88 repeated-add cases, two constructor-payload cases and eight framed session-tick cases. The Client count increases from 20,954 to 21,236. Existing tests now assert native unknown-update handling and explicitly request modern list membership when comparing it with a legacy add.

The Release build, 303 focused/adjacent tests, full solution tests, all 17 test-count totals, format, dataset, language, extraction freshness and generated-output checks pass. The full suite passes 34,557 tests with seven opt-in skips; 201 Python tests pass. All 61 generated outputs match. Exact commands, output hashes and the unchanged source manifest are in `player-spectator-validation-20261018-v1` under the shared runs directory. The full test output was captured unfiltered in `/tmp/umpk-test.log` and preserved in that run.

## Remaining scope

The native getter measurements cover 1.14.4 onward. Older player geometry, passengers, entity pushing, modern collision-aware interpolation, specialized placement and the remaining input/version matrix are unfinished. A vanilla spectator switch normally stops tracking that actor for a survival viewer, so successful placement after that switch alone cannot prove the spectator predicate. The framed runtime cases and unchanged native getter observations establish the cache behavior independently.
