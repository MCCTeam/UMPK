# Legacy bed-facing data

`IBlockInteractionSource.TryGetBedFacing` now returns original horizontal bed facings for protocols 47 through 404. Fourteen canonical tables cover 944 wire state IDs. Other states and unmeasured versions return unknown. Existing source implementations retain that default through the interface's optional method.

Implementation commit: `03419bfe340b7cfc87ffcdec7e741a1b4668216a`. The immutable `source-bed-facing-v1` archive supplies validation and consumer binaries. This capability supplies data for the pending legacy player runtime owner; it does not connect sleep or wake packets to the session.

## Original measurements

`/home/anon/Projects/MinecraftOfficial/runs/pre-pose-bed-facing-all-20261018-v1` records 1,152 observations across all 27 releases from 1.8 through 1.13.2. The Java 11 probe reads original registry objects and facing properties. It decodes every legacy metadata value from 0 through 15 and iterates the original flattened state registry for all sixteen bed colors. No actor or world callback is involved.

The run retains commands, original artifact hashes, unchanged original class bytes, source bindings and native method owners. The importer rejects missing or repeated states, nonhorizontal directions, changed legacy block identities, incomplete facing coverage and disagreement between releases sharing a protocol. Every bed has sixteen measured states, with four states for each horizontal direction.

Legacy native packed IDs encode the decoded state as `(metadata << 12) | blockId`; UMPK's wire identity is `(blockId << 4) | metadata`. Re-encoding an original state can collapse distinct raw metadata inputs. The table preserves all sixteen input IDs and checks the separately captured native decoded ID. The flattened table uses the original registry IDs directly.

## Generated contract

The canonical `bed-facings.json` table has an explicit identity, count and ordered state/facing rows. DataGen requires complete bed coverage against the version's block registry. The packed interaction schema 6 appends the bed rows while preserving the separate entity geometry table. Schema 5 retains its existing inline geometry decoder.

All eight changed shared blobs preserve their previous block, item, entity and replacement fields byte for byte after the schema byte. Every appended bed state matches its canonical row. All other generated members remain unchanged. Runtime lookup uses sorted arrays and binary search; no dependency or service lifetime changed.

Reconstruction is documented in [the extraction guide](../../tools/extraction/README.md#legacy-player-lifecycle-geometry). `extract_legacy_bed_facings.py --check` reproduces all fourteen canonical files from the original measurements.

## Verification

The initial fourteen native lookup cases fail against the unknown default and pass after generation and reader wiring. The final focused run passes 506 table/geometry cases and 24 validator cases, including malformed packed rows, existing providers, unmeasured versions and preservation of separate geometry. Seven new Python tests cover invalid native imports. A misplaced test import interrupted the first green build; the corrected run is recorded separately.

All thirteen exact-commit gates pass under `pre-pose-bed-facing-validation-20261018-v1`: 34,732 .NET tests, seven opt-in skips, 228 Python tests, matching totals for all seventeen suites and identical output for all 61 generated files. All 4,071 archived source blobs and links remain unchanged. The deliberate additions are 26 Data.Java and 24 DataGen cases.

Consumer validation passes under `pre-pose-bed-facing-consumers-20261018-v2`: DMCBK 1,999 tests, MCC 918 tests and all 33 dependency-binary comparisons. Before/after snapshots confirm unchanged sibling source and Git state, covering 756 DMCBK files at `af22688e783fb2876ad55f06e0654a7a4c6c5641` and 515 MCC files at `ea4d8f011608ad993808afa0e53ded40c0ac3f80`.

The original queued consumer wait ended with signal 143 before restore or tests began; its cause is unestablished. Its dependent AOT wait was stopped. Those waits supply no consumer or AOT pass. The fresh v2 runs use completed prerequisites and separate output directories.

Native AOT publish and smoke pass under `pre-pose-bed-facing-aot-20261018-v2`, with producer binaries unchanged. The executable is 24,727,168 bytes against the unchanged 25,165,824-byte budget; SHA-256 is `08ef3d06d22dfb58138c87748f4f5e8d66e628bdac240a110526380c3700399f`.

No new live-server behavior is claimed for this data-only capability. Intact-bed exit support/material rules, player lifecycle ownership, eligible world ticks and packet ordering still need runtime integration. The separate [legacy lifecycle component](legacy-player-lifecycle.md) retains its measured scope.
