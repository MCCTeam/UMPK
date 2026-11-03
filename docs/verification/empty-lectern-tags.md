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

## Remaining acceptance

Exact source promotion, formatting, current producer-matched consumer and AOT checks, native live book insertion and final repository/version-matrix gates remain separate requirements. Earlier filled-lectern live results keep their original producer and scope. This checkpoint does not complete the broader interaction inventory or the 50-protocol/75-release acceptance pack.
