# Entity geometry and native trimming

Registering entity geometry through `JavaGameData.BlockInteractionData` made entity spawning retain every block-interaction table. MinimalBot only uses chat, but its native binary grew beyond the existing 24 MiB limit after the geometry change.

`JavaGameData.EntityGeometryData` now loads a separate generated geometry table. The block-interaction accessor shares that cached source and preserves its existing geometry adapter. The generator moves geometry bytes into their own blobs; byte-by-byte review confirms every preceding block, item, obstruction and replacement field remains identical, as do all 324 other existing shared-table members. Canonical dataset values and conformance pins do not change.

The comparison uses .NET SDK 10.0.401, its local Native AOT 10.0.12 compiler, Release configuration and the same `linux-x64` publish target. The prior passing commit `c0ddc1e` measures 24,364,544 bytes locally. The published geometry implementation at `35a18f7` measures 26,961,808 bytes. Separating geometry reduces that measurement to 24,481,040 bytes, saving 2,480,768 bytes and passing the unchanged 25,165,824-byte limit. Compiler maps attribute 2,348,337 bytes of the regression to newly retained block-interaction blobs.

The native bot passes `AotPublishSmokeTests.PublishedBot_DecodesBothWireLayoutsAndJoinsTheFakeServer`: it decodes committed protocol 47 and 776 frames, then joins and chats with the protocol 777 fake server. All 72 focused data tests and all 179 generator tests pass. Nine new tests cover the independent accessor's cache, compatibility, unsupported protocols and schema rejection. The Java data suite baseline deliberately increases from 1,799 to 1,808.

The generator's environment-gated golden command regenerates two descriptor fixtures, adding one geometry accessor to each. A subsequent normal build and test run copies and verifies the updated fixtures. A run with `--no-build` immediately after regeneration still read the old copied fixtures and failed; that run is not a pass.

Raw measurements, compiler maps, binaries, command logs and the generated-table review stay outside Git. The evidence is preserved under `/home/anon/Projects/MinecraftOfficial/runs/entity-geometry-aot-proof-20261017-v1` on this workstation.
