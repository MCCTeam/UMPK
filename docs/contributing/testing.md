---
title: "Testing"
description: "The 17 suites, what the recorded fixtures prove, how to regenerate the pins, and why a green test run is not evidence on its own."
sidebar:
  order: 2
---

The checked-in baseline has 12,407 tests across 17 suites, and the number matters less than the shape. A protocol library can accumulate thousands of tests that check only that the code ran without an exception. That property is weak here. A packet with a real wire id but no bound codec can be relayed verbatim while the session stays up and the decoded data remains unavailable.

So the tests are organised around assertions that a missing or wrong implementation cannot satisfy: decoded values from real bytes, pinned bindings, and expectations written independently of the data they check.

## The suites

Counts below are the baseline totals in `engineering/testcounts/expected_counts.json`. The count detector checks each run against them.

| Suite | Baseline total | Covers |
| --- | --- | --- |
| `Umpk.Protocol.Java.Tests` | 4,574 | codecs, framing, login and encryption, chat signing, command trees |
| `Umpk.Client.Tests` | 2,623 | appliers, client state, navigation, physics profile conformance |
| `Umpk.Data.Java.Tests` | 1,557 | the generated tables and the dataset they came from |
| `Umpk.Pathfinding.Tests` | 1,045 | the planner and its execution templates |
| `Umpk.Protocol.Java.Conformance` | 872 | corpus replay, the pins, the marker allowlist |
| `Umpk.Game.Tests` | 484 | blocks, entities, inventory, registries |
| `Umpk.Physics.Tests` | 292 | the tick-accurate movement engine and its vanilla traces |
| `Umpk.Text.Tests` | 218 | chat components and styles |
| `Umpk.Auth.Tests` | 136 | Microsoft flows, Yggdrasil, the token store |
| `Umpk.Core.Tests` | 144 | identity, geometry, events |
| `Umpk.Nbt.Tests` | 118 | NBT reading and writing |
| `Umpk.Commands.Tests` | 85 | Brigadier trees and argument types |
| `Umpk.Data.Lang.Tests` | 78 | translation tables and formatting |
| `Umpk.DataGen.Tests` | 67 | the dataset validator and the emitter, against golden files |
| `Umpk.Realms.Tests` | 53 | the Realms API client |
| `Umpk.IntegrationTests` | 46 | real sockets, local servers, the live matrix |
| `Umpk.PacketRecorder.Tests` | 14 | the capture tool |

Nineteen project directories sit under `tests/`. Seventeen are test suites. `Umpk.TestKit` is a shared library rather than a suite, and `Umpk.Benchmarks` is BenchmarkDotNet.

The suite that surprises people is `Umpk.Protocol.Java.Tests` at 4,574. That is what per-protocol codec coverage costs when there are 49 protocols and a packet can have seven wire forms across them.

## Intentional skips

The default test run can report environment-gated skips. A skipped test did not pass.

- The Native AOT smoke test needs a published `MinimalBot` executable. The dedicated `AOT publish smoke + size budget` CI job publishes the executable and runs this test with `UMPK_AOT_BINARY` set.
- Four live-server tests need `UMPK_NIGHTLY=1` and isolated, provisioned vanilla server directories. They run only when those prerequisites exist.
- The vanilla citation test needs local decompiled Mojang trees under `MinecraftOfficial/` or `UMPK_ORACLE_ROOT`. Those non-redistributable trees are not present on ordinary CI runners.
- Two directory-symlink cycle tests run only on Linux because Windows test discovery commonly lacks permission to create symbolic links.

## The fixture layer

`fixtures/` is about 209 MB. It is evidence, not scaffolding.

### Recorded captures

`fixtures/corpus/` holds 121 packet captures recorded off live vanilla servers, one or more for each protocol. Each binary `.umpkcap` has a JSON manifest beside it, for 242 files in total.

What a capture proves is the thing nothing else can prove: that a codec decodes bytes a real server actually sent. Every other test feeds the codec input that somebody wrote while thinking about the codec. The captures establish boundaries such as the chunk-tail boolean at protocols 762 and 763: the recorded 1.19.4 frame parses only with the boolean, while the recorded 1.20.1 frame parses only without it.

Treat these files as irreplaceable. They came off live servers and they are not reproducible on demand. Do not delete or re-record one without agreement.

### Codec identity pins

`fixtures/codec-identity/` has one text file per protocol, 49 in total. Each line records which codec class is bound to which wire id, and how many bytes it consumes from a canonical probe payload:

```text
Play Clientbound 0x00 minecraft:keep_alive PlayKeepAliveCodecs.ClientV1_8 read:1
Play Clientbound 0x01 minecraft:login JoinGameCodecs.V1_8 read:10
Play Clientbound 0x02 minecraft:chat ChatCodecs.ClientLegacyV1_8 fault:ComponentFormatException
```

The codec name catches a rebinding. The byte count catches an edit to a codec body that the name cannot see. Both matter, because the wrong-codec case is worse than the missing-codec case: a packet bound to a neighbouring era's codec is recorded as implemented exactly like a correct one, and it will decode plausible garbage or kill the session.

### Registration pins

`fixtures/registration/` is the coarser twin, also 49 files: the packet id table per protocol, with each entry marked `codec` or `marker`. A packet quietly losing its codec shows up here as a one-word diff.

### Command frames

`fixtures/commands/` holds recorded declare-commands payloads for seven protocols, both the default tree and the operator tree a server sends after an `op`. A wrong argument-parser table can still consume the right number of bytes while silently shifting every parser name. Assert the names, not only successful decoding.

## Regenerating a pin

A pin is regenerated deliberately, never edited by hand.

Do not edit a pin file to make a test pass. A failing pin is telling you that a binding moved.

1. Regenerate the codec identity pins.

   ```bash
   UMPK_UPDATE_CODEC_IDENTITY_PINS=1 dotnet test tests/Umpk.Protocol.Java.Conformance
   ```

2. Regenerate the registration pins.

   ```bash
   UMPK_UPDATE_REGISTRATION_PINS=1 dotnet test tests/Umpk.Protocol.Java.Conformance
   ```

3. Regenerate the DataGen golden files.

   ```bash
   UMPK_UPDATE_GOLDEN=1 dotnet test tests/Umpk.DataGen.Tests
   ```

4. Read the resulting diff line by line.

5. Explain every moved line. A moved line is either a change you intended or a defect you just found.

6. Run the full suite again without the environment variable.

The intentional marker allowlist in `tests/Umpk.Protocol.Java.Conformance/IntentionalMarkers.cs` has no regeneration switch, on purpose. Add an entry by writing one, with the exact protocol set and a written reason. The gate rejects an entry whose reason is too short or ends without a full stop.

## The count detector

Read this part even if you skip the rest.

`dotnet test` on a multi-project solution prints one summary line per project and no overall total. If a test host crashes, hangs and is killed, or exits mid-assembly, the aggregator still prints that project's line using only the results it received before the host died. The line reads `Passed! - Failed: 0, Passed: N` with N silently smaller than the real suite size. It is green. It is also missing tests, and there is no way to tell by looking at it.

The per-suite totals therefore must be reconciled against a baseline:

1. Run the suite and capture the log.

   ```bash
   dotnet test UMPK.sln 2>&1 | tee /tmp/test.log
   ```

2. Run the detector.

   ```bash
   python3 engineering/testcounts/check_test_counts.py /tmp/test.log --baseline engineering/testcounts/expected_counts.json
   ```

3. The detector must exit with code 0. It exits 0 only when every expected suite appeared at its expected total and no abort or crash text appeared anywhere in the log.

When you add or remove tests on purpose, update the baseline.

1. Run the full suite.
2. Edit the affected suite total in `engineering/testcounts/expected_counts.json`.
3. State the old total and the new total in the commit message.

Do not update the baseline to silence a mismatch you have not explained.

## Writing a test here

Three habits, in the order they matter.

Write the failing test first and watch it fail. Not as ritual: watch what the failure output actually says. A test that fails for a different reason than you predicted is telling you your model of the bug is wrong, and that is the most useful five seconds in the whole cycle.

Assert values, not survival. For a codec, decode a real frame, assert the decoded fields, then assert that re-encoding produces byte-identical output. For an era boundary, state the expectation independently of the dataset, as [era gating](../concepts/era-gating.md) describes.

Do not weaken a test to make a build pass. If a test is wrong, prove it is wrong, and say so in the commit message.
