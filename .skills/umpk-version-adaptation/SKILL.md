---
name: umpk-version-adaptation
description: Adapt UMPK Java protocol versions using vanilla artifacts, canonical datasets, generated tables, wire witnesses, and live boundary tests. Use for new version support or cross-version protocol mismatches.
---

# UMPK version adaptation

Read `docs/contributing/adding-a-version.md` before editing. UMPK owns protocol codecs, registries, world data, movement, and physics.

## Artifact evidence

Read `tools/minecraft-official/README.md` from the repository root. Use `/home/anon/Projects/MinecraftOfficial` on this workstation. Set `MINECRAFT_OFFICIAL_ROOT` and `UMPK_ORACLE_ROOT` explicitly when selecting another root.

Run `bash tools/decompile.sh --version <version>` for server source. Add `--side client` for client behavior. Check the downloaded artifact hashes and each tree's `.complete.json`. An obfuscated legacy tree cannot satisfy a check that requires readable MCP class names. Decompiler warnings require inspection of the affected methods.

Generate vanilla data reports with the matching jar and compatible Java when that release supports them. Put reports in a fresh external directory. Read UMPK's extraction guide for the correct invocation in each era. Runtime reports determine registry and block-state identifiers. Source field order alone cannot establish those identifiers. A stable packet identifier does not prove a stable layout, registry, feature axis, or lifecycle obligation.

## Implement through the existing dataset pipeline

1. Determine whether the release aliases an existing protocol. Preserve codec timelines and pins for an alias.
2. Compare matching vanilla jars, mappings, reports, and source for each changed boundary. Record hashes and source citations.
3. Extract canonical datasets with `tools/extraction/`. Review feature values against vanilla evidence. Obtain authorization for dataset changes when it is absent.
4. Run DataGen verification and dataset comparisons. Use the layout oracle as a source comparison aid, not a conformance gate.
5. Regenerate data, language, and codec tables through DataGen. Never edit `.g.cs` files or generated pins directly.
6. Add a timeline step only when the packet's wire form changes. Keep era rules in dataset features or existing profiles.
7. Add the required literal rows, real-frame round trips, adjacent-band rejection witnesses, and conformance pins through their documented procedures.
8. Run focused tests, adjacent protocols, a live vanilla leg, and the complete repository gates.

Do not restore MCC 1.x palette classes, `Protocol18.cs`, or its generated physics data. Those responsibilities now belong to UMPK's canonical extraction and generation pipeline. Vanilla artifacts and real captures remain the acceptance oracle.

For live proof, use `.skills/umpk-integration-testing/SKILL.md`. Report executed commands, observed assertions, inferred conclusions, harness faults, and unexecuted checks separately.
