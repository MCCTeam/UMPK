"""Import original client-world chunk membership and player traversal witnesses."""

import argparse
import json
import math
from pathlib import Path
import re

from extract_entity_geometry import fixture_bytes
from extract_legacy_collision_grids import sha
from extract_legacy_player_clearance import read_run


def validate_reentry(rows: list[list[dict]]) -> list[list[dict]]:
    phases = ['before_unload_phase'] + [f'{phase}_tick_{tick}' for tick in range(4) for phase in ['entity', 'world']]
    if len(rows) != len(phases) or len(rows[0]) != 20:
        raise ValueError('The native reentry phase or actor count differs')
    ids = [item['id'] for item in rows[0]]
    if len(set(ids)) != 20 or any(type(value) is not int for value in ids):
        raise ValueError('The native reentry identities differ')
    for phase, actors in zip(phases, rows, strict=True):
        if [item['id'] for item in actors] != ids or any(item['operation'] != phase for item in actors):
            raise ValueError('The native reentry phase or actor coverage differs')
        for item in actors:
            if any(type(item[name]) is not bool for name in ['added', 'in_entity_list']):
                raise ValueError('The native reentry membership is unknown')
    return rows


def read_packet_bodies(root: Path, rows: list[dict]) -> dict[str, str]:
    bodies = {}
    for row in rows:
        for packet in row['packets']:
            path = root / (packet['sha256'] + '.bin')
            body = path.read_bytes()
            if len(body) != packet['bytes'] or sha(path) != packet['sha256']:
                raise ValueError('The original chunk packet body differs')
            bodies[packet['sha256']] = body.hex()
    return bodies


def validate_rows(rows: list[dict]) -> list[dict]:
    expected = {(point, scene) for point in range(4) for scene in range(8)}
    actual = [(row['position'], row['scene']) for row in rows]
    if len(actual) != len(expected) or set(actual) != expected:
        raise ValueError('The original traversal scenario coverage differs')
    phases = ['initial'] + [f'{phase}_tick_{tick}' for tick in range(8) for phase in ['entity', 'world']]
    for row in rows:
        if [item['operation'] for item in row['observations']] != phases:
            raise ValueError('The original traversal phase order differs')
        for item in row['observations']:
            if any(type(item[name]) is not bool for name in ['added', 'in_entity_list']):
                raise ValueError('The original traversal membership is unknown')
            if type(item['age']) is not int or item['age'] < 0 or len(item['registered_chunk']) != 3 or any(type(value) is not int for value in item['registered_chunk']):
                raise ValueError('The original traversal membership identity is invalid')
            for values, length in [(item['state']['position'], 3), (item['state']['box'], 6), (item['size'], 2)]:
                if len(values) != length or any(type(value) not in (int, float) or not math.isfinite(value) for value in values):
                    raise ValueError('The original traversal geometry is invalid')
        for packet in row['packets']:
            if not packet['action'] or type(packet['bytes']) is not int or packet['bytes'] <= 0 or re.fullmatch('[0-9a-f]{64}', packet['sha256']) is None:
                raise ValueError('The original traversal packet receipt is invalid')
    return rows


def fixture(root: Path, reentry_root: Path, official: Path, data: Path) -> bytes:
    measurements = read_run(root, official, observation='traversal.json')
    reentries = read_run(reentry_root, official, observation='reentry.json')
    catalog = {row['name']: row['protocol'] for row in json.loads((data / 'versions.json').read_text())['versions'] if row['protocol'] <= 404}
    if set(measurements) != set(catalog) or set(reentries) != set(catalog) or len(measurements) != 27:
        raise ValueError('The original traversal release coverage differs')
    cases, releases = [], []
    for version, (receipt, raw) in measurements.items():
        rows = validate_rows(raw['rows'])
        if receipt['protocol'] != catalog[version] or receipt['snapshots'] != 544:
            raise ValueError('The original traversal protocol or observation count differs: ' + version)
        reentry_receipt, reentry = reentries[version]
        if reentry_receipt['protocol'] != receipt['protocol'] or reentry_receipt['snapshots'] != 180:
            raise ValueError('The native reentry protocol or snapshot count differs: ' + version)
        case = {'positions': raw['positions'], 'rows': rows, 'packet_bodies': read_packet_bodies(root / version / 'packets', rows), 'reentry': validate_reentry(reentry['rows'])}
        if case not in cases:
            cases.append(case)
        releases.append({'version': version, 'protocol': receipt['protocol'], 'case': cases.index(case), 'client_sha256': receipt['client_sha256'], 'server_sha256': receipt['server_sha256'], 'measurement_sha256': sha(root / version / 'traversal.json'), 'reentry_sha256': sha(reentry_root / version / 'reentry.json'), 'sources_sha256': receipt['sources_sha256']})
    return fixture_bytes({'schema': 1, 'scope': raw['scope'], 'measurement': root.name, 'reentry_measurement': reentry_root.name, 'reentry_scope': reentry['scope'], 'releases': releases, 'cases': cases}, compressed=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ['measurements', 'reentry', 'official', 'data', 'out']:
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    contents = fixture(args.measurements, args.reentry, args.official, args.data)
    if args.check:
        if not args.out.is_file() or args.out.read_bytes() != contents:
            raise ValueError('The original traversal fixture differs')
    else:
        args.out.write_bytes(contents)
    print('Checked' if args.check else 'Wrote', '27-release native player traversal fixture')


if __name__ == '__main__':
    main()
