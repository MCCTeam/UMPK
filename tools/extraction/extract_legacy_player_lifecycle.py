"""Rebuild legacy player lifecycle witnesses from verified original client callbacks."""

import argparse
import itertools
import json
from pathlib import Path

from extract_entity_geometry import fixture_bytes
from extract_legacy_collision_grids import sha
from extract_legacy_player_clearance import read_run


def validate_owners(contract: dict, raw: dict, *, classic: bool = False, size_phase: bool = False) -> None:
    if size_phase:
        expected = {'setSize': contract['entity'], 'sizePhase': contract['player'], 'setPosition': contract['entity'], 'setFlags': contract['entity'], 'getBoundingBox': contract['entity']}
    else:
        expected = {'sleep': contract['player'], 'wake': contract['player'], 'status': contract['player'], 'size_phase': 'absent' if classic else contract['player'], 'set_position': contract['entity']}
    if raw['method_owners'] != expected:
        raise ValueError('The native player callback owners differ')


def reduce_release(receipt: dict, raw: dict) -> dict:
    classic = receipt['protocol'] == 47
    expected = set(itertools.product(range(4), [0, 2] if classic else [0, 2, 128, 130], range(6), range(3)))
    seen: set[tuple] = set()
    rows = []
    snapshots = 0
    if len(raw['positions']) != 6 or len(raw['bed_faces']) != 4 or {(face[1], face[2]) for face in raw['bed_faces']} != {(0, -1), (0, 1), (-1, 0), (1, 0)}:
        raise ValueError('The native lifecycle positions or bed facings are incomplete')
    for row in raw['rows']:
        key = tuple(row[name] for name in ['facing', 'flags', 'position', 'sequence'])
        if key not in expected or key in seen:
            raise ValueError('Duplicate or unexpected native lifecycle case')
        seen.add(key)
        operations = ['initial']
        if row['sequence'] != 1:
            operations += ['sleep', 'sleep_size_phase']
        if row['sequence'] != 0:
            operations += ['death_event', 'death_size_phase']
        if row['sequence'] != 1:
            operations += ['wake_after_bed_removal', 'wake_size_phase']
        operations += ['explicit_position']
        if classic:
            operations = [name for name in operations if not name.endswith('_size_phase')]
        if [step['operation'] for step in row['transitions']] != operations:
            raise ValueError('The native lifecycle callback sequence differs')
        transitions = []
        for step in row['transitions']:
            transitions.append({'operation': step['operation'], 'size': step['size'], 'position': step['state']['position'], 'box': step['state']['box'], 'sleeping': step['sleeping'], 'flags': step['shared_flags'], 'query_box': step.get('query_box'), 'query_result': step.get('query_result')})
        snapshots += len(transitions)
        rows.append({**{name: row[name] for name in ['facing', 'flags', 'position', 'sequence', 'bed']}, 'transitions': transitions})
    if seen != expected or snapshots != (576 if classic else 1728):
        raise ValueError('The native lifecycle matrix is incomplete')
    return {'positions': raw['positions'], 'facings': [face[1:] for face in raw['bed_faces']], 'rows': rows}


def fixture(classic: Path, phases: Path, official: Path) -> bytes:
    cohorts = [(classic, read_run(classic, official, observation='lifecycle.json')), (phases, read_run(phases, official, observation='lifecycle.json'))]
    if len(cohorts[0][1]) != 10 or len(cohorts[1][1]) != 17 or set(cohorts[0][1]) & set(cohorts[1][1]):
        raise ValueError('The native lifecycle cohorts differ')
    cases, releases = [], []
    aliases: dict[int, dict] = {}
    for root, measurements in cohorts:
        tools = json.loads((root / 'tools.json').read_text())
        for version, (receipt, raw) in measurements.items():
            contract = json.loads((root / version / 'bindings.json').read_text())['clearance_contract']
            validate_owners(contract, raw, classic=receipt['protocol'] == 47)
            reduced = reduce_release(receipt, raw)
            protocol = receipt['protocol']
            if aliases.setdefault(protocol, reduced) != reduced:
                raise ValueError('Native lifecycle aliases disagree: ' + version)
            if reduced not in cases:
                cases.append(reduced)
            releases.append({'version': version, 'protocol': protocol, 'case': cases.index(reduced), 'proof': {'scope': raw['scope'], 'client_sha256': receipt['client_sha256'], 'server_sha256': receipt['server_sha256'], 'measurement_sha256': sha(root / version / 'lifecycle.json'), 'probe_sha256': tools['probe_source_sha256'], 'sources_sha256': receipt['sources_sha256'], 'method_owners': raw['method_owners']}})
    return fixture_bytes({'schema': 1, 'scope': 'Original explicit sleep, wake after bed removal, status 3 death, size and position callbacks; native bed/air client-world queries; no intact-bed exit selection, complete player tick or packet listener execution. This fixture covers geometry, independently of death velocity and health changes.', 'releases': releases, 'cases': cases}, compressed=True)


def reduce_size_release(receipt: dict, raw: dict) -> dict:
    modern = receipt['protocol'] >= 393
    mask = 146 if modern else 130
    flags = [value for value in range(256) if value & ~mask == 0]
    expected = {(size, sleeping, value | (0 if not noise else ~mask & 255), spin, noise, context, point) for size, sleeping, value, spin, noise, context, point in itertools.product(range(4), [False, True], flags, [False, True] if modern else [False], [False, True], range(4), range(3))}
    if raw['flag_mask'] != mask or len(raw['positions']) != 3 or len(raw['initial_sizes']) != 4:
        raise ValueError('The native size-phase axes differ')
    seen: set[tuple] = set()
    rows = []
    for row in raw['rows']:
        key = tuple(row[name] for name in ['from', 'sleeping', 'flags', 'spin', 'noise', 'context', 'position'])
        if key not in expected or key in seen:
            raise ValueError('Duplicate or unexpected native size phase')
        seen.add(key)
        rows.append({name: row[name] for name in ['from', 'sleeping', 'flags', 'spin', 'noise', 'context', 'position', 'before_size', 'before', 'stone', 'query_count', 'query_box', 'after_size', 'after_phase', 'after_position']} | {'query_result': row.get('query_result')})
    if seen != expected:
        raise ValueError('The native size-phase matrix is incomplete')
    return {'positions': raw['positions'], 'initial_sizes': raw['initial_sizes'], 'rows': rows}


def size_fixture(root: Path, official: Path) -> bytes:
    measurements = read_run(root, official)
    if len(measurements) != 17:
        raise ValueError('The native size-phase cohort differs')
    cases, releases = [], []
    aliases: dict[int, dict] = {}
    tools = json.loads((root / 'tools.json').read_text())
    for version, (receipt, raw) in measurements.items():
        contract = json.loads((root / version / 'bindings.json').read_text())['clearance_contract']
        validate_owners(contract, raw, size_phase=True)
        reduced = reduce_size_release(receipt, raw)
        if aliases.setdefault(receipt['protocol'], reduced) != reduced:
            raise ValueError('Native size-phase aliases disagree: ' + version)
        if reduced not in cases:
            cases.append(reduced)
        releases.append({'version': version, 'protocol': receipt['protocol'], 'case': cases.index(reduced), 'proof': {'scope': raw['scope'], 'client_sha256': receipt['client_sha256'], 'server_sha256': receipt['server_sha256'], 'measurement_sha256': sha(root / version / 'clearance.json'), 'probe_sha256': tools['probe_source_sha256'], 'sources_sha256': receipt['sources_sha256'], 'method_owners': raw['method_owners']}})
    return fixture_bytes({'schema': 1, 'scope': 'Original player size phases and native client-world clearance with controlled initial dimensions, shared flags, sleeping state, living spin flags and air/stone context; no complete actor or world ticks.', 'releases': releases, 'cases': cases}, compressed=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ['classic', 'phases', 'size-phases', 'official', 'out']:
        parser.add_argument('--' + name, required=True, type=Path)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    outputs = {'LegacyPlayerLifecycle.native.json.gz': fixture(args.classic, args.phases, args.official), 'LegacyPlayerSizePhase.native.json.gz': size_fixture(args.size_phases, args.official)}
    for name, contents in outputs.items():
        path = args.out / name
        if args.check:
            if not path.is_file() or path.read_bytes() != contents:
                raise ValueError('The generated legacy player fixture differs: ' + name)
        else:
            path.write_bytes(contents)
    print(('Checked' if args.check else 'Wrote'), '27-release lifecycle and 17-release size-phase fixtures')


if __name__ == '__main__':
    main()
