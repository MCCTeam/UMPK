# Legacy bed exit cell data

`IBlockInteractionSource.TryGetBedExitCell` supplies the original support and material predicates for protocols 47 through 404. Fourteen canonical tables contain 64,253 measured states and 1,639 explicitly rejected metadata inputs. Missing measurements and rejected metadata remain unknown. Existing providers retain that default through the optional interface method.

## Native measurements

`/home/anon/Projects/MinecraftOfficial/runs/pre-pose-bed-exit-cells-all-20261018-v1` retains 315,624 original predicate evaluations across 105,208 valid state observations and 3,404 native metadata rejections from all 27 releases between 1.8 and 1.13.2. Each state occupies the support, feet and head cells in separate invocations; the other cells contain original stone or air. The probe records every world lookup and checks short-circuit order. The feet and head material results agree for every state. Releases sharing a protocol agree on every canonical flag and rejection.

The source audit follows the native support getter through the material and full-cube methods. The measured implementations depend on state, material and constructor constants; the relevant slab and snow exceptions use their native properties. The controlled world supplies original state objects. It does not run player ticks, block entities, World.setBlock notifications or chunk packet handlers. The initial pilot stopped on invalid rail metadata; the corrected probe records only the original decoder's IllegalArgumentException as a rejection and fails on other exceptions.

The importer verifies artifact and source hashes, native predicate owners, exact read order, decoded legacy block identity, complete registry coverage and protocol aliases. The canonical schema records compact flag ranges and ordered rejected state IDs. DataGen rejects holes, overlap, invalid flags, uncompacted ranges and flattened-state rejections.

## Generated contract and tests

Interaction schema 7 preserves the schema 6 payload after its version byte and appends compact cell ranges. All eight changed blobs retain every prior payload byte; other shared members remain unchanged. The reader preserves separately supplied entity geometry and bed facings. Lookup uses the existing range binary search. No dependency or service lifetime changed.

The fourteen native lookup cases fail against the previous unknown default. The completed focused run passes 534 table and geometry tests, including all measured flags, rejected metadata, malformed packed ranges, older providers and unmeasured versions. The 59 validator cases include 35 new registry partition cases. Eight new Python tests cover invalid measurements. One green build stopped on a missing test import; the corrected run passes.

Full exact-commit gates and fresh consumer/AOT checks are recorded after completion. This data capability supplies the pending legacy player runtime integration; it does not by itself handle sleep or wake packets.
