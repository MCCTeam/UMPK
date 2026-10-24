"""Import original missing-column block aliases and bed listener witnesses."""

import argparse
import json
from pathlib import Path
import re

from extract_entity_geometry import fixture_bytes
from extract_legacy_collision_grids import sha
from extract_legacy_player_block_context import validate_snapshot, validate_world_packets
from extract_legacy_player_clearance import read_run
from extract_legacy_player_traversal import read_packet_bodies

POINTS = ((1, 64, 1), (-17, 64, -17), (15, 64, 15), (16, 64, 16))


def validate_rows(rows: list[dict]) -> list[dict]:
    expected = {(point, scene, cell) for point in range(4) for scene in range(8) for cell in range(2)}
    actual = [(row['point'], row['scene'], row['sleep_cell']) for row in rows]
    if len(actual) != len(expected) or set(actual) != expected:
        raise ValueError('The original EmptyChunk scenario coverage differs')
    for row in rows:
        a = POINTS[row['point']]
        cells = [list(a), [a[0] + 16, a[1], a[2] + 16], [a[0] + 17, a[1], a[2] + 16]]
        if row['cells'] != cells:
            raise ValueError('The original EmptyChunk alias coordinate differs')
        scene = row['scene']
        actions = (['initial_full_a'] if scene == 2 else ['initial_full_b'] if scene == 5 else []) + ['bed_a']
        actions += ['bed_b'] if scene == 1 else ['full_b'] if scene == 3 else ['full_a', 'unload_a'] if scene == 4 else ['air_c'] if scene == 6 else ['air_b']
        if scene == 7:
            actions.append('restore_bed_a')
        validate_world_packets(row['world_packets'], actions)
        failed = row['sleep']['result'] == 'native_property_exception'
        if failed:
            if row['sleep'].get('exception') != 'java.lang.IllegalArgumentException' or 'property' not in row['sleep'].get('message', '') or 'wake' in row:
                raise ValueError('The original EmptyChunk aborted sleep is unexpected or followed by wake')
        elif row['sleep'] != {'result': 'completed'} or row.get('wake') != {'result': 'completed'}:
            raise ValueError('The original EmptyChunk callback result is unknown')
        if [packet['kind'] for packet in row['packets']] != ['bed'] + ([] if failed else ['wake']):
            raise ValueError('The original EmptyChunk callback packet order differs')
        if any(not isinstance(packet['body'], str) or re.fullmatch('(?:[0-9a-f]{2})+', packet['body']) is None for packet in row['packets']):
            raise ValueError('The original EmptyChunk callback body is invalid')
        phases = ['initial'] + ['after_' + action for action in actions if not action.startswith('initial_')] + ['after_sleep'] + ([] if failed else ['after_wake'])
        if [item['operation'] for item in row['observations']] != phases:
            raise ValueError('The original EmptyChunk phase order differs')
        for item in row['observations']:
            validate_snapshot(item)
            if len(item['reads']) != 3:
                raise ValueError('The original EmptyChunk cell observations are incomplete')
            for read in item['reads']:
                if type(read['bed_native_state']) is not int or read['bed_native_state'] < 0:
                    raise ValueError('The original EmptyChunk cell state is unknown')
                if any(type(read[key]) is not bool for key in ['bed_available', 'empty_chunk']):
                    raise ValueError('The original EmptyChunk cell knowledge is unknown')
                if not isinstance(read['chunk_class'], str) or re.fullmatch(r'[\w.$]+', read['chunk_class']) is None:
                    raise ValueError('The original EmptyChunk cell class is missing')
            if any(item[key] != value for key, value in item['reads'][0].items()):
                raise ValueError('The original EmptyChunk primary cell differs from its read')
    return rows


def fixture(root: Path, official: Path, data: Path) -> bytes:
    measurements = read_run(root, official, observation='empty-alias.json')
    catalog = {row['name']: row['protocol'] for row in json.loads((data / 'versions.json').read_text())['versions'] if row['protocol'] <= 404}
    if set(measurements) != set(catalog) or len(measurements) != 27:
        raise ValueError('The original EmptyChunk release coverage differs')
    cases, releases = [], []
    scope = None
    for version, (receipt, raw) in measurements.items():
        binding = json.loads((root / version / 'bindings.json').read_text())
        baseline = json.loads((root / version / 'context-baseline.json').read_text())
        if baseline['block_update_owner'] != binding['listener_contract']['listener']:
            raise ValueError('The original EmptyChunk block listener differs: ' + version)
        rows = validate_rows(raw['rows'])
        if receipt['protocol'] != catalog[version] or receipt['snapshots'] != sum(len(row['observations']) for row in rows):
            raise ValueError('The original EmptyChunk protocol or snapshot count differs: ' + version)
        if scope is not None and scope != raw['scope']:
            raise ValueError('The original EmptyChunk scope differs: ' + version)
        scope = raw['scope']
        classes = binding['clearance_contract']['world_tick']
        for row in rows:
            for item in row['observations']:
                for read in [item, *item['reads']]:
                    expected = classes['empty_chunk_type'] if read['empty_chunk'] else classes['chunk_type']
                    if read['chunk_class'] != expected:
                        raise ValueError('The original EmptyChunk class differs: ' + version)
        packets = read_packet_bodies(root / version / 'packets', [{'packets': row['world_packets']} for row in rows])
        case = {'protocol': receipt['protocol'], 'rows': rows, 'packet_bodies': packets}
        if case not in cases:
            cases.append(case)
        releases.append({'version': version, 'protocol': receipt['protocol'], 'case': cases.index(case), 'client_sha256': receipt['client_sha256'], 'server_sha256': receipt['server_sha256'], 'measurement_sha256': sha(root / version / 'empty-alias.json'), 'sources_sha256': receipt['sources_sha256'], 'block_update_owner': baseline['block_update_owner']})
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
            raise ValueError('The original EmptyChunk fixture differs')
    else:
        args.out.write_bytes(contents)
    print('Checked' if args.check else 'Wrote', '27-release original EmptyChunk fixture')


if __name__ == '__main__':
    main()
