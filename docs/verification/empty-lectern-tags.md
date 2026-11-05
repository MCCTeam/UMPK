# Empty lecterns and connection-owned tags

Using a known empty lectern now predicts the native block result through the existing session owner and public `PlaceBlockAsync` caller on protocols 766–777. A received `minecraft:lectern_books` membership permits insertion with either hand, before item cooldown or Adventure item permissions. An ordinary nonbook stack in the main hand reaches the native default-block `CONSUME` result. Empty main hand passes; offhand default activation is skipped and existing item continuation handles the stack. Protocol 777 retains local-only swing prediction. The client hook does not consume a stack, insert a book, mutate the lectern or open a menu locally.

The permission decision also requires correct tag ownership. A missing registry observation remains unknown; an omitted tag inside a reported registry is known false. Configuration payloads are collected until the existing before-play gate commits them. This protects mining predicates, candle membership and book insertion from observing an intermediate configuration payload as a complete set.

## Native boundaries

The source audit covers all 19 supported named client releases from 1.20.5 through 26.3, across 12 protocol datasets. It verifies official client jar hashes, source-completion receipts, original lectern methods, tag definitions, item-stack dispatch and controller ordering. The separate tag audit binds the original tag collectors, registry loaders and both configuration branches.

| Boundary | Block/item policy |
| --- | --- |
| Play, 766–767 | Reset built-in block/item tags before each update, then apply nonempty reported registry payloads. |
| Play, 768–777 | Replace every reported registry's complete tag map; retain unreported registries. Empty reported registry establishes known empty membership. |
| Configuration, no tag packet | Preserve committed block/item facts, including when registry contents arrived. |
| Configuration, 766–767 with any tag packet | Collect the last payload for each registry; reset built-in tags once at finish, then apply nonempty registry payloads. A zero-registry packet still creates the collector and triggers this reset. |
| Configuration, 768–777, tags only | Apply each collected reported registry at finish, including an empty registry payload. |
| Configuration, 768–777 with any registry-data packet | Skip empty block/item registry payloads, retaining their committed facts; apply nonempty payloads. |

A payload with one named tag and zero members is nonempty. It replaces that registry and establishes false membership. This differs from a payload containing no tag names. Pending payloads use last-payload-wins replacement by registry, and incoming arrays cannot change the installed immutable facts after validation. Duplicate registries, duplicate tags and negative member IDs cannot partially publish state.

The implementation preserves pre-766 behavior. It certifies the relevant block/item reset and rebuild policies. Unclassified registry facts become unknown when a legacy reset or a contents rebuild could invalidate them. Existing public signatures, versions, dependencies and DI lifetimes are preserved. The existing session clock and interaction packet lifetime still own actions.

## Runtime integration

`ClientRegistryTags` owns committed and pending facts. `ConnectionApplier` selects configuration or play behavior and records the presence of every registry-data packet before filtering registry kinds that UMPK tracks. Both initial login and re-entry use the existing `UmpkClient` before-play callbacks to commit pending tags before the first play frame can be decoded or applied. A new configuration epoch clears pending collection, and connection teardown clears both pending and committed state.

The new fixtures add 288 lectern rows, 30 real-session phase cases and three direct applier atomicity cases. They cover both hands, received and missing tag contexts, all eight empty lectern states, custom components, cooldown, Adventure mode, secondary use, terrain loss, empty registries, empty-member tags, repeated registries, contents-only rebuilds, first-play ordering and disconnect/reconnect. The offhand stone continuation establishes physics before claiming clear placement geometry. The existing filled-lectern cases and action expectations remain intact. One prior configuration-applier assertion now checks deferred publication; its case count is unchanged.

The phase fixture sends real bound configuration and play packets, asserts decoded values and byte-identical round trips, and observes state at packet events and the first play packet. It uses a source-shaped generic damage-type entry to exercise the registry-contents flag for a registry UMPK does not otherwise install. That fixture proves UMPK's routing and publication; it does not claim a complete native registry-loading session.

## Validation checkpoint

The joint normal-SDK trial combines the accepted enchantment hash producer, the later Adventure codec/runtime changes and the book-placement owner. The existing compiled book owner and original native measurements are retained. Observed focused results are 286 passes/42 causal failures before tag integration, the same counts after adding the owner methods, 311 passes/17 failures after dispatcher routing, then all 328 passes after the before-play caller is connected. The separate later Adventure selection passes all 50 cases, and the combined adjacent selection passes all 1,370 cases. These runs execute without skips. The same runtime passes the complete unfiltered **36,522-case Client suite**, with zero skips. Its 36,398 distinct display names retain 124 duplicate occurrences. All runtime files except the intended Client DLL/PDB remain byte-identical, including the Test DLL/PDB and the frozen Protocol producer.

The controlled native witness has 40 original-method rows at protocols 770 and 777, with 20 loaded class byte comparisons against the original jars. It observes tag membership, item/default-hook results, native swing source and unchanged input state/count. It uses an original `ClientLevel` allocated without its constructor, with its real client-side field set true and unused position/player/hit arguments null. Full controller input, graphical animation, item continuation and a live server are outside that witness. The 770 prefix retains its original helper; the 777 continuation changes only the explicit inherited `TypedInstance.is(TagKey)` reflection binding.

The first 288-case managed attempt is retained: its 36 remaining failures split into 12 missing-physics preconditions, 14 incorrect native expectations for empty or legacy-omitted registry contexts, and 10 modern registry-knowledge defects. The corrected fixture keeps the same 288 rows and preserves its success assertions. Native wrapper preflight and observer failures are retained separately. A later native wrapper completed the 770 observations before its explicit 777 tag probe failed; the 777-only continuation reuses that successful prefix.

The normal SDK compiles one Test assembly and four Client stages: one combined baseline owner and three one-file tag changes. Each Client invocation records 225 compile items, including three generated files, 182 references and 15 analyzers; the Test invocation records 367 compile items, 199 references, 10 analyzers and 54 embedded resources. All five actual Csc apphosts use processor count one. The bounded job takes 378.35 seconds, peaks at 3,698,982,912 bytes under its unchanged 4 GiB limit and records zero final limit/OOM events. All 34 observed HOST PIDs are absent, and the exact owned unit is stopped with an empty cgroup.

The tested Client DLL is `2c208915154c9f08b4a7ea5696d9ecef8c73ae12707f302e31b959182af57c74`, PDB `4ecaee127a7da8a5e33abf1e7604a964883b1af76f2afba819de6a63cd672d81`; the Test DLL is `3c692fa31b0d645128dddb0e045112df96280701f34e13e312dcca2bfd71b2eb`, PDB `fd938502804e9ceaefd3461c1cb136ebce68314d5ff925b87e0b846a9602fcff`. The frozen later Adventure Protocol DLL is `5868755addfaf956af5338400edb3ac2165183e643e925d24146b575cdabbb20`. Source provenance is immutable `130e973` plus the exact reviewed postimages recorded in the configuration receipt. These private binaries do not yet identify a later code/documentation commit.

Exact commands, inputs, compiler source/reference/analyzer lists, binaries, TRX results and resource observations are indexed in `/home/anon/Projects/MinecraftOfficial/runs/lectern-tag-phase328-managed-20261103-v1/CONFIGURATION.json` and its completion receipt. Native evidence is indexed by `.scratch/lectern-empty-modern-20261103-v1/NATIVE-ACCEPTANCE.json`; the original source indexes are `.scratch/lectern-empty-modern-20261103-v1/SOURCE-AUDIT.json` and `.scratch/tag-reload-source-20261103-v1/SOURCE-AUDIT19.json`. Raw evidence stays outside Git.

## Native live insertion, 2026-11-03

The existing filled-lectern live test now also exercises empty lecterns through public `PlaceBlockAsync`, with physics enabled and real server tag, inventory, terrain and game-mode packets. Three isolated native servers pass all 37 scenarios on the admitted source-equivalent `d13f88e` producer. The same existing opt-in theory retains its filled scenarios and adds no normal skip identity.

| Native release | Protocol | Filled scenarios | Empty/book scenarios | Total | Captured bytes | Test seconds |
| --- | --- | --- | --- | --- | --- | --- |
| 1.20.5 | 766 | 7 | 7 | 14 | 4,354,599 | 64.17 |
| 1.21.5 | 770 | 2 | 7 | 9 | 4,976,155 | 54.50 |
| 26.3 | 777 | 7 | 7 | 14 | 5,430,181 | 60.82 |

Each empty-lectern leg covers empty main hand, an ordinary main-hand stick, writable-book main-hand insertion, written-book offhand insertion, secondary-use main book, secondary-use empty main with an offhand book, and Adventure-mode main-hand insertion. Native server predicates establish the initial terrain and exact hand stacks. After the public action, they independently verify `has_book`, the stored book identifier and count one, consumed or unchanged hand stacks, unchanged neighboring air and the unchanged lectern-interaction statistic. No insertion opens a menu. The client waits for the server-driven stack and block state; the fixture does not inject local book, inventory or world effects.

Every action has one use packet and the applicable following hand swing. Successful protocol-777 uses have no wire swing. A native sequence acknowledgment and at least two seconds of subsequent traffic precede the final observation. Received native `lectern_books` tags contain both book identifiers. The fixture replays their decoded registry/tag/member values and requires byte-identical tags, use, swing, inventory, menu, block and acknowledgment frames where selected. All observation markers round-trip exactly. Capture remains bounded by the existing 20 MiB limit.

Pass/consume classification and protocol-777 local swing remain supported by the original client methods and framed runtime tests. The live pass/consume controls establish the expected use-only traffic and unchanged native state. This headless leg does not observe a rendered arm animation.

The completed campaign selected protocols 766, 770 and 777. The earlier ledger also named the 1.21.2/protocol-768 spectator boundary. None of these 37 scenes uses spectator mode, so that native boundary remains **not run**. The framed runtime cases cover the spectator result before and after 768; a fresh 768 spectator live scenario remains required. The existing nonspectator 768 release row would not establish that result by itself. Protocol 770 was selected for this bounded campaign alongside its retained native hook evidence and modern offhand stack representation; it does not replace 768.

The combined Integration entry is compiled once against the admitted thirteen-library producer; no producer library is rebuilt. Its DLL is `6928f6f7510cb4303e14c271d6347a7436da2ebde63a2420ce29a957265d42ab`, PDB `2405d0c8fcfb45c36fa709f0c9c827a97f9b7be6dfc3b27b8ffa64bf19da4830`. The actual compiler records 51 sources, 201 references and 10 analyzers. Normal formatting changes none of the five combined live source files. The normal Integration suite passes 49 executed tests with the exact existing 22 opt-in skip identities, matching its 71-total baseline. An initial wrapper misread 71 as the passing count and stopped before any native server; its valid suite result and failed wrapper are retained. The authorized continuation reuses that suite result and runs only the three live legs.

All 136 runtime files, all 20 frozen references, the source snapshot and all three shared seed directories remain unchanged. Each native server and launcher exits naturally with code zero, the peer observes disconnect and both owned ports close. The serial native campaign takes 181.41 seconds, peaks at 1,516,982,272 bytes under the unchanged 4 GiB limit and records zero final limit/OOM events. All 16 observed HOST PIDs are absent; the exact owned unit is inactive with an empty or removed cgroup.

Exact commands, TRX results, native predicates, frames, binary/source manifests and cleanup are indexed in `/home/anon/Projects/MinecraftOfficial/runs/combined-interaction-live-entry-20261103-v1/BUILD-RESULT.json` and `lectern-live-v2/TERMINAL-ACCEPTANCE.json`. The terminal receipt SHA-256 is `beb7d92bb861530f28154649c06706249407cfa6eedb51ae80581ffe8dc233f3`. The preserved count-guard correction is `lectern-live-v1/NORMAL-GUARD-CORRECTION.json`, SHA-256 `69ba2b6bc41a68c9034ddab30553da4f5bbbd1591ee2d82f0b7a6af647b44904`. ROOT's source-equivalence admission is `.scratch/composite13-d13f88e-root-admission-20261103-v1.json`. Raw captures and server evidence remain outside Git.

## Remaining acceptance

Fresh protocol-768 spectator native acceptance, current-producer consumer, AOT and package checks, final repository gates and the complete version matrix remain separate requirements. Earlier filled-lectern results retain their original producers. These three empty-lectern boundary legs do not complete the broader interaction inventory or the 50-protocol/75-release acceptance pack.
