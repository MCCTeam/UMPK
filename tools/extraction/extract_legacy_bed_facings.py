"""Import original bed-facing facts without collapsing legacy raw metadata aliases."""

import argparse
import json
from pathlib import Path

from extract_legacy_collision_grids import sha
from extract_legacy_player_clearance import read_run


def reduce_states(protocol: int, raw: dict, blocks: dict) -> list[list[int]]:
    if protocol < 393:
        beds = [row for row in blocks['blocks'] if row.get('material') == 'bed']
        if len(beds) != 1:
            raise ValueError('The canonical legacy bed identity differs')
        first = beds[0]['block_id'] << 4
        expected = {'minecraft:bed': set(range(first, first + 16))}
    else:
        expected = {row['name']: set(range(row['min_state'], row['max_state'] + 1)) for row in blocks['blocks'] if row['name'].endswith('_bed')}
        if len(expected) != 16:
            raise ValueError('The canonical flattened bed registry differs')
    seen = {name: set() for name in expected}
    counts: dict[str, dict[int, int]] = {name: {} for name in expected}
    facts: dict[int, int] = {}
    for row in raw['rows']:
        if len(row) != (5 if protocol < 393 else 4):
            raise ValueError('The native bed row has an invalid layout')
        name, state, dx, dz = row[:4]
        direction = {(0, -1): 2, (0, 1): 3, (-1, 0): 4, (1, 0): 5}.get((dx, dz))
        if direction is None or type(state) is not int or name not in expected or state not in expected[name] or state in seen[name] or state in facts:
            raise ValueError('The native bed state is unknown, repeated or nonhorizontal')
        seen[name].add(state)
        facts[state] = direction
        counts[name][direction] = counts[name].get(direction, 0) + 1
        if protocol < 393 and (type(row[4]) is not int or row[4] & 4095 != state >> 4):
            raise ValueError('The native decoded legacy bed changed block identity')
    if seen != expected or raw['bed_counts'] != {name: 16 for name in expected} or any(value != {2: 4, 3: 4, 4: 4, 5: 4} for value in counts.values()):
        raise ValueError('The native bed matrix is incomplete')
    return [[state, direction] for state, direction in sorted(facts.items())]


def tables(root: Path, official: Path, data: Path) -> dict[int, dict]:
    measurements = read_run(root, official, observation='bed-facings.json')
    catalog = {row['name']: row['protocol'] for row in json.loads((data / 'versions.json').read_text())['versions'] if 47 <= row['protocol'] <= 404}
    if set(measurements) != set(catalog) or len(measurements) != 27:
        raise ValueError('The original bed-facing cohort differs')
    by_protocol: dict[int, dict] = {}
    tools = json.loads((root / 'tools.json').read_text())
    for version, (receipt, raw) in measurements.items():
        protocol = receipt['protocol']
        contract = json.loads((root / version / 'bindings.json').read_text())['clearance_contract']
        spec = contract['bed_facing_measurement']
        owners = {'state_id': spec['block']}
        if protocol < 393:
            owners.update(block_id=spec['block'], decode_metadata=spec['bed_type'])
        if protocol != catalog[version] or any(raw['method_owners'].get(name) != owner for name, owner in owners.items()) or any(name + '.java' not in contract['sources_sha256'] for name in raw['method_owners'].values()):
            raise ValueError('The original bed protocol or method owners differ: ' + version)
        blocks = json.loads((data / str(protocol) / 'blocks.json').read_text())
        states = reduce_states(protocol, raw, blocks)
        entry = by_protocol.setdefault(protocol, {'_provenance': {'kind': 'extracted', 'scope': raw['scope'], 'measurement': root.name, 'probe_sha256': tools['probe_source_sha256'], 'releases': []}, 'schema': 1, 'identity': blocks['identity'], 'state_count': len(states), 'states': states})
        if entry['states'] != states:
            raise ValueError('Original bed-facing aliases disagree: ' + version)
        entry['_provenance']['releases'].append({'version': version, 'client_sha256': receipt['client_sha256'], 'server_sha256': receipt['server_sha256'], 'measurement_sha256': sha(root / version / 'bed-facings.json'), 'sources_sha256': receipt['sources_sha256'], 'method_owners': raw['method_owners']})
    return by_protocol


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ['measurements', 'official', 'data']:
        parser.add_argument('--' + name, required=True, type=Path)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    outputs = tables(args.measurements, args.official, args.data)
    for protocol, table in outputs.items():
        path = args.data / str(protocol) / 'bed-facings.json'
        contents = (json.dumps(table, indent=2) + '\n').encode()
        if args.check:
            if not path.is_file() or path.read_bytes() != contents:
                raise ValueError('The generated bed-facing data differs: ' + str(protocol))
        else:
            path.write_bytes(contents)
    print(('Checked' if args.check else 'Wrote'), len(outputs), 'native bed-facing tables')


if __name__ == '__main__':
    main()
