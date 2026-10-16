# Ordinary block replacement verification

Placing an ordinary cube through water, lava or a plant could succeed on the server while UMPK omitted its swing. The predictor now uses vanilla replacement rules for the clicked block and the resolved destination across all 50 protocol datasets. Legacy upper double plants also read the actual lower block. The operation keeps world and inventory state authoritative and sends the use/animation group before waiting for acknowledgment. Protocol 777 retains its local use swing without sending a punch packet.

This checkpoint covers ordinary cube items. Specialized item use, active block interactions and dynamic entity obstruction remain unfinished. In particular, any tracked entity whose geometry is unknown still suppresses the predicted swing, even when that entity is far from the placement cell.

## Native evidence

The accepted matrices cover all 75 stable releases from 1.8 through 26.3: 1,140,776 state observations, 128,229,448 predicate calls and 1,330 complete item-use samples. They invoke original client classes with controlled world queries; they do not execute a complete client or world tick. Receipts verify official artifact hashes, class preservation, actual class origins and inspected source hashes. Their directories under `MinecraftOfficial/runs/` are `placement-replacement-modern-all-20261016-v2` and `placement-replacement-legacy-all-20261016-v1`.

`extract_block_replacement.py` checks complete state and ordinary-item coverage before writing data. It retains clicked/destination differences and lower-block dependencies. Protocol 47 uses the latest measured 1.8.9 client profile; provenance also preserves the differing 1.8 observations. Native measurements are release-specific even when releases share a wire protocol.

```bash
python3 tools/extraction/extract_block_replacement.py \
  --data data/java --official "$MINECRAFT_OFFICIAL_ROOT" \
  --measurements "$MINECRAFT_OFFICIAL_ROOT/runs/placement-replacement-modern-all-20261016-v2" \
  --measurements "$MINECRAFT_OFFICIAL_ROOT/runs/placement-replacement-legacy-all-20261016-v1" \
  --out /tmp/umpk-block-replacement-data
```

DataGen emits 33 packed tables shared by the 50 datasets. Review decoded every new range and lower-state override and confirmed that the previous block, item and entity contract fields stayed identical. The new reader rejects malformed range counts, overlaps, flags and lower-state coverage. Unknown states remain unknown.

## Executed checks

The isolated source snapshot is tree `cc11ed7cdc03f3525dcf1f9080fc4823dbfa8809`, based on `2c450752101f9fd9d1c30104d2708b499f186689`. Build and test commands used SDK 10.0.401 and Release binaries. The gate receipt is `MinecraftOfficial/runs/replacement-placement-build-20261016-v2/gates.json`; it records the exact commands and logs. This report is a later documentation addition to the same tested implementation.

| Check | Observed result |
| --- | --- |
| Focused placement suite | 517 passed, including 455 new cases; the new cases first produced 294 failures and 161 controls |
| New data and generator cases | 61 Data.Java and 18 DataGen cases passed |
| Extraction regressions | 133 Python tests passed; seven corrupt native-input variants were rejected before writing data |
| Full solution | 19,101 passed, one failed, seven skipped; the runner reported all 19,109 expected rows |
| Existing failure | `VanillaCitationTests.EveryCitation_ResolvesInTheDecompiledTrees`, line 138, fails the source-citation count guard; the same failure preceded this change |
| Build, format, dataset, language and generation | Passed; all 61 generated outputs matched |
| Test-count gate | Failed because the full run contains the citation failure; suite totals match the deliberate additions |
| Fresh DMCBK and MCC consumers | 1,999 and 918 tests passed; loaded UMPK binaries match the isolated producers |

The DMCBK consumer includes local commit `af22688`, which preserves ray-hit details in `useitem`, refuses unreachable coordinates and removes its forced extra swing. Five command regressions first failed and then passed. The tested binaries contain the same source hashes as that commit, but were built before the commit existed. No DMCBK push or pull request forms part of this checkpoint.

## Live results and remaining failure

Five fresh survival actions passed on 1.8 and 1.8.9: placement into water, lava and plants, direct plant replacement through `useitem`, and rejection of an occupied bottom slab. Server block queries and selected-stack counts independently establish each result. Captures contain one use followed by one swing for each successful placement and no swing for the slab control. These results are in `MinecraftOfficial/runs/replacement-placement-live-boundaries-20261017-v2`.

The same matrix stopped at a real 1.10.2 failure: the four placements succeed but omit their swings. A fresh same-seed run, `MinecraftOfficial/runs/replacement-placement-walk-20261017-v1`, reproduces this after MCC walks onto the pedestal through its normal navigation command. The server and MCC both observe the support block, all five server block/count checks complete, and the capture has five use packets with zero swings. It has no movement warning. Client and server exit zero, all owned processes disappear and both ports close. The capture assertion correctly fails. Ten later requested boundary releases remain unexecuted.

Earlier reproduction attempts exposed setup faults: a support block did not survive early world preparation, another attempt reached a server/client block mismatch during initialization, and teleport-based runs logged `moved wrongly`. Those failed runs remain separate evidence. They do not count as passes. The successful movement setup does not resolve legacy teleport reply timing or prove complete entity interpolation and obstruction behavior.

The wider block interaction work remains open. This checkpoint does not establish vanilla parity for every interaction, every release or every server.
