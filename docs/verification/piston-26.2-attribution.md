# 26.2 piston correction attribution

The recorded piston reproduction now has an original-client comparison. The untouched 26.2 client and frozen MCC using UMPK `548111813953d8ea8b0e5b00e6c371059b3a91c2` publish the same positions, receive the same correction and produce the same eleven-packet sequence across four client-tick intervals. The vanilla server logs `ParityBot moved wrongly!` for both. This case demonstrates behavior present in the original client/server pair; it supplies no demonstrated UMPK mismatch requiring a production change.

The historical saved world was not retained. Both comparisons reconstruct its recorded piston coordinates and materials in separate fresh isolated worlds. This closes the attribution item for that bounded reproduction, with the limits below. It does not establish universal piston parity or identify an upstream bug.

## Producer and native identity

All `runs/` paths are relative to `/home/anon/Projects/MinecraftOfficial`. Official artifacts, raw traffic, logs, worlds and executables remain outside Git.

| Input | Verified identity |
| --- | --- |
| Official 26.2 client | Untouched original entry point; client JAR SHA-256 `40896ee9f1e2bec3c934daac7e93d41e9e3d9c2f8ae0ca366d52ffbfd1afa290` |
| Official 26.2 server | Server JAR SHA-256 `cdacdfb25898de5e4b4b0e5ddcc2722f77067e46605709c2d886c000ebb63ec5` |
| UMPK producer | Immutable `5481118`; completed 25 repository gates and 26,736 Client cases required before launch |
| Runtime lock | `interaction-boundary-validation-20261022-v1/producer-lock.json`, SHA-256 `1d83ee12b8c3f19a9f3dd05ff82f48fe66293d8b68d0285c975e53ae1b33d7fa`; all 22 DLL/PDB artifacts match |
| Frozen MCC | `ea4d8f011608ad993808afa0e53ded40c0ac3f80`, from the completed 1,999 DMCBK/918 MCC consumer run with 33 dependency comparisons |
| Complete MCC manifest | All 576 files; SHA-256 `7e4f370f74731ec3e68e7e2bc0d00e3018d09fb746105deed860a6eace0ec64d` |

The native witness uses a private virtual display, software OpenGL and null audio. All 68 libraries and 5,057 official asset objects are verified, with matching existing downloads reused. It uses no authenticated account or desktop session. MCC uses its public executable with terrain, inventory, entity tracking and physics enabled; its diagnostic archive confirms those actual settings. No sibling source or Git changes are needed.

## Fixture and execution

The native and MCC captures contain the same 27 ordered RCON commands. They prepare a stone floor at Y99, clear the actor region, install an east-facing piston at `(4,100,8)`, place the player at `(5.5,100,8.5)`, equip a diamond pickaxe and select survival. Position and grounded state must be observed before the prepared/no-event markers and redstone activation at `(3,100,8)`. The five post-action position observations use the original cumulative waits of 0.05, 0.10, 0.25, 0.5 and 1.2 seconds; the final traffic tail remains 2.1 seconds. Mining is not exercised.

The accepted MCC run receives the actual `(0,0)` full chunk before activation. After the final setup teleport it emits 64 real client-tick delimiters and four grounded movement/status packets before the piston event. Its recording is 581,628 bytes under the unchanged 20 MiB cap. The separate native capture is 793,118 bytes.

The only RCON substitution increases the external setup helper's socket response wait from five to thirty seconds while world generation finishes, before MCC launches. Measured action commands still use the original five-second helper and unchanged delays. There is no warning suppression, collision-tolerance change, synthetic movement input or change to pre-777 publication order.

## Observed packet sequence

The table groups the identical ordered packets by actual serverbound `client_tick_end` delimiters. Every selected payload byte and decoded value matches; independent arrival timestamps are excluded from equality.

| Completed tick delimiters after event | Ordered packets and values |
| --- | --- |
| 0 | Clientbound piston event `(4,100,8)`, action 0, face 5, block 138; serverbound tick end |
| 1 | Serverbound position `(5.810000011920929,100,8.5)`, grounded true, horizontal collision false; tick end |
| 2 | Clientbound teleport 4 to `(6.310000011920929,100,8.5)`, zero delta, absolute flags; serverbound acknowledgment 4; position/rotation at that point with grounded false; position at that point with grounded false; tick end |
| 3 | Serverbound status with grounded true and horizontal collision false; tick end |

The correction arrives 100.410402 ms after the event in the original-client capture and 99.526095 ms in the accepted MCC capture. These are proxy arrival observations from independent client phases. They do not establish native main-thread application instants or require equal wall-clock latency. Both servers independently report the warning and the corrected position.

The original network audit accounts for all 1,547 framed packets and verifies 958 selected decoded-field assertions plus byte-identical round trips. The accepted MCC audit accounts for all 736 framed packets and verifies 413 selected field/byte round trips through the exact producer's real bound codecs. MCC's extra connection is a complete five-packet status/ping exchange; there is exactly one game session. Every byte is accounted for, no partial frame remains, and both actual directional EOFs are recorded for each connection.

## Receipts and commands

| Evidence | Location and result |
| --- | --- |
| Original client/server witness | `runs/piston-native-client-network-26.2-20261022-v1`; normal client, launcher, server and display exit; peer disconnect and closed ports |
| Original network audit | `runs/piston-native-client-network-offline-audit-20261022-v1`; manifest SHA-256 `5b11ce08eafc98ec7b929959ee6fc6f58520ac79e088e08679568d196f71d6df` |
| Accepted frozen-MCC driver | `python3 /home/anon/Projects/MinecraftOfficial/runs/piston-frozen-mcc-network-preparation-20261022-v6/mcc-piston-network.py`; exit 0; driver SHA-256 `69f95d4f9adba5328a6f02a9df5d0eb00205a4141d01d5401707bbb16432762f` |
| Accepted frozen-MCC capture | `runs/piston-frozen-mcc-network-26.2-20261022-v4`; wire SHA-256 `039defe84aa47ba4aeb94e65ad76f4babd2c973ce301be4015934a6d46519dce` |
| Exact-codec caller | Retained `piston-frozen-mcc-network-offline-audit-20261022-v1/codec-audit/bin/Release/net10.0/CodecAudit.dll`, run with the pinned .NET 10.0.401 host and fresh v3 `codec-input.json`/`codec-results.json`; no producer rebuild |
| Accepted comparison | `runs/piston-frozen-mcc-network-offline-audit-20261022-v3/acceptance.json`, SHA-256 `1c8b2df1e290056bd4e30ca9b107759a9f9eda7380ede767e6ec55d2645649bd` |
| Comparison index | Same directory's `manifest.json`, SHA-256 `5a28f4895007e2406769b719fcff9fba5fb38b1ecdb196cee009aacb3d1a7b47`; framing scripts, independent byte-field expectations, exact codec command, sequences and external receipt hashes |

The accepted MCC client, native child and launcher exit normally. All bridge tasks finish without errors; the peer observes disconnect. Root independently verifies all three owned PIDs are absent and game/RCON/bridge ports reject connections. Seed files, the frozen MCC manifest and producer artifacts remain unchanged. `root-driver-and-cleanup.json`, native `stopped.json`, connection classification and diagnostic archive retain the details.

## Retained failures and limits

The first MCC capture completed the action but failed an incorrect one-total-connection guard; its extra connection was the status/ping exchange. The next attempt failed a five-second RCON setup wait before launching MCC. The third completed the action but failed a redundant half-close after both actual EOFs. Each original failed receipt remains unchanged. Separate offline audits of the first and third captures also match the eleven-packet sequence; they are not counted as successful live runs. The corrected driver skips only the redundant half-close after the opposite actual EOF or closing state, retains unexpected errors as failures, and passes in the fresh fourth run. Two offline metadata/script preparation errors are retained separately and do not cause measurement reruns.

No assertion about all piston layouts, old unretained world contents, every protocol, mining while pushed or the full acceptance matrix follows from this result. The adjacent 777 publication control and earlier controlled phase probes retain their original producers in the historical report. The broader interaction objective remains open in the [completion ledger](interaction-completion.md).
