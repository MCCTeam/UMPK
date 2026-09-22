#!/usr/bin/env python3
"""Fold a PushDump run into the canonical ``data/java/<protocol>/block-push.json``.

Input is the raw per-state JSON that ``shape-extractor/PushDump.java`` writes while
driving that version's own server jar through its official ``server_mappings.txt``:

    { "<stateId>": ["<REACTION>", <destroySpeed>, <hasBlockEntity>, <isAir>], ... }

Those are the four values ``PistonBaseBlock.isPushable`` and
``PistonStructureResolver.resolve`` read, and they are the reason the client could not
model the blocks a piston pushes: no report and no other dataset carries any of them.

Output keeps only what a consumer needs, keyed by block NAME, because every measured
band answers all three per-block constants identically for every state of a block --
which this script re-checks and REFUSES to collapse when it stops being true.

Usage:
    python3 extract_push_reactions.py <protocol> --dump <push-NNN.json> \
        --data ../../data/java --version 1.21.11 --mapping-source mojang-proguard
"""

import argparse
import json
import os
import sys

REACTIONS = ("NORMAL", "DESTROY", "BLOCK", "IGNORE", "PUSH_ONLY")
DEFAULT_REACTION = "NORMAL"

# 26.3 renamed vanilla's PushReaction constants. The per-block assignment is unchanged,
# verified state-by-state against the 26.2 table (glazed terracotta PUSH_ONLY -> PUSH,
# obsidian BLOCK -> IMMOVEABLE, torch DESTROY -> POPPED, stone NORMAL -> PUSH_PULL, and the
# sticky-piston retract check `reaction != PUSH_PULL` is the old `reaction == NORMAL` pull
# rule), so the fold normalizes to the stable dataset spelling DataGen and Umpk.Game share.
REACTION_ALIASES = {
    "PUSH_PULL": "NORMAL",
    "POPPED": "DESTROY",
    "IMMOVEABLE": "BLOCK",
    "PUSH": "PUSH_ONLY",
    "IGNORE_ENTITY": "IGNORE",
}


def load_state_names(blocks_path):
    """state id -> block name, from the committed per-protocol block table."""
    with open(blocks_path, "r", encoding="utf-8") as handle:
        blocks = json.load(handle)
    if blocks.get("identity") != "flat":
        raise SystemExit(
            "%s is not a flattened dataset; the piston model is gated off the legacy band"
            % blocks_path
        )
    names = {}
    for entry in blocks["blocks"]:
        for state in range(entry["min_state"], entry["max_state"] + 1):
            names[state] = entry["name"]
    return names, len(blocks["blocks"])


def build(dump, names):
    """Collapse per-state rows to per-block constants, refusing any block that varies."""
    per_block = {}
    for raw_id, row in dump.items():
        state = int(raw_id)
        if state not in names:
            raise SystemExit(
                "dump carries state %d which the committed blocks.json does not define" % state
            )
        reaction, destroy_speed, has_block_entity, _is_air = row
        reaction = REACTION_ALIASES.get(reaction, reaction)
        if reaction not in REACTIONS:
            raise SystemExit("unknown PushReaction %r at state %d" % (row[0], state))
        value = (reaction, destroy_speed == -1.0, bool(has_block_entity))
        name = names[state]
        seen = per_block.setdefault(name, value)
        if seen != value:
            raise SystemExit(
                "block %s answers %r at one state and %r at another. Push reaction, "
                "unbreakability and block-entity-ness are per-BLOCK on every measured band; "
                "a version that breaks that needs a per-STATE table, not a collapse." % (name, seen, value)
            )

    missing = sorted(set(names.values()) - set(per_block))
    if missing:
        raise SystemExit("no dumped state for %d block(s), e.g. %s" % (len(missing), missing[:5]))

    reactions = {key: [] for key in REACTIONS if key != DEFAULT_REACTION}
    unbreakable = []
    block_entity = []
    for name in sorted(per_block):
        reaction, is_unbreakable, has_block_entity = per_block[name]
        if reaction != DEFAULT_REACTION:
            reactions[reaction].append(name)
        if is_unbreakable:
            unbreakable.append(name)
        if has_block_entity:
            block_entity.append(name)
    return reactions, unbreakable, block_entity


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("protocol", type=int)
    parser.add_argument("--dump", required=True)
    parser.add_argument("--data", required=True, help="the data/java root")
    parser.add_argument("--version", required=True, help="the Minecraft version the jar is")
    parser.add_argument("--mapping-source", default="mojang-proguard")
    args = parser.parse_args(argv)

    out_dir = os.path.join(args.data, str(args.protocol))
    blocks_path = os.path.join(out_dir, "blocks.json")
    if not os.path.isfile(blocks_path):
        raise SystemExit("no %s" % blocks_path)

    with open(args.dump, "r", encoding="utf-8") as handle:
        dump = json.load(handle)
    names, block_count = load_state_names(blocks_path)
    if len(dump) != len(names):
        raise SystemExit(
            "dump has %d states, blocks.json defines %d" % (len(dump), len(names))
        )

    reactions, unbreakable, block_entity = build(dump, names)
    # 26.1+ jars ship unobfuscated, so PushDump runs with mappings "none" and there is no
    # server_mappings.txt to cite; older bands cite theirs.
    sources = ["downloads/%s/server.jar" % args.version]
    if args.mapping_source != "vineflower-direct":
        sources.append("downloads/%s/server_mappings.txt" % args.version)
    document = {
        "_provenance": {
            "version": args.version,
            "protocol": args.protocol,
            "mapping_source": args.mapping_source,
            "sources": sources,
            "kind": "extracted",
            "tool": "tools/extraction/shape-extractor/PushDump.java",
            "note": (
                "BlockState.getPistonPushReaction(), getDestroySpeed()==-1.0F and "
                "hasBlockEntity()/Block.isEntityBlock(), read off the version's own server jar by "
                "reflection. All three are constant across a block's states on this band, which "
                "extract_push_reactions.py re-checks."
            ),
        },
        "identity": "flat",
        "default_reaction": DEFAULT_REACTION,
        "block_count": block_count,
        "state_count": len(names),
        "reactions": reactions,
        "unbreakable": unbreakable,
        "block_entity": block_entity,
    }

    out_path = os.path.join(out_dir, "block-push.json")
    with open(out_path, "w", encoding="utf-8") as handle:
        json.dump(document, handle, indent=2)
        handle.write("\n")
    print(
        "%s: %d blocks, %s, unbreakable=%d block_entity=%d"
        % (
            out_path,
            block_count,
            " ".join("%s=%d" % (k, len(v)) for k, v in reactions.items()),
            len(unbreakable),
            len(block_entity),
        )
    )
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
