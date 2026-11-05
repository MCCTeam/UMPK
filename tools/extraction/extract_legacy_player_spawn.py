"""Import original legacy spawn, initial-metadata and replacement listeners."""

import argparse
import json
import math
from pathlib import Path
import re

from extract_entity_geometry import fixture_bytes
from extract_legacy_collision_grids import sha
from extract_legacy_player_clearance import read_run
from extract_legacy_player_traversal import read_packet_bodies


def validate_rows(rows: list[dict], actor: str) -> list[dict]:
    expected = {(point, flags, terrain) for point in range(4) for flags in [0, 2, 128, 130] for terrain in [False, True]}
    actual = [(row['position'], row['flags'], row['terrain']) for row in rows]
    if len(actual) != len(expected) or set(actual) != expected or any(type(row['terrain']) is not bool for row in rows):
        raise ValueError('The original spawn scenario coverage differs')
    phases = ['after_spawn', 'after_metadata', 'after_explicit_position', 'after_replacement']
    for row in rows:
        if row['replaced_instance'] is not True:
            raise ValueError('The native spawn retained its prior replacement identity')
        if [item['operation'] for item in row['observations']] != phases:
            raise ValueError('The original spawn phase order differs')
        actions = (['terrain'] if row['terrain'] else []) + ['spawn', 'metadata', 'replacement']
        if [packet['action'] for packet in row['packets']] != actions:
            raise ValueError('The original spawn packet order differs')
        for packet in row['packets']:
            if type(packet['bytes']) is not int or packet['bytes'] <= 0 or re.fullmatch('[0-9a-f]{64}', packet['sha256']) is None:
                raise ValueError('The original spawn packet receipt is invalid')
            if packet['action'] in ['spawn', 'replacement'] and packet.get('native_actor_reconstruction_equal') is not True:
                raise ValueError('The original spawn actor reconstruction proof is missing')
        for item in row['observations']:
            if item['actor_class'] != actor:
                raise ValueError('The original spawn actor was replaced by an adapter')
            flags = row['flags'] ^ 2 if item['operation'] in ['after_metadata', 'after_explicit_position'] else row['flags']
            if type(item['flags']) is not int or item['flags'] != flags:
                raise ValueError('The original spawn metadata flags differ from the dispatch')
            if item['age'] != 0 or any(type(item[name]) is not bool for name in ['added', 'in_entity_list', 'sleeping']):
                raise ValueError('The original spawn actor lifecycle state differs')
            if type(item['state']['active']) is not bool or item['state']['active']:
                raise ValueError('The original spawn retained pending motion')
            if len(item['registered_chunk']) != 3 or any(type(value) is not int for value in item['registered_chunk']):
                raise ValueError('The original spawn membership identity is invalid')
            vectors = [(item['size'], 2), (item['state']['position'], 3), (item['state']['rotation'], 2), (item['state']['target'], 5), (item['state']['box'], 6)]
            if any(len(values) != length or any(type(value) not in (int, float) or not math.isfinite(value) for value in values) for values, length in vectors):
                raise ValueError('The original spawn geometry is invalid')
    return rows


def fixture(root: Path, official: Path, data: Path) -> bytes:
    measurements = read_run(root, official, observation='spawn.json')
    catalog = {row['name']: row['protocol'] for row in json.loads((data / 'versions.json').read_text())['versions'] if row['protocol'] <= 404}
    if set(measurements) != set(catalog) or len(measurements) != 27:
        raise ValueError('The original spawn release coverage differs')
    cases, releases = [], []
    scope = None
    for version, (receipt, raw) in measurements.items():
        binding = json.loads((root / version / 'bindings.json').read_text())
        rows = validate_rows(raw['rows'], binding['actor'])
        if receipt['protocol'] != catalog[version] or receipt['snapshots'] != 128:
            raise ValueError('The original spawn protocol or observation count differs: ' + version)
        if scope is not None and scope != raw['scope']:
            raise ValueError('The original spawn scope differs: ' + version)
        scope = raw['scope']
        case = {'protocol': receipt['protocol'], 'positions': raw['positions'], 'rows': rows, 'packet_bodies': read_packet_bodies(root / version / 'packets', rows)}
        if case not in cases:
            cases.append(case)
        releases.append({'version': version, 'protocol': receipt['protocol'], 'case': cases.index(case), 'client_sha256': receipt['client_sha256'], 'server_sha256': receipt['server_sha256'], 'measurement_sha256': sha(root / version / 'spawn.json'), 'sources_sha256': receipt['sources_sha256']})
    return fixture_bytes({'schema': 1, 'scope': scope, 'measurement': root.name, 'releases': releases, 'cases': cases}, compressed=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ['measurements', 'official', 'data', 'out']:
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    contents = fixture(args.measurements, args.official, args.data)
    if args.check:
        if not args.out.is_file() or args.out.read_bytes() != contents:
            raise ValueError('The original spawn fixture differs')
    else:
        args.out.write_bytes(contents)
    print('Checked' if args.check else 'Wrote', '27-release original spawn fixture')


if __name__ == '__main__':
    main()
