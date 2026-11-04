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

The subsequent owning Data.Java compile fails with a Roslyn `OutOfMemoryException` in nullable flow analysis. The attempt reaches 3,808,788,480 bytes under the unchanged 4 GiB limit; all cgroup memory-limit/OOM-kill event counters remain zero. The compiler and tests retain nullable checking, warnings-as-errors and their analyzers. That attempt stops before the five GREEN and full 2,493 runs. Its failed status is retained separately from the successful continuation below.

`completion.json`, SHA256 `2f99d6c66b7b3bbfb988a51cf9b6ae45480d731616606c225e1c32e9e110e514`, in `/home/anon/Projects/MinecraftOfficial/runs/copper-chest-properties-managed5-20261104-v1` records every exact command, TRX, generator output, compiler input and source/PDB prefix. Its `CONFIGURATION.json` is `21c450c302ada3e9aaf23915ea0637cd0ba0c571c49a5fece6d23309019b235d`; the reviewed runner is `f989c529992ccfa02425b59491116b40427a21ea3238888b434fa0a80e563670`. Cleanup `7429817448223ad1c647c95d26f310a6aac0ecca93a584a17d69e6157027fa99` verifies all nineteen observed HOST identities absent, MainPID zero and the cgroup removed. The original 39 producer files, twenty references, four generator files and test pair remain unchanged. No automatic retry or memory increase follows this failure.

## Source-verified remote build and local continuation

All seven jobs pass on published `84b797f`, run `41768780354`. The actual checked-out merge is `b661aeb97caf65ffe1bc3b58a3c1b1adf27c7f0f`, recorded separately from that source-equivalent PR head. Package job `126373320879` verifies fourteen primary and thirteen symbol packages and builds its consumer. Artifact `13293872814`, SHA256 `783b45b7c744f6c1818e3397c474f6a316295174bb554aa660830f11ca025f38`, retains those packages and a producer manifest. All 27 archive hashes and all 1,330 tracked source-file hashes match. This retains build evidence and creates no release or public package publication; that CI package consumer supplies build proof.

Only the Data.Java package DLL/XML and symbol PDB are imported. Its DLL is `669f88e27f0e5bd9fee1d4ce36d26aff1d4c1fc1ec29f1a06b2ee3b9ed9ea7eb`, PDB `d4de07442b9b6a8f898c2343d2effd7e38b74f1e6e2f829cb7943c0a01f21980`, XML `010d30b8af7e61d71b3eff7cb35ed31e68c91d4e9f0ad2f0401ece59025cf164`. The actual PE/PDB identities match, and all 67 project-source document checksums match the reviewed source. Three CI SDK-generated document hashes remain recorded metadata without a claim that their original paths exist locally. The XML changes member order; every one of its 213 member names and contents remains equivalent. The new XML retains its actual byte hash.

The bounded continuation reuses the original compiled test pair, which was compiled against the earlier Data.Java reference, and runs it against the imported corrected library. All five GREEN cases and the full unfiltered 2,493 execute and pass with zero skips. The exact name multiset is the old 2,488 plus the same five causal cases. No compiler, prior RED run or generator repeats. All 4,440 source files, 136 runtime files and input pins remain unchanged. Both tests finish in 4.862 seconds with peak 462,016,512 bytes and no memory-limit events. All seven HOST identities are absent, MainPID is zero and the naturally unloaded unit/cgroup are gone; an initial cleanup-only stop reports that absence and does not repeat the process.

`ACTUAL-DATA-REMOTE5.json`, SHA256 `459e361112073b8bc8cdb41acd70ce43a3759a5ee47a9513e168c994eb2c748d`, in `.scratch/copper-chest-remote-continuation-preparation-20261104-v1` indexes the exact commands, TRXs, old compiler reference, source mappings, actual package origins and cleanup `44ba747cb3cd913aa4251845a9a66ba7282cad5f82addc21d7993138b1dbf27d`. The isolated runtime is physically beneath its immutable solution root. An earlier source-preparation attempt lacks the external Test SDK Program source mapping; the correction copies its exact checksum-matched source and starts no compiler.

The corrected thirteen-library freeze is authored-source-equivalent to `84b797f`: `PRODUCER13.json` SHA256 `eba1d8151a82f2919f59fa72f5b4479677a37d8817f1625b0a4f01b305c52d5c`, source proof `36d534f6c7c8b8d551c18d4a0a27634aa032c89b49116fa7c9b3d1c042387014`, under `runs/final-interaction-producer13-copper84b-20261104-v1`. All 1,280 project sources/PDB documents and thirteen project/API groups match. Its other twelve DLL/PDB/XML triples remain byte-identical to the admitted `beda621` producer. This is a composed source-equivalent tuple with separate build origins, rather than a claim that every library was rebuilt at this commit.

The independent CI log audit `ec0643a7e9977f3412b5a8aa751e265e72cd5455a8b8c60014ecc3df752cc7c5` verifies all seventeen Ubuntu totals: 52,035 cases, comprising 52,012 passes and 23 exact named normal-suite skips, without aborts. The same source head passes the native size guard at 24,844,968 bytes, strictly below 25,165,824, and executes exactly one passing AOT smoke with zero skips. These are the CI producers; fresh consumer/AOT/live acceptance of the composed local tuple remains separate.

## Caller and final acceptance

The data defect appeared during the real-session copper-chest tool-prediction test. Its earlier attempt stopped at that setup error after 21 causal failures and one passing control; it supplies no accepted 23-case RED/GREEN result. The corrected data must feed a fresh producer freeze and that caller campaign, modern stair placement, live tests and final consumer/AOT/package/CI gates. This state-table correction alone does not close those behaviors.
