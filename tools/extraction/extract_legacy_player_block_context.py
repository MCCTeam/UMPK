"""Import original block-update bed context and eligible resize witnesses."""

import argparse
import json
import math
from pathlib import Path
import re

from extract_entity_geometry import fixture_bytes
from extract_legacy_collision_grids import sha
from extract_legacy_player_clearance import read_run
from extract_legacy_player_traversal import read_packet_bodies

CONTEXT_BEDS = ((1, 64, 1), (-17, 64, -17), (15, 64, 15), (16, 64, 16))
RESIZE_BEDS = ((1, 64, 1), (-18, 64, -18), (15, 64, 15), (16, 64, 16))


def actions(scene: int, *, resize: bool) -> list[str]:
    initial = scene in ((1, 2, 3, 5) if resize else (1, 2, 4, 5, 7))
    result = (['initial_full'] if initial else []) + (['unload_before_update'] if scene == 2 else []) + ['bed_update']
    if resize:
        result += (['unload_after_update', 'second_bed_update'] if scene == 3 else []) + (['full_after_update'] if scene in (4, 5) else [])
    else:
        result += (['unload_after_update'] if scene == 5 else []) + (['full_after_update'] if scene in (3, 4, 5) else []) + (['air_update'] if scene in (6, 7) else [])
    return result


def validate_snapshot(item: dict) -> None:
    if any(type(item[name]) is not bool for name in ['added', 'in_entity_list', 'sleeping', 'bed_available', 'empty_chunk']):
        raise ValueError('The original block-context knowledge or membership is unknown')
    if not isinstance(item['chunk_class'], str) or re.fullmatch(r'[\w.$]+', item['chunk_class']) is None:
        raise ValueError('The original block-context chunk class is missing')
    if type(item['age']) is not int or item['age'] < 0 or type(item['bed_native_state']) is not int or item['bed_native_state'] < 0:
        raise ValueError('The original block-context actor identity is invalid')
    if len(item['registered_chunk']) != 3 or any(type(value) is not int for value in item['registered_chunk']):
        raise ValueError('The original block-context membership identity is invalid')
    vectors = [(item['size'], 2), (item['velocity'], 3), (item['state']['position'], 3), (item['state']['rotation'], 2), (item['state']['target'], 5), (item['state']['box'], 6), ([item['health']], 1)]
    if any(len(values) != length or any(type(value) not in (int, float) or not math.isfinite(value) for value in values) for values, length in vectors):
        raise ValueError('The original block-context geometry is invalid')


def validate_world_packets(packets: list[dict], expected: list[str]) -> None:
    if [packet['action'] for packet in packets] != expected:
        raise ValueError('The original block-context packet order differs')
    for packet in packets:
        if type(packet['bytes']) is not int or packet['bytes'] <= 0 or re.fullmatch('[0-9a-f]{64}', packet['sha256']) is None:
            raise ValueError('The original block-context packet receipt is invalid')


def validate_context(rows: list[dict]) -> list[dict]:
    expected = {(point, scene) for point in range(4) for scene in range(8)}
    actual = [(row['point'], row['scene']) for row in rows]
    if len(actual) != len(expected) or set(actual) != expected:
        raise ValueError('The original block-context scenario coverage differs')
    for row in rows:
        if row['bed'] != list(CONTEXT_BEDS[row['point']]):
            raise ValueError('The original block-context coordinate differs')
        failed = row['sleep']['result'] == 'native_property_exception'
        if failed:
            if row['sleep'].get('exception') != 'java.lang.IllegalArgumentException' or 'property' not in row['sleep'].get('message', '') or 'wake' in row:
                raise ValueError('The original aborted block-context sleep is unexpected or followed by wake')
        elif row['sleep'] != {'result': 'completed'} or row.get('wake') != {'result': 'completed'}:
            raise ValueError('The original block-context callback result is unknown')
        expected_actions = actions(row['scene'], resize=False)
        validate_world_packets(row['world_packets'], expected_actions)
        phases = ['initial'] + ['after_' + action for action in expected_actions if action != 'initial_full'] + ['after_sleep_packet'] + ([] if failed else ['after_wake_packet'])
        if [item['operation'] for item in row['observations']] != phases:
            raise ValueError('The original block-context callback phase order differs')
        if [packet['kind'] for packet in row['packets']] != ['bed'] + ([] if failed else ['wake']):
            raise ValueError('The original block-context bed packet order differs')
        if any(not isinstance(packet['body'], str) or re.fullmatch('(?:[0-9a-f]{2})+', packet['body']) is None for packet in row['packets']):
            raise ValueError('The original block-context bed packet body is invalid')
        for item in row['observations']:
            validate_snapshot(item)
    return rows


def validate_resize(rows: list[dict], *, fixed_positions: bool = False) -> list[dict]:
    expected = {(point, flags, scene) for point in range(4) for flags in [2, 128] for scene in range(6)}
    actual = [(row['point'], row['flags'], row['scene']) for row in rows]
    if len(actual) != len(expected) or set(actual) != expected:
        raise ValueError('The original block-resize scenario coverage differs')
    for row in rows:
        if row['bed'] != list(RESIZE_BEDS[row['point']]):
            raise ValueError('The original block-resize coordinate differs')
        expected_actions = actions(row['scene'], resize=True)
        validate_world_packets(row['packets'], expected_actions)
        setup = ['before_updates'] + ['after_' + action for action in expected_actions if action != 'initial_full']
        phases = ['initial'] + [f'{phase}_tick_{tick}' for tick in range(8) for phase in ['entity', 'world']]
        if [item['operation'] for item in row['setup']] != setup or [item['operation'] for item in row['observations']] != phases:
            raise ValueError('The original block-resize phase order differs')
        for item in [*row['setup'], *row['observations']]:
            validate_snapshot(item)
        if fixed_positions:
            state = row['observations'][0]['state']
            if any(value * 32 != round(value * 32) for value in [*state['position'], *state['target'][:3]]):
                raise ValueError('The original fixed packet coordinate is not exactly representable')
    return rows


def fixture(context_root: Path, resize_root: Path, official: Path, data: Path) -> bytes:
    contexts = read_run(context_root, official, observation='block-context.json')
    resizes = read_run(resize_root, official, observation='block-resize.json')
    catalog = {row['name']: row['protocol'] for row in json.loads((data / 'versions.json').read_text())['versions'] if row['protocol'] <= 404}
    if set(contexts) != set(catalog) or set(resizes) != set(catalog) or len(contexts) != 27:
        raise ValueError('The original block-context release coverage differs')
    cases, releases = [], []
    scopes = None
    for version, (receipt, raw) in contexts.items():
        resize_receipt, resize = resizes[version]
        binding = json.loads((context_root / version / 'bindings.json').read_text())
        contract = binding['clearance_contract']
        if raw['block_update_owner'] != binding['listener_contract']['listener'] or resize['traversal_owner'] != contract['world'] or resize['actor_tick_owner'] != binding['actor']:
            raise ValueError('The original block-context method owners differ: ' + version)
        rows, resize_rows = validate_context(raw['rows']), validate_resize(resize['rows'], fixed_positions=receipt['protocol'] == 47)
        if receipt['protocol'] != catalog[version] or resize_receipt['protocol'] != receipt['protocol'] or resize_receipt['sources_sha256'] != receipt['sources_sha256']:
            raise ValueError('The original block-context producer identities differ: ' + version)
        if receipt['snapshots'] != sum(len(row['observations']) for row in rows) or resize_receipt['snapshots'] != 816:
            raise ValueError('The original block-context observation count differs: ' + version)
        observed_scopes = (raw['scope'], resize['scope'])
        if scopes is not None and scopes != observed_scopes:
            raise ValueError('The original block-context scope differs: ' + version)
        scopes = observed_scopes
        for item in [item for row in rows for item in row['observations']] + [item for row in resize_rows for item in [*row['setup'], *row['observations']]]:
            expected_class = contract['world_tick']['empty_chunk_type'] if item['empty_chunk'] else contract['world_tick']['chunk_type']
            if item['chunk_class'] != expected_class:
                raise ValueError('The original block-context class does not match its native chunk: ' + version)
        for row in rows:
            if row['scene'] == 1 and row['sleep']['result'] != 'completed':
                raise ValueError('The original loaded-bed positive control failed: ' + version)
        bodies = read_packet_bodies(context_root / version / 'packets', [{'packets': row['world_packets']} for row in rows])
        for digest, body in read_packet_bodies(resize_root / version / 'packets', resize_rows).items():
            if digest in bodies and bodies[digest] != body:
                raise ValueError('Original block-context packet hashes collide')
            bodies[digest] = body
        case = {'protocol': receipt['protocol'], 'context': rows, 'resize': resize_rows, 'points': resize['points'], 'packet_bodies': bodies}
        if case not in cases:
            cases.append(case)
        releases.append({'version': version, 'protocol': receipt['protocol'], 'case': cases.index(case), 'client_sha256': receipt['client_sha256'], 'server_sha256': receipt['server_sha256'], 'context_sha256': sha(context_root / version / 'block-context.json'), 'resize_sha256': sha(resize_root / version / 'block-resize.json'), 'sources_sha256': receipt['sources_sha256']})
    return fixture_bytes({'schema': 1, 'context_scope': scopes[0], 'resize_scope': scopes[1], 'context_measurement': context_root.name, 'resize_measurement': resize_root.name, 'cases': cases, 'releases': releases}, compressed=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ['context', 'resize', 'official', 'data', 'out']:
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    contents = fixture(args.context, args.resize, args.official, args.data)
    if args.check:
        if not args.out.is_file() or args.out.read_bytes() != contents:
            raise ValueError('The original block-context fixture differs')
    else:
        args.out.write_bytes(contents)
    print('Checked' if args.check else 'Wrote', '27-release original block-context fixture')


if __name__ == '__main__':
    main()
