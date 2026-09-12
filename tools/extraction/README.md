# UMPK extraction tooling

These tools add data for current and future Minecraft Java protocol versions. Previously imported version-band builders are not kept here. Their outputs and provenance remain in `data/java/`, which is the canonical dataset.

## Requirements

- Python 3.10 or later. The Python tools use only the standard library.
- Java 17 or later for Mojang data reports and the jar-driven extractors.
- Official server jars under `MinecraftOfficial/downloads/<version>/server.jar` and decompiled trees under `MinecraftOfficial/<version>-decompiled/`, or an absolute external root selected with `UMPK_ORACLE_ROOT`.

Never commit official jars, mappings, libraries, or decompiled sources.

## Add a report-capable version

Run the server reports, extract one protocol dataset, and then review every changed file.

```bash
python3 run_reports.py <version> --out /tmp/umpk-reports-<protocol>
python3 extract_modern.py <version> <protocol> \
    --reports /tmp/umpk-reports-<protocol>/reports \
    --out ../../data/java/<protocol> \
    --codec-key <candidate-era>
```

`extract_modern.py` writes packet, item, component, menu, command argument, registry, block, entity, metadata, and collision-shape data. The codec key is candidate provenance for review, not proof that the previous wire layout still applies.

Add the release to `data/java/versions.json`, author or review `features.json`, and follow `docs/contributing/adding-a-version.md` before regenerating C# sources.

## Source and mapping check

`decompile.py` identifies the mapping source for a release and reports whether a usable decompiled tree already exists.

```bash
python3 decompile.py <version>
```

The script does not download or decompile artifacts. Use `.skills/umpk-integration-testing/references/minecraft-official.md` to provision official sources.

## Language data

`extract_lang.py` selects the representative release for each protocol from `data/java/versions.json`, extracts its vanilla `en_us` table, and can compare all committed language tables without rewriting them.

```bash
python3 extract_lang.py --data ../../data/java --check
```

## Piston push data

`shape-extractor/PushDump.java` measures push reaction, destroy speed, block-entity presence, and air state from the target server jar. Fold its JSON output into the protocol dataset with:

```bash
python3 extract_push_reactions.py <protocol> \
    --dump /tmp/push-<protocol>.json \
    --data ../../data/java \
    --version <version>
```

The fold step rejects a block when its states disagree instead of collapsing state-specific behavior into a false block-wide value.

## Collision shapes

`shape-extractor/ShapeDump.java` drives a target server jar and records vanilla collision shapes. See `shape-extractor/README.md` for its requirements and supported layouts.

When jar-driven extraction is unavailable, `shape_extractor.py` maps the report's block-state ordering onto the repository-local bootstrap asset and marks the result with `curated-shape-bootstrap` provenance. This fallback does not invent data for unresolved blocks.

## Validation

```bash
dotnet run --project ../Umpk.DataGen -- verify --data ../../data/java
dotnet run --project ../Umpk.DataGen -- diff --data ../../data/java <previous-protocol> <protocol>
```

After extraction, regenerate all affected C# outputs through `Umpk.DataGen` and review the dataset and generated diffs together.
