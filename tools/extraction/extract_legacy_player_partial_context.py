"""Import original partial-chunk bed callbacks, object identities and full actor ticks."""

import argparse
import hashlib
import json
from pathlib import Path
import re

from extract_entity_geometry import fixture_bytes
from extract_legacy_collision_grids import sha
from extract_legacy_player_block_context import validate_snapshot, validate_world_packets
from extract_legacy_player_clearance import read_run
from extract_legacy_player_traversal import read_packet_bodies

POINTS = ((1, 64, 1), (-17, 64, -17), (15, 64, 15), (16, 64, 16))


def validate_rows(rows: list[dict], *, traversal: bool, full: bool = False) -> list[dict]:
    expected = {(point, terrain, target, payload) for point in range(4) for terrain in range(4) for target in range(2) for payload in range(2)}
    actual = [(row['point'], row['terrain'], row['partial_cell'], row['payload']) for row in rows]
    if len(actual) != len(expected) or set(actual) != expected:
        raise ValueError('The original partial-chunk scenario coverage differs')
    for row in rows:
        a = POINTS[row['point']]
        if row['cells'] != [list(a), [a[0] + 16, a[1], a[2] + 16], [a[0] + 17, a[1], a[2] + 16]]:
            raise ValueError('The original partial-chunk alias coordinate differs')
        terrain = row['terrain']
        payload = 'full_payload' if full else 'partial_payload'
        actions = ['seed_bed_a'] + (['full_b' if terrain == 2 else 'full_a'] if terrain else []) + (['unload_a'] if terrain == 3 else []) + [payload]
        validate_world_packets(row['world_packets'], actions)
        if traversal:
            phases = ['initial'] + [f'{phase}_tick_{tick}' for tick in range(8) for phase in ['entity', 'world']]
            if [item['operation'] for item in row['setup']] != ['after_spawn', 'after_' + payload]:
                raise ValueError('The original partial-chunk setup phase order differs')
        else:
            failed = row['sleep']['result'] == 'native_property_exception'
            if failed:
                if row['sleep'].get('exception') != 'java.lang.IllegalArgumentException' or 'property' not in row['sleep'].get('message', '') or 'wake' in row:
                    raise ValueError('The original partial-chunk aborted sleep is unexpected or followed by wake')
            elif row['sleep'] != {'result': 'completed'} or row.get('wake') != {'result': 'completed'}:
                raise ValueError('The original partial-chunk callback result is unknown')
            if [packet['kind'] for packet in row['packets']] != ['bed'] + ([] if failed else ['wake']):
                raise ValueError('The original partial-chunk callback packet order differs')
            if any(not isinstance(packet['body'], str) or re.fullmatch('(?:[0-9a-f]{2})+', packet['body']) is None for packet in row['packets']):
                raise ValueError('The original partial-chunk callback body is invalid')
            phases = ['initial'] + ['after_' + action for action in actions] + ['after_sleep'] + ([] if failed else ['after_wake'])
        if [item['operation'] for item in row['observations']] != phases:
            raise ValueError('The original partial-chunk phase order differs')
        for item in [*row.get('setup', []), *row['observations']]:
            validate_snapshot(item)
            if traversal and type(item['placement_clear']) is not bool:
                raise ValueError('The original partial-chunk placement query is unknown')
            if len(item['reads']) != 3:
                raise ValueError('The original partial-chunk cell observations are incomplete')
            for read in item['reads']:
                if type(read['same_as_previous_chunk']) is not bool:
                    raise ValueError('The original partial-chunk object identity is unknown')
                if type(read['bed_native_state']) is not int or read['bed_native_state'] < 0:
                    raise ValueError('The original partial-chunk cell state is unknown')
                if any(type(read[key]) is not bool for key in ['bed_available', 'empty_chunk']) or not isinstance(read['chunk_class'], str) or re.fullmatch(r'[\w.$]+', read['chunk_class']) is None:
                    raise ValueError('The original partial-chunk class or knowledge is unknown')
            if any(item[key] != value for key, value in item['reads'][0].items() if key != 'same_as_previous_chunk'):
                raise ValueError('The original partial-chunk primary cell differs from its read')
    return rows


def fixture(callback_root: Path, traversal_root: Path, full_root: Path, official: Path, data: Path) -> bytes:
    callbacks = read_run(callback_root, official, observation='partial-context.json')
    traversals = read_run(traversal_root, official, observation='partial-context.json')
    fulls = read_run(full_root, official, observation='partial-context.json')
    catalog = {row['name']: row['protocol'] for row in json.loads((data / 'versions.json').read_text())['versions'] if row['protocol'] <= 404}
    if set(callbacks) != set(catalog) or set(traversals) != set(catalog) or set(fulls) != set(catalog) or len(callbacks) != 27:
        raise ValueError('The original partial-chunk release coverage differs')
    cases, releases = [], []
    scopes = None
    for version, (receipt, raw) in callbacks.items():
        tick_receipt, tick_raw = traversals[version]
        full_receipt, full_raw = fulls[version]
        binding = json.loads((callback_root / version / 'bindings.json').read_text())
        tick_binding = json.loads((traversal_root / version / 'bindings.json').read_text())
        contract = binding['clearance_contract']
        spec = binding['partial_context_contract']
        parameters = [contract['world_tick']['chunk_type']] + (['boolean', 'int'] if spec['explicit_ground_up_argument'] else ['int'])
        if spec['packet'] != binding['traversal_contract']['chunk_packet'] or spec['constructor_parameters'] != parameters or spec['section_mask'] != 16:
            raise ValueError('The original partial-chunk constructor differs: ' + version)
        if spec != tick_binding['partial_context_contract']:
            raise ValueError('The original partial-chunk constructor identities disagree: ' + version)
        if raw['origins'].keys() - tick_raw['origins'].keys() or any(tick_receipt['sources_sha256'].get(name) != digest for name, digest in receipt['sources_sha256'].items()):
            raise ValueError('The original partial-chunk source identities disagree: ' + version)
        if tick_raw['traversal_owner'] != contract['world'] or tick_raw['actor_tick_owner'] != binding['actor'] or tick_raw['query_owner'] != contract['world']:
            raise ValueError('The original partial-chunk tick/query owners differ: ' + version)
        if full_receipt['sources_sha256'] != tick_receipt['sources_sha256'] or any(full_raw[name] != tick_raw[name] for name in ['traversal_owner', 'actor_tick_owner', 'query_owner']):
            raise ValueError('The original full-chunk control identities differ: ' + version)
        callback_rows = validate_rows(raw['rows'], traversal=False)
        tick_rows = validate_rows(tick_raw['rows'], traversal=True)
        full_rows = validate_rows(full_raw['rows'], traversal=True, full=True)
        if receipt['protocol'] != catalog[version] or tick_receipt['protocol'] != receipt['protocol'] or full_receipt['protocol'] != receipt['protocol'] or tick_receipt['snapshots'] != 1088 or full_receipt['snapshots'] != 1088 or receipt['snapshots'] != sum(len(row['observations']) for row in callback_rows):
            raise ValueError('The original partial-chunk protocol or observation count differs: ' + version)
        observed_scopes = (raw['scope'], tick_raw['scope'], full_raw['scope'])
        if scopes is not None and scopes != observed_scopes:
            raise ValueError('The original partial-chunk scope differs: ' + version)
        scopes = observed_scopes
        for cohort in [callback_rows, tick_rows, full_rows]:
            for row in cohort:
                for item in [*row.get('setup', []), *row['observations']]:
                    for read in [item, *item['reads']]:
                        key = 'empty_chunk_type' if read['empty_chunk'] else 'chunk_type'
                        if read['chunk_class'] != contract['world_tick'][key]:
                            raise ValueError('The original partial-chunk class differs: ' + version)
        packets = read_packet_bodies(callback_root / version / 'packets', [{'packets': row['world_packets']} for row in callback_rows])
        for digest, body in read_packet_bodies(traversal_root / version / 'packets', [{'packets': row['world_packets']} for row in tick_rows]).items():
            if digest in packets and packets[digest] != body:
                raise ValueError('Original partial-chunk body hashes collide')
            packets[digest] = body
        for digest, body in read_packet_bodies(full_root / version / 'packets', [{'packets': row['world_packets']} for row in full_rows]).items():
            if digest in packets and packets[digest] != body:
                raise ValueError('Original full-chunk body hashes collide')
            packets[digest] = body
        case = {'protocol': receipt['protocol'], 'callbacks': callback_rows, 'traversal': tick_rows, 'full': full_rows, 'packet_bodies': packets}
        if case not in cases:
            cases.append(case)
        constructor = {key: value for key, value in spec.items() if key != 'constructor_source'}
        constructor['constructor_source_sha256'] = hashlib.sha256(spec['constructor_source'].encode()).hexdigest()
        releases.append({'version': version, 'protocol': receipt['protocol'], 'case': cases.index(case), 'client_sha256': receipt['client_sha256'], 'server_sha256': receipt['server_sha256'], 'callback_sha256': sha(callback_root / version / 'partial-context.json'), 'traversal_sha256': sha(traversal_root / version / 'partial-context.json'), 'full_sha256': sha(full_root / version / 'partial-context.json'), 'sources_sha256': tick_receipt['sources_sha256'], 'partial_constructor': constructor})
    return fixture_bytes({'schema': 1, 'callback_scope': scopes[0], 'traversal_scope': scopes[1], 'full_scope': scopes[2], 'callback_measurement': callback_root.name, 'traversal_measurement': traversal_root.name, 'full_measurement': full_root.name, 'releases': releases, 'cases': cases}, compressed=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ['callbacks', 'traversal', 'full', 'official', 'data', 'out']:
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    contents = fixture(args.callbacks, args.traversal, args.full, args.official, args.data)
    if args.check:
        if not args.out.is_file() or args.out.read_bytes() != contents:
            raise ValueError('The original partial-chunk fixture differs')
    else:
        args.out.write_bytes(contents)
    print('Checked' if args.check else 'Wrote', '27-release original partial-chunk fixture')


if __name__ == '__main__':
    main()
