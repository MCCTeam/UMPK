---
title: Adding a version
description: The workflow for supporting a Minecraft release, from the server jar through the dataset, packet timelines, and verification pins.
sidebar:
  order: 3
---

There are two different changes people call "adding a version." Decide which one applies before editing anything.

If Mojang shipped another release with an existing protocol number, add a version alias in `data/java/versions.json`, then regenerate the generated outputs. The wire protocol did not change, so no codec timeline or pin should move.

If the release has a new protocol number, add a dataset directory, generated descriptor, any packet timeline bands whose wire forms changed, and the new protocol's pin rows. The steps below cover that case.

## Before you start

Never commit Mojang server jars, mappings, decompiled source, or Minecraft library jars. Keep them under the repository-local, gitignored `MinecraftOfficial/` tree, or set `UMPK_ORACLE_ROOT` to an absolute untracked location.

Run the extraction commands from `tools/extraction/`. They require Python 3.10 or later and Java 17 or later.

Write a failing test before changing behavior, and retain the failure output. For any wire boundary, record the release evidence with the change review and describe the observable rule beside the codec or timeline declaration.

## Add the dataset

1. Check the decompiled tree for the new release. The driver detects and reuses an existing tree. It reports when the offline extraction environment does not have one.

   ```bash
   python3 decompile.py <version>
   ```

2. For a modern release with server data reports, generate the reports, then extract the protocol dataset.

   ```bash
   python3 run_reports.py <version> --out /tmp/mc-reports-<protocol>
   python3 extract_modern.py <version> <protocol> \
       --reports /tmp/mc-reports-<protocol>/reports \
       --out ../../data/java/<protocol> \
       --codec-key <candidate-era>
   ```

`--codec-key` is still required by the extractor, but it is candidate provenance only. It is not a runtime binding decision: `PacketRegistrar` no longer accepts it, and the generated descriptor does not carry it. The extraction output is input to review, not an automatic verdict. Write or update the curated `features.json` and add the release name to `data/java/versions.json`. A changed feature axis can affect many protocols, so decide that change before making it.

3. Verify the dataset and compare it with the previous protocol.

   ```bash
   dotnet run --project tools/Umpk.DataGen -- verify --data data/java
   dotnet run --project tools/Umpk.DataGen -- diff --data data/java <previous> <protocol>
   ```

4. When a decompiled tree is available, use the layout oracle as a review aid before choosing codec bands.

   ```bash
   dotnet run --project tools/Umpk.DataGen -- layouts --data data/java <protocol> \
       --diff-oracle --oracle "$UMPK_ORACLE_ROOT"
   ```

Set `UMPK_ORACLE_ROOT` to the absolute repository-local `MinecraftOfficial` directory. `--oracle` may be omitted when that environment variable is set. `--against <protocol>` selects a different comparison protocol.

This is a **VANILLA SOURCE-TO-SOURCE HEURISTIC**. It compares parsed layouts from the new release's decompiled vanilla source with the previous supported vanilla tree by default. It does not compare UMPK inheritance or C# codec bodies, reads only supported `STREAM_CODEC` and `write` forms, reports unparsed packets, and is never a conformance gate. A blank diff is therefore not a guarantee that every packet is unchanged.

## Generate all derived outputs

Regenerate all three output roots after a dataset change. Start with no uncommitted changes to generated files. `--protocol-out` is required because the per-era tables used by the protocol codecs are generated into `Umpk.Protocol.Java`.

```bash
dotnet run --project tools/Umpk.DataGen -- generate --data data/java \
    --out src/Umpk.Data.Java --out-lang src/Umpk.Data.Lang --protocol-out src/Umpk.Protocol.Java
git diff --exit-code -- ':(glob)src/Umpk.Data.Java/**/*.g.cs' \
    ':(glob)src/Umpk.Data.Lang/**/*.g.cs' ':(glob)src/Umpk.Protocol.Java/**/*.g.cs'
```

The Git command must print nothing and exit with status 0 when the generated outputs are current. Review any generated diff. Never hand-edit a `.g.cs` file.

## Add packet timelines

The generated descriptor supplies `(phase, flow, wire id, identifier)`. It deliberately does not choose a codec. `PacketBindings` assembles nine family indexes, such as `WorldBindings.cs`. Each index calls the `Declare<Packet>` method stored beside that packet's wire codec in `src/Umpk.Protocol.Java/Families/<family>/<packet>/`.

Find the affected packet's family-local `.Wire.cs` file and add a `From` step at the first protocol whose wire form differs:

```csharp
bindings.Packet(WorldPackets.Clientbound.LightUpdate)
    .From(JavaProtocols.V1_14, WorldStateCodecs.LightUpdateV1_14)
    .From(JavaProtocols.V1_16, WorldStateCodecs.LightUpdateV1_16)
    .From(JavaProtocols.V1_17, WorldStateCodecs.LightUpdateV1_17)
    .From(JavaProtocols.V1_20, WorldStateCodecs.LightUpdateV1_20)
    .From(JavaProtocols.V26_2, WorldStateCodecs.LightUpdateV26_2);
```

Timeline resolution chooses the greatest `fromProtocol` not greater than the descriptor protocol. Leave a packet alone when its wire form did not change: the prior step resolves forward. A marker step is also valid when the packet is deliberately unsupported, but it must have a reason and an exact protocol range.

Do not reintroduce a generated `codecKey` decision. The dataset's identifier is used to locate the timeline, while the hand-authored family-local timeline is the source of truth for era selection.

## Prove the new boundary

Use more than one pin. Each pin has a different blind spot.

1. Add a byte-exact frame test from a real capture when possible. Decode asserted values and require byte-identical `encode(decode(frame))` output.
2. Add or refine an authored witness in `WitnessCatalog` when neighbouring bands can plausibly read the same zero probe. Its payload must decode in its own band and carry `reject:` verdicts for neighbouring bands, unless the pair is an explicitly justified entry in `IntentionalTwins.cs`.
3. Ensure the codec declares a non-opaque `WireShape` when its family is covered. Shape tokens are derived from the codec object, era shape, or era table rather than its bind-site spelling, so they catch a different class of error from an identity rename.
4. Add the new protocol to every literal era table. Check first with:

   ```bash
   python3 engineering/refactor/add-protocol-rows.py --check <protocol>
   ```

5. Regenerate each mechanical pin, then review every line it moves. The corpus now has captures for all 49 protocols. Witness renderings are per protocol, but authored witnesses cover only 20 packet families.

   ```bash
   UMPK_UPDATE_CODEC_IDENTITY_PINS=1 dotnet test tests/Umpk.Protocol.Java.Conformance
   UMPK_UPDATE_REGISTRATION_PINS=1 dotnet test tests/Umpk.Protocol.Java.Conformance
   UMPK_UPDATE_TIMELINE_BAND_PINS=1 dotnet test tests/Umpk.Protocol.Java.Conformance
   UMPK_EMIT_BAND_COMMENTS=1 dotnet test tests/Umpk.Protocol.Java.Conformance
   UMPK_UPDATE_WITNESS_PINS=1 dotnet test tests/Umpk.Protocol.Java.Conformance
   ```

The identity pin records bind-site identity, framing behavior, derived `shape:` tokens, and any `via:` alias. `fixtures/timelines/bands.txt` is the transposed, per-packet view of contiguous protocol bands. Witness fixtures are a rendering of authored payloads, not generated payloads. `IntentionalMarkers.cs` and `IntentionalTwins.cs` are exact hand-authored allowlists and have no update switch. Band, witness, and shape pins are separate checks with different blind spots, not a full conformance proof.

6. Run the conformance suite again with none of those environment variables set, update the affected `engineering/testcounts/expected_counts.json` total deliberately, and state the old and new totals in the commit message.

## Finish

Run the full development gate, including the count detector and formatting check. Confirm that the dataset verifier reports the new protocol count, the generated-output comparisons report no differing files, and every pin diff has an explanation.

The important failure mode is a partial update: packet ids may remain stable while their layouts move. The dataset, a successful build, and regenerated pins can all agree with the same mistaken timeline. Release research, real frames, witnesses, band pins, and derived wire shapes are complementary evidence, not a claim of complete conformance.
