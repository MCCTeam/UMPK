"""Import original legacy metadata, hurt and repeated death callback witnesses."""

import argparse
import json
import math
from pathlib import Path
import re

from extract_entity_geometry import fixture_bytes
from extract_legacy_collision_grids import sha
from extract_legacy_player_clearance import read_run
from extract_legacy_player_traversal import read_packet_bodies


YAWS = (0., 45., 90., -90., -178.59375, 178.59375)


def validate_rows(rows: list[dict], *, clears_fire: bool) -> list[dict]:
    expected = {(angle, flags, health, hurt) for angle in range(len(YAWS)) for flags in [0, 1, 2, 3, 128, 129, 130, 131] for health in [20., 0.] for hurt in [False, True]}
    actual = [(row['angle'], row['flags'], row['health'], row['hurt']) for row in rows]
    if len(actual) != len(expected) or set(actual) != expected or any(type(row['hurt']) is not bool or type(row['health']) not in (int, float) for row in rows):
        raise ValueError('The original death prior-state scenario coverage differs')
    for row in rows:
        phases = ['initial', 'after_health_metadata'] + (['after_hurt_status'] if row['hurt'] else []) + ['after_death_status', 'after_repeated_death_status']
        actions = ['health_metadata'] + (['hurt_status'] if row['hurt'] else []) + ['death_status', 'repeated_death_status']
        if [item['operation'] for item in row['observations']] != phases:
            raise ValueError('The original death phase order differs')
        if [packet['action'] for packet in row['packets']] != actions:
            raise ValueError('The original death packet order differs')
        for packet in row['packets']:
            if type(packet['bytes']) is not int or packet['bytes'] <= 0 or re.fullmatch('[0-9a-f]{64}', packet['sha256']) is None:
                raise ValueError('The original death packet receipt is invalid')
        initial = row['observations'][0]
        first_death = row['observations'][-2]
        for item in row['observations']:
            if type(item['damage_yaw']) not in (int, float) or item['damage_yaw'] != 0:
                raise ValueError('The original remote damage yaw is unknown or changed')
            vectors = [(item['size'], 2), (item['velocity'], 3), (item['state']['position'], 3), (item['state']['rotation'], 2), (item['state']['target'], 5), (item['state']['box'], 6)]
            if any(len(values) != length or any(type(value) not in (int, float) or not math.isfinite(value) for value in values) for values, length in vectors):
                raise ValueError('The original death geometry is invalid')
            if item['state']['rotation'] != [YAWS[row['angle']], 0.]:
                raise ValueError('The original death current yaw changed')
            if item['state']['target'] != initial['state']['target'] or item['state']['active'] is not True:
                raise ValueError('The original death callback changed pending interpolation')
            if item['state']['position'] != initial['state']['position'] or item['age'] != 0 or any(type(item[name]) is not bool for name in ['added', 'in_entity_list', 'sleeping']):
                raise ValueError('The original death lifecycle state changed unexpectedly')
            dead = item['operation'] in ['after_death_status', 'after_repeated_death_status']
            health = 20. if item['operation'] == 'initial' else 0. if dead else row['health']
            if type(item['health']) not in (int, float) or item['health'] != health:
                raise ValueError('The original death health differs from its metadata/status operation')
            flags = 0 if item['operation'] == 'initial' else row['flags'] & ~1 if dead and clears_fire else row['flags']
            if type(item['flags']) is not int or item['flags'] != flags:
                raise ValueError('The original death changed unexpected flags')
            if not dead and (item['size'] != initial['size'] or item['state']['box'] != initial['state']['box'] or item['velocity'] != initial['velocity']):
                raise ValueError('The original health/hurt metadata was misclassified as death effects')
            if dead and item['size'] != [.2, .2]:
                raise ValueError('The original death callback did not resize its actor')
        for name in ['health', 'velocity', 'size', 'flags', 'sleeping', 'damage_yaw', 'state']:
            if row['observations'][-1][name] != first_death[name]:
                raise ValueError('The original repeated death callback changed its result')
    return rows


def fixture(root: Path, official: Path, data: Path) -> bytes:
    measurements = read_run(root, official, observation='death.json')
    catalog = {row['name']: row['protocol'] for row in json.loads((data / 'versions.json').read_text())['versions'] if row['protocol'] <= 404}
    if set(measurements) != set(catalog) or len(measurements) != 27:
        raise ValueError('The original death release coverage differs')
    cases, releases = [], []
    scope = None
    for version, (receipt, raw) in measurements.items():
        binding = json.loads((root / version / 'bindings.json').read_text())
        contract = binding['clearance_contract']
        owners = {'health_setter': contract['living'], 'health_getter': contract['living'], 'status': contract['player'], 'death': contract['player'], 'metadata_listener': binding['listener_contract']['listener'], 'damage_override': binding['actor']}
        if raw['method_owners'] != owners or binding['death_contract']['remote_damage_source'].strip() != 'return true;':
            raise ValueError('The original death method owners or remote damage override differ: ' + version)
        clears_fire = 'b(0, false)' in binding['death_contract']['death_source']
        rows = validate_rows(raw['rows'], clears_fire=clears_fire)
        if receipt['protocol'] != catalog[version] or receipt['snapshots'] != 864 or raw['yaws'] != list(YAWS):
            raise ValueError('The original death protocol, yaw or observation count differs: ' + version)
        if scope is not None and scope != raw['scope']:
            raise ValueError('The original death scope differs: ' + version)
        scope = raw['scope']
        case = {'protocol': receipt['protocol'], 'clears_fire': clears_fire, 'rows': rows, 'packet_bodies': read_packet_bodies(root / version / 'packets', rows)}
        if case not in cases:
            cases.append(case)
        releases.append({'version': version, 'protocol': receipt['protocol'], 'case': cases.index(case), 'client_sha256': receipt['client_sha256'], 'server_sha256': receipt['server_sha256'], 'measurement_sha256': sha(root / version / 'death.json'), 'sources_sha256': receipt['sources_sha256'], 'method_owners': owners})
    return fixture_bytes({'schema': 1, 'scope': scope, 'measurement': root.name, 'yaws': YAWS, 'releases': releases, 'cases': cases}, compressed=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ['measurements', 'official', 'data', 'out']:
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    contents = fixture(args.measurements, args.official, args.data)
    if args.check:
        if not args.out.is_file() or args.out.read_bytes() != contents:
            raise ValueError('The original death fixture differs')
    else:
        args.out.write_bytes(contents)
    print('Checked' if args.check else 'Wrote', '27-release original death fixture')


if __name__ == '__main__':
    main()
