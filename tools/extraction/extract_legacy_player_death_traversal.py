"""Import original legacy death ticks, world membership and placement queries."""

import argparse
import hashlib
import json
import math
from pathlib import Path
import re

from extract_entity_geometry import fixture_bytes
from extract_legacy_collision_grids import sha
from extract_legacy_player_clearance import read_run
from extract_legacy_player_traversal import read_packet_bodies


def packet_actions(mode: int, terrain: int) -> list[str]:
    actions = (['initial_full'] if terrain in (0, 2) else []) + ['initial_health'] + (['death_status'] if mode in (2, 3) else [])
    for tick in range(40):
        if terrain == 2 and tick == 5:
            actions.append('unload_5')
        if terrain == 2 and tick == 12 or terrain == 3 and tick == 5:
            actions.append(f'full_{tick}')
        if mode in (3, 4) and tick == 10:
            actions.append('revive_10')
        if mode == 4 and tick == 15:
            actions.append('zero_15')
    return actions


def validate_rows(rows: list[dict]) -> list[dict]:
    expected = {(flags, mode, terrain) for flags in [0, 2, 128] for mode in range(5) for terrain in range(4)}
    actual = [(row['flags'], row['mode'], row['terrain']) for row in rows]
    if len(actual) != len(expected) or set(actual) != expected:
        raise ValueError('The original death traversal scenario coverage differs')
    phases = ['initial'] + [f'{phase}_tick_{tick}' for tick in range(40) for phase in ['entity', 'world']]
    for row in rows:
        observations = row['observations']
        if [item['operation'] for item in observations] != phases:
            raise ValueError('The original death traversal phase order differs')
        if [item['action'] for item in row['packets']] != packet_actions(row['mode'], row['terrain']):
            raise ValueError('The original death traversal packet order differs')
        for packet in row['packets']:
            if type(packet['bytes']) is not int or packet['bytes'] <= 0 or re.fullmatch('[0-9a-f]{64}', packet['sha256']) is None:
                raise ValueError('The original death traversal packet receipt is invalid')
        for index, item in enumerate(observations):
            if any(type(item[name]) is not bool for name in ['added', 'in_entity_list', 'removed', 'alive', 'lookup_same', 'placement_clear']):
                raise ValueError('The original death traversal membership is unknown')
            if not item['lookup_same']:
                raise ValueError('The original death traversal entity lookup was lost')
            tick = -1 if index == 0 else (index - 1) // 2
            mode = row['mode']
            health = 20. if mode == 0 or mode in (3, 4) and tick >= 10 and (mode == 3 or tick < 15) else 0.
            if type(item['health']) not in (int, float) or item['health'] != health or item['alive'] != (health > 0 and not item['removed']):
                raise ValueError('The original death traversal health or alive predicate differs')
            if type(item['death_time']) is not int or item['death_time'] not in range(21) or type(item['age']) is not int or item['age'] < 0:
                raise ValueError('The original death traversal counter is invalid')
            if item['removed'] and (item['death_time'] != 20 or item['in_entity_list'] or not item['placement_clear']):
                raise ValueError('The original removed actor still participates in its world query')
            if index and observations[index - 1]['removed'] and not item['removed']:
                raise ValueError('The original removed actor unexpectedly returned')
            if index and index % 2 == 0 and any(item[name] != observations[index - 1][name] for name in ['age', 'death_time', 'removed']):
                raise ValueError('The original world phase unexpectedly aged its actor')
            if len(item['registered_chunk']) != 3 or any(type(value) is not int for value in item['registered_chunk']):
                raise ValueError('The original death traversal membership identity is invalid')
            for values, length in [(item['state']['position'], 3), (item['state']['rotation'], 2), (item['state']['target'], 5), (item['state']['box'], 6), (item['velocity'], 3), (item['size'], 2)]:
                if len(values) != length or any(type(value) not in (int, float) or not math.isfinite(value) for value in values):
                    raise ValueError('The original death traversal geometry is invalid')
    return rows


def fixture(root: Path, official: Path, data: Path) -> bytes:
    measurements = read_run(root, official, observation='death-traversal.json')
    catalog = {row['name']: row['protocol'] for row in json.loads((data / 'versions.json').read_text())['versions'] if row['protocol'] <= 404}
    if set(measurements) != set(catalog) or len(measurements) != 27:
        raise ValueError('The original death traversal release coverage differs')
    cases, releases, observations = [], [], []
    observation_keys = {}
    scope = None
    for version, (receipt, raw) in measurements.items():
        binding = json.loads((root / version / 'bindings.json').read_text())
        contract = binding['clearance_contract']
        if raw['query_owner'] != contract['world'] or raw['death_tick_owner'] != contract['living']:
            raise ValueError('The original death traversal method owners differ: ' + version)
        rows = validate_rows(raw['rows'])
        if receipt['protocol'] != catalog[version] or receipt['snapshots'] != 4860:
            raise ValueError('The original death traversal protocol or snapshot count differs: ' + version)
        if scope is not None and scope != raw['scope']:
            raise ValueError('The original death traversal scope differs: ' + version)
        scope = raw['scope']
        for mode in [0, 1, 2]:
            end = next(row for row in rows if row['flags'] == 0 and row['mode'] == mode and row['terrain'] == 0)['observations'][-1]
            if end['removed'] != (mode != 0) or end['death_time'] != (0 if mode == 0 else 20) or end['placement_clear'] != (mode != 0):
                raise ValueError('The original death traversal positive controls did not execute: ' + version)
        packed = []
        for row in rows:
            key = hashlib.sha256(json.dumps(row['observations'], separators=(',', ':')).encode()).hexdigest()
            if key not in observation_keys:
                observation_keys[key] = len(observations)
                observations.append(row['observations'])
            elif observations[observation_keys[key]] != row['observations']:
                raise ValueError('A native death observation hash collision occurred')
            packed.append({name: value for name, value in row.items() if name != 'observations'} | {'observation_case': observation_keys[key]})
        case = {'rows': packed, 'packet_bodies': read_packet_bodies(root / version / 'packets', rows)}
        if case not in cases:
            cases.append(case)
        releases.append({'version': version, 'protocol': receipt['protocol'], 'case': cases.index(case), 'client_sha256': receipt['client_sha256'], 'server_sha256': receipt['server_sha256'], 'measurement_sha256': sha(root / version / 'death-traversal.json'), 'sources_sha256': receipt['sources_sha256']})
    return fixture_bytes({'schema': 1, 'scope': scope, 'measurement': root.name, 'releases': releases, 'cases': cases, 'observations': observations}, compressed=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ['measurements', 'official', 'data', 'out']:
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    contents = fixture(args.measurements, args.official, args.data)
    if args.check:
        if not args.out.is_file() or args.out.read_bytes() != contents:
            raise ValueError('The original death traversal fixture differs')
    else:
        args.out.write_bytes(contents)
    print('Checked' if args.check else 'Wrote', '27-release original death traversal fixture')


if __name__ == '__main__':
    main()
