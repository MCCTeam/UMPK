"""Reconstruct original integer bed-exit and explicit wake geometry witnesses."""

import argparse
import itertools
import json
from pathlib import Path

from extract_entity_geometry import fixture_bytes
from extract_legacy_collision_grids import sha
from extract_legacy_player_clearance import read_run


def reduce_release(protocol: int, raw: dict) -> dict:
    expected = set(itertools.product(range(5), range(4), [0, 2] if protocol == 47 else [0, 2, 128, 130], range(6), [0, 2]))
    seen: set[tuple] = set()
    rows = []
    for row in raw['rows']:
        if row['sequence'] == 1:
            continue
        key = tuple(row[name] for name in ['scene', 'facing', 'flags', 'position', 'sequence'])
        if key not in expected or key in seen:
            raise ValueError('Duplicate or unexpected native wake case')
        seen.add(key)
        snapshots = [step for step in row['transitions'] if step['operation'] == 'wake_with_intact_bed']
        if len(snapshots) != 1:
            raise ValueError('The native wake callback sequence differs')
        wake = snapshots[0]
        if wake['block_writes'] != [[*row['bed'], 4]] or wake['sleeping'] is not False:
            raise ValueError('The original wake state or occupied-state write differs')
        # The original predicate reads the support cell first. Its state and material accessors make no other world reads, so each below-bed read identifies one evaluated candidate.
        candidates = [[cell[0], cell[1] + 1, cell[2]] for cell in wake['exit_query_reads'] if cell[1] == row['bed'][1] - 1]
        if not candidates or len(candidates) > 18:
            raise ValueError('The original bed-exit candidate scan differs')
        rows.append({**{name: row[name] for name in ['scene', 'facing', 'flags', 'position', 'sequence', 'bed']}, 'exit': wake.get('native_exit_cell'), 'candidates': candidates, 'wake_position': wake['state']['position'], 'box': wake['state']['box'], 'size': wake['size']})
    if seen != expected or len(raw['positions']) != 6 or len(raw['bed_faces']) != 4:
        raise ValueError('The original wake matrix is incomplete')
    return {'positions': raw['positions'], 'facings': [face[1:] for face in raw['bed_faces']], 'rows': rows}


def fixture(root: Path, official: Path, data: Path) -> bytes:
    measurements = read_run(root, official, observation='lifecycle.json')
    catalog = {row['name']: row['protocol'] for row in json.loads((data / 'versions.json').read_text())['versions'] if 47 <= row['protocol'] <= 404}
    if set(measurements) != set(catalog) or len(measurements) != 27:
        raise ValueError('The original wake release cohort differs')
    tools = json.loads((root / 'tools.json').read_text())
    cases, releases = [], []
    aliases: dict[int, dict] = {}
    for version, (receipt, raw) in measurements.items():
        protocol = receipt['protocol']
        binding = json.loads((root / version / 'bindings.json').read_text())
        contract = binding['clearance_contract']
        if protocol != catalog[version] or raw['method_owners'].get('wake') != contract['player'] or raw['method_owners'].get('exit_finder') != contract['lifecycle']['exit_owner']:
            raise ValueError('The original wake or exit-finder owner differs: ' + version)
        reduced = reduce_release(protocol, raw)
        if aliases.setdefault(protocol, reduced) != reduced:
            raise ValueError('Original wake aliases disagree: ' + version)
        if reduced not in cases:
            cases.append(reduced)
        releases.append({'version': version, 'protocol': protocol, 'case': cases.index(reduced), 'proof': {'scope': raw['scope'], 'client_sha256': receipt['client_sha256'], 'server_sha256': receipt['server_sha256'], 'measurement_sha256': sha(root / version / 'lifecycle.json'), 'probe_sha256': tools['probe_source_sha256'], 'sources_sha256': receipt['sources_sha256'], 'method_owners': raw['method_owners']}})
    return fixture_bytes({'schema': 1, 'scope': 'Original wake geometry, integer exit and ordered candidate evaluations on five controlled native bed/stone/air neighborhoods. Occupied-state input update is owned; native World.setBlock implementation/notifications, audio, full ticks and packet listeners remain excluded.', 'releases': releases, 'cases': cases}, compressed=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ['measurements', 'official', 'data', 'out']:
        parser.add_argument('--' + name, required=True, type=Path)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    contents = fixture(args.measurements, args.official, args.data)
    if args.check:
        if not args.out.is_file() or args.out.read_bytes() != contents:
            raise ValueError('The generated native player wake fixture differs')
    else:
        args.out.write_bytes(contents)
    print(('Checked' if args.check else 'Wrote'), '27-release original player wake fixture')


if __name__ == '__main__':
    main()
