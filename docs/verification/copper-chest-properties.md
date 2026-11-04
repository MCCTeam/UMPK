# Copper-chest state properties

## Placement defect

The eight copper-chest variants on protocols 773–777 used a six-direction facing property and the piston values `normal`/`sticky` for `type`. Their 24 states made that incorrect 6 × 2 × 2 combination appear valid. Native copper chests instead use four horizontal directions, the chest values `single`/`left`/`right`, and waterlogging. Interpreting a received state with the wrong domains can select the wrong neighbor state during a tool action or placement check.

The correction adds the eight copper-chest names to the existing ChestType override in `data/java/shared/block-attributes.json`. The existing generator then selects the matching horizontal facing domain. The resolver, packed reader, APIs, feature axes, dependencies and DI lifetimes remain unchanged.

| Protocol | Supported names | First copper-chest state | States per variant |
| --- | --- | --- | --- |
| 773 | 1.21.9, 1.21.10 | 26,893 | 24 |
| 774 | 1.21.11 | 26,893 | 24 |
| 775 | 26.1, 26.1.1, 26.1.2 | 27,095 | 24 |
| 776 | 26.2 | 29,568 | 24 |
| 777 | 26.3 | 32,925 | 24 |

## Original native observations

Five serial native executions verify all 40 block variants and 960 state identities. The original registry, state-definition, property, default-state and state-ID getters report facing `[north, south, west, east]`, type `[single, left, right]`, and waterlogging `[true, false]`. Each default is north/single/false. All 18,725 loaded original class occurrences match the official bytes and origins. The helper creates no world, player, controller or server and replaces no original method.

Source bindings cover eight named releases. Sixteen relevant source roles match byte for byte across the aliases; the aliases have no additional JVM execution claim. The retained bootstrap helpers are reused with their exact hashes. `ACTUAL-NATIVE5.json`, SHA256 `069dacc208cb7e8b8990617e8ebe9d83818d54e16ac965b07eec40dbaa47db78`, in `.scratch/copper-chest-domain-independent-review-20261104-v1` indexes the artifacts, sources, commands, observations and cleanup. Cleanup `e479bc4967af19b761e5cdb223c23f4363ee7dabf828b3a2169eeebc9c6cbeb2` proves all seven observed HOST processes exited, the exact unit is inactive with MainPID zero, and its cgroup is gone.

## Generated and managed acceptance

The isolated trial starts from the admitted `beda621` producer. The retained generator matches all twenty-five authored and three SDK-generated source documents; one RegexGenerator-generated document is unavailable and remains an explicit provenance limit. Baseline regeneration reproduces all 61 files exactly. The five new native-state theory rows fail at the expected facing comparison, and all existing 2,488 data tests pass without skips. After the exact eight-name override, dataset verification passes. Fresh generation changes only `SharedTables.g.cs`; its SHA256 is `a3d670a348f6ad699780ba17b2583b3b39dd186b6fb78e89cb1bcc9905ca7b97`. A decoded field review admits exactly forty block/protocol changes to the two property domains. Attributes, state flags, IDs and other protocol members remain unchanged; the other sixty generated files are byte-identical.

The subsequent owning Data.Java compile fails with a Roslyn `OutOfMemoryException` in nullable flow analysis. The attempt reaches 3,808,788,480 bytes under the unchanged 4 GiB limit; all cgroup memory-limit/OOM-kill event counters remain zero. The compiler and tests retain nullable checking, warnings-as-errors and their analyzers. The five GREEN and full 2,493 runs have not executed. The deliberate baseline increase of five is an expectation for the correction, not a claimed passing count.

`completion.json`, SHA256 `2f99d6c66b7b3bbfb988a51cf9b6ae45480d731616606c225e1c32e9e110e514`, in `/home/anon/Projects/MinecraftOfficial/runs/copper-chest-properties-managed5-20261104-v1` records every exact command, TRX, generator output, compiler input and source/PDB prefix. Its `CONFIGURATION.json` is `21c450c302ada3e9aaf23915ea0637cd0ba0c571c49a5fece6d23309019b235d`; the reviewed runner is `f989c529992ccfa02425b59491116b40427a21ea3238888b434fa0a80e563670`. Cleanup `7429817448223ad1c647c95d26f310a6aac0ecca93a584a17d69e6157027fa99` verifies all nineteen observed HOST identities absent, MainPID zero and the cgroup removed. The original 39 producer files, twenty references, four generator files and test pair remain unchanged. No automatic retry or memory increase follows this failure.

The correction is submitted for the normal PR build and data tests. The existing package-validation job will retain its verified packages with explicit source and package hashes so the resulting Data.Java DLL/PDB/XML can support a bounded local test continuation. This adds artifact retention to PR CI; it publishes no package or release. Remote build, local GREEN/full 2,493 and corrected producer admission remain pending.

## Caller and final acceptance

The data defect appeared during the real-session copper-chest tool-prediction test. Its earlier attempt stopped at that setup error after 21 causal failures and one passing control; it supplies no accepted 23-case RED/GREEN result. The corrected data must feed a fresh producer freeze and that caller campaign, modern stair placement, live tests and final consumer/AOT/package/CI gates. This state-table correction alone does not close those behaviors.
