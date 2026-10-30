"""Import original occupied-bed state transitions through native world mutation."""

import argparse
import json
from pathlib import Path

from extract_entity_geometry import fixture_bytes
from extract_legacy_collision_grids import sha
from extract_legacy_player_clearance import read_run


def reduce_states(protocol: int, raw: dict, beds: list[list[int]]) -> list[list[int]]:
    expected = {row[0] for row in beds}
    states: dict[int, int] = {}
    for row in raw['rows']:
        state = row['wire_state']
        if type(state) is not int or state not in expected or state in states:
            raise ValueError('The native occupied input is unknown or repeated')
        if row['after_occupied'] is not False:
            raise ValueError('The native occupied result is not false')
        if row['other_properties_equal'] is not True:
            raise ValueError('The native wake changed another property')
        before = row['decoded_native_state']
        after = row['after_native_state']
        result = row['after_wire_state']
        if any(type(value) is not int or value < 0 for value in [before, after, result]):
            raise ValueError('The native occupied state identity is invalid')
        if protocol < 393:
            if before & 4095 != state >> 4 or after & 4095 != state >> 4:
                raise ValueError('The original decoded bed changed block identity')
            encoded = ((after & 4095) << 4) | (after >> 12)
        else:
            if before != state:
                raise ValueError('The original flattened bed changed state identity')
            encoded = after
        if result != encoded or result not in expected:
            raise ValueError('The native and wire occupied outputs differ')
        states[state] = result
    if set(states) != expected:
        raise ValueError('The native occupied state coverage is incomplete')
    return [[state, result] for state, result in sorted(states.items())]


def fixture(root: Path, official: Path, data: Path) -> bytes:
    measurements = read_run(root, official, observation='occupied.json')
    catalog = {row['name']: row['protocol'] for row in json.loads((data / 'versions.json').read_text())['versions'] if row['protocol'] <= 404}
    if set(measurements) != set(catalog) or len(measurements) != 27:
        raise ValueError('The native occupied release coverage differs')
    cases, releases = [], []
    for version, (receipt, raw) in measurements.items():
        protocol = catalog[version]
        binding = json.loads((root / version / 'bindings.json').read_text())
        contract = binding['clearance_contract']
        owners = {'set_block': contract['world'], 'state_id': contract['lifecycle']['state_id_owner'], 'decode_state': contract['lifecycle']['state_id_owner'], 'wake': contract['player']}
        if receipt['protocol'] != protocol or raw['method_owners'] != owners:
            raise ValueError('The native occupied protocol or method owners differ: ' + version)
        beds = json.loads((data / str(protocol) / 'bed-facings.json').read_text())['states']
        states = reduce_states(protocol, raw, beds)
        existing = next((row for row in cases if row['protocol'] == protocol), None)
        if existing is None:
            existing = {'protocol': protocol, 'states': states}
            cases.append(existing)
        elif existing['states'] != states:
            raise ValueError('The native occupied aliases disagree: ' + version)
        releases.append({'version': version, 'protocol': protocol, 'case': cases.index(existing), 'client_sha256': receipt['client_sha256'], 'server_sha256': receipt['server_sha256'], 'measurement_sha256': sha(root / version / 'occupied.json'), 'sources_sha256': receipt['sources_sha256'], 'method_owners': raw['method_owners']})
    return fixture_bytes({'schema': 1, 'scope': raw['scope'], 'measurement': root.name, 'releases': releases, 'cases': cases}, compressed=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ['measurements', 'official', 'data', 'out']:
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    contents = fixture(args.measurements, args.official, args.data)
    if args.check:
        if not args.out.is_file() or args.out.read_bytes() != contents:
            raise ValueError('The native occupied fixture differs')
    else:
        args.out.write_bytes(contents)
    print('Checked' if args.check else 'Wrote', '27-release occupied-bed fixture')


if __name__ == '__main__':
    main()
