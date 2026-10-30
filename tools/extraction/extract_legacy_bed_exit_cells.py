"""Import original bed-exit support/material facts and explicit native metadata rejections."""

import argparse
import json
from pathlib import Path

from extract_legacy_collision_grids import sha
from extract_legacy_player_clearance import read_run


def reduce_states(protocol: int, raw: dict, blocks: dict) -> dict:
    expected = {state for block in blocks['blocks'] for state in (range(block['block_id'] << 4, (block['block_id'] << 4) + 16) if protocol < 393 else range(block['min_state'], block['max_state'] + 1))}
    known: dict[int, int] = {}
    rejected: dict[int, tuple[str, str]] = {}
    for row in raw['rows']:
        if len(row) != (4 if protocol < 393 else 3):
            raise ValueError('The native bed-exit cell row differs')
        state, flags, traces = row[:3]
        if type(state) is not int or state not in expected or state in known or type(flags) is not int or flags not in [0, 1, 6, 7]:
            raise ValueError('The native bed-exit state is unknown, repeated or inconsistent')
        reads = [[[0, 63, 0]] + ([[0, 64, 0], [0, 65, 0]] if flags & 1 else []), [[0, 63, 0], [0, 64, 0]] + ([[0, 65, 0]] if flags & 2 else []), [[0, 63, 0], [0, 64, 0], [0, 65, 0]]]
        if traces != reads:
            raise ValueError('The original bed-exit predicate reads unexpected world cells')
        if protocol < 393 and (type(row[3]) is not int or row[3] & 4095 != state >> 4):
            raise ValueError('The decoded legacy cell changed block identity')
        known[state] = (flags & 1) | (2 if flags & 2 else 0)
    for row in raw['rejected_metadata']:
        if len(row) != 3:
            raise ValueError('The original metadata rejection has an invalid layout')
        state, kind, message = row
        if protocol >= 393 or type(state) is not int or state not in expected or state in known or state in rejected or kind != 'java.lang.IllegalArgumentException' or not isinstance(message, str) or not message:
            raise ValueError('The original metadata rejection is inconsistent')
        rejected[state] = (kind, message)
    if expected != known.keys() | rejected.keys() or not known:
        raise ValueError('The original cell facts and rejections do not cover the registry')
    ranges: list[list[int]] = []
    for state, flags in sorted(known.items()):
        if ranges and ranges[-1][1] + 1 == state and ranges[-1][2] == flags:
            ranges[-1][1] = state
        else:
            ranges.append([state, state, flags])
    return {'state_count': len(known), 'ranges': ranges, 'rejected_states': sorted(rejected)}


def tables(root: Path, official: Path, data: Path) -> dict[int, dict]:
    observations = read_run(root, official, observation='bed-exit-cells.json')
    catalog = {row['name']: row['protocol'] for row in json.loads((data / 'versions.json').read_text())['versions'] if 47 <= row['protocol'] <= 404}
    if set(observations) != set(catalog) or len(observations) != 27:
        raise ValueError('The original bed-exit cell cohort differs')
    tools = json.loads((root / 'tools.json').read_text())
    outputs: dict[int, dict] = {}
    for version, (receipt, raw) in observations.items():
        protocol = receipt['protocol']
        binding = json.loads((root / version / 'bindings.json').read_text())
        if protocol != catalog[version] or raw['predicate_owner'] != binding['clearance_contract']['bed_exit_cells']['predicate_owner']:
            raise ValueError('The original bed-exit predicate owner differs: ' + version)
        blocks = json.loads((data / str(protocol) / 'blocks.json').read_text())
        facts = reduce_states(protocol, raw, blocks)
        entry = outputs.setdefault(protocol, {'_provenance': {'kind': 'extracted', 'measurement': root.name, 'scope': raw['scope'], 'predicate': 'support && feet_material_clear && head_material_clear; rejected metadata remains unknown', 'probe_sha256': tools['probe_source_sha256'], 'releases': []}, 'schema': 1, 'identity': blocks['identity'], **facts})
        if any(entry[key] != value for key, value in facts.items()):
            raise ValueError('Original bed-exit cell aliases disagree: ' + version)
        entry['_provenance']['releases'].append({'version': version, 'client_sha256': receipt['client_sha256'], 'server_sha256': receipt['server_sha256'], 'measurement_sha256': sha(root / version / 'bed-exit-cells.json'), 'sources_sha256': receipt['sources_sha256'], 'predicate_owner': raw['predicate_owner'], 'rejected_metadata': raw['rejected_metadata']})
    return outputs


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ['measurements', 'official', 'data']:
        parser.add_argument('--' + name, required=True, type=Path)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    outputs = tables(args.measurements, args.official, args.data)
    for protocol, table in outputs.items():
        path = args.data / str(protocol) / 'bed-exit-cells.json'
        contents = (json.dumps(table, indent=2) + '\n').encode()
        if args.check:
            if not path.is_file() or path.read_bytes() != contents:
                raise ValueError('The generated bed-exit cell data differs: ' + str(protocol))
        else:
            path.write_bytes(contents)
    print(('Checked' if args.check else 'Wrote'), len(outputs), 'native bed-exit cell tables')


if __name__ == '__main__':
    main()
