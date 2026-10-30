"""Import original legacy bed callbacks over missing terrain and world bounds."""

import argparse
import json
import math
from pathlib import Path
import re

from extract_entity_geometry import fixture_bytes
from extract_legacy_collision_grids import sha
from extract_legacy_player_clearance import read_run
from extract_legacy_player_traversal import read_packet_bodies


BED_POSITIONS = ((1, 64, 1), (-17, 64, -17), (1, -1, 1), (1, 256, 1), (29999999, 64, 1), (30000000, 64, 1), (-30000000, 64, 1), (-30000001, 64, 1), (1, -64, 1), (1, 320, 1), (1, 255, 1))


def validate_rows(rows: list[dict]) -> list[dict]:
    expected = {(point, scene) for point in range(len(BED_POSITIONS)) for scene in range(4)}
    actual = [(row['position'], row['scene']) for row in rows]
    if len(actual) != len(expected) or set(actual) != expected:
        raise ValueError('The native bed-availability scenario coverage differs')
    for row in rows:
        if row['bed'] != list(BED_POSITIONS[row['position']]):
            raise ValueError('The native bed coordinate differs from its scenario')
        failed = row['sleep']['result'] == 'native_property_exception'
        if failed:
            error = row['sleep']
            if error.get('exception') != 'java.lang.IllegalArgumentException' or 'property' not in error.get('message', '') or 'wake' in row:
                raise ValueError('The native bed exception is unexpected or followed by wake')
        elif row['sleep'] != {'result': 'completed'} or row.get('wake') != {'result': 'completed'}:
            raise ValueError('The native bed callback result is unknown')
        phases = ['initial', 'after_sleep_packet'] + ([] if failed else ['after_wake_packet'])
        if [item['operation'] for item in row['observations']] != phases:
            raise ValueError('The native bed callback phase order differs')
        kinds = ['bed'] + ([] if failed else ['wake'])
        if [packet['kind'] for packet in row['packets']] != kinds:
            raise ValueError('The original bed packet order differs')
        for packet in row['packets']:
            if not isinstance(packet['body'], str) or re.fullmatch('(?:[0-9a-f]{2})+', packet['body']) is None:
                raise ValueError('The original bed packet body is invalid')
        if ('terrain_packet' in row) != (row['scene'] != 0):
            raise ValueError('The native bed terrain setup receipt is missing or unexpected')
        if 'terrain_packet' in row:
            packet = row['terrain_packet']
            if packet['action'] != 'loaded_full' or type(packet['bytes']) is not int or packet['bytes'] <= 0 or re.fullmatch('[0-9a-f]{64}', packet['sha256']) is None:
                raise ValueError('The native bed terrain packet receipt is invalid')
        if row['scene'] >= 2:
            if type(row.get('bed_write_accepted')) is not bool:
                raise ValueError('The native bed block write result is unknown')
        elif 'bed_write_accepted' in row:
            raise ValueError('The native bed scenario has an unexpected block write')
        for item in row['observations']:
            if any(type(item[name]) is not bool for name in ['added', 'in_entity_list', 'sleeping']):
                raise ValueError('The native bed actor state is unknown')
            if any(type(item[name]) is not int or item[name] < 0 for name in ['age', 'bed_native_state']):
                raise ValueError('The native bed actor identity is invalid')
            if len(item['registered_chunk']) != 3 or any(type(value) is not int for value in item['registered_chunk']):
                raise ValueError('The native bed actor membership is invalid')
            vectors = [(item['size'], 2), (item['velocity'], 3), (item['state']['position'], 3), (item['state']['rotation'], 2), (item['state']['target'], 5), (item['state']['box'], 6), ([item['health']], 1)]
            if any(len(values) != length or any(type(value) not in (int, float) or not math.isfinite(value) for value in values) for values, length in vectors):
                raise ValueError('The native bed actor geometry is invalid')
    return rows


def read_terrain_bodies(root: Path, rows: list[dict]) -> dict[str, str]:
    return read_packet_bodies(root, [{'packets': [row['terrain_packet']]} for row in rows if 'terrain_packet' in row])


def fixture(root: Path, official: Path, data: Path) -> bytes:
    measurements = read_run(root, official, observation='availability.json')
    catalog = {row['name']: row['protocol'] for row in json.loads((data / 'versions.json').read_text())['versions'] if row['protocol'] <= 404}
    if set(measurements) != set(catalog) or len(measurements) != 27:
        raise ValueError('The native bed-availability release coverage differs')
    cases, releases = [], []
    scope = None
    for version, (receipt, raw) in measurements.items():
        rows = validate_rows(raw['rows'])
        if receipt['protocol'] != catalog[version] or receipt['snapshots'] != sum(len(row['observations']) for row in rows):
            raise ValueError('The native bed protocol or snapshot count differs: ' + version)
        if scope is not None and scope != raw['scope']:
            raise ValueError('The native bed measurement scope differs: ' + version)
        scope = raw['scope']
        case = {'protocol': receipt['protocol'], 'rows': rows, 'terrain_bodies': read_terrain_bodies(root / version / 'packets', rows)}
        if case not in cases:
            cases.append(case)
        releases.append({'version': version, 'protocol': receipt['protocol'], 'case': cases.index(case), 'client_sha256': receipt['client_sha256'], 'server_sha256': receipt['server_sha256'], 'measurement_sha256': sha(root / version / 'availability.json'), 'sources_sha256': receipt['sources_sha256']})
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
            raise ValueError('The native bed-availability fixture differs')
    else:
        args.out.write_bytes(contents)
    print('Checked' if args.check else 'Wrote', '27-release native bed-availability fixture')


if __name__ == '__main__':
    main()
