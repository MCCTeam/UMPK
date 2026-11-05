"""Rebuild the legacy resize-query fixtures from verified original client measurements."""

import argparse
import gzip
import hashlib
import json
from pathlib import Path
import zipfile

from extract_entity_geometry import fixture_bytes
from extract_legacy_collision_grids import sha


def read_run(root: Path, official: Path, *, observation: str = 'clearance.json') -> dict[str, tuple[dict, dict]]:
    receipts = json.loads((root / 'results.json').read_text())
    tools = json.loads((root / 'tools.json').read_text())
    probes = list(root.glob('*.java'))
    if len(probes) != 1 or sha(probes[0]) != tools['probe_source_sha256']:
        raise ValueError('The original clearance probe changed: ' + str(root))
    if 'driver_sha256' in tools and sha(root / 'driver.py') != tools['driver_sha256']:
        raise ValueError('The original clearance driver changed: ' + str(root))
    for path, digest in tools.get('helper_classes_sha256', tools.get('helpers_sha256', {})).items():
        if sha(Path(path)) != digest:
            raise ValueError('An original clearance helper changed: ' + path)
    results = {}
    for receipt in receipts:
        version = receipt['version']
        if version in results or receipt['status'] != 'complete':
            raise ValueError('Duplicate or incomplete native clearance measurement: ' + version)
        directory = root / version
        for name, digest in receipt['files_sha256'].items():
            if sha(directory / name) != digest:
                raise ValueError('A native clearance receipt file changed: ' + version + '/' + name)
        binding = json.loads((directory / 'bindings.json').read_text())
        contract = binding['clearance_contract']
        if receipt['sources_sha256'] != contract['sources_sha256'] or binding['protocol'] != receipt['protocol'] or binding['version'] != version:
            raise ValueError('Native clearance source coverage or identity differs: ' + version)
        for name, digest in contract['sources_sha256'].items():
            if sha(official / (version + '-client-decompiled') / name) != digest:
                raise ValueError('An original clearance source changed: ' + version + '/' + name)
        metadata = json.loads((official / 'downloads' / version / 'version.json').read_text())
        for side in ['client', 'server']:
            artifact = official / 'downloads' / version / (side + '.jar')
            with artifact.open('rb') as stream:
                digest = hashlib.file_digest(stream, 'sha1').hexdigest()
            if digest != metadata['downloads'][side]['sha1'] or sha(artifact) != receipt[side + '_sha256']:
                raise ValueError('An original clearance artifact changed: ' + version + '/' + side)
        raw = json.loads((directory / observation).read_text())
        if len(raw['rows']) != receipt['rows'] or set(raw['origins']) != {Path(name).stem for name in contract['sources_sha256']}:
            raise ValueError('Native clearance observations or class-origin coverage differs: ' + version)
        origins = set(raw['origins'].values())
        if len(origins) != 1:
            raise ValueError('Native clearance classes have mixed origins: ' + version)
        origin = Path(next(iter(origins)))
        if sha(origin) != receipt['unsigned_sha256']:
            raise ValueError('The original clearance class container changed: ' + version)
        with zipfile.ZipFile(official / 'downloads' / version / 'client.jar') as original, zipfile.ZipFile(origin) as unsigned:
            names = [name for name in original.namelist() if name.endswith('.class')]
            if len(names) != receipt.get('original_class_files_equal', receipt.get('original_classes_equal')) or any(original.read(name) != unsigned.read(name) for name in names):
                raise ValueError('An original clearance class was modified: ' + version)
        results[version] = receipt, raw
    return results


def fixtures(classic: Path, voxel: Path, phase: Path, composite: Path, official: Path) -> dict[str, bytes]:
    base = read_run(classic, official)
    for version, value in read_run(voxel, official).items():
        if version in base:
            raise ValueError('The classic and voxel clearance cohorts overlap')
        base[version] = value
    phases = read_run(phase, official)
    if set(base) != set(phases) or len(base) != 17:
        raise ValueError('The clearance and player-phase cohorts differ')
    cases, releases = [], []
    for version, (receipt, original) in base.items():
        protocol = receipt['protocol']
        sizes = phases[version][1]
        seen, rows = {}, []
        observations = [(r['box'], r['stone'], not r['blocked']) for r in original['rows']]
        observations += [(r['query_box'], r['stone'], r['query_result'] if protocol >= 393 else not r['query_result']) for r in sizes['rows'] if r['query_count']]
        for box, stone, clear in observations:
            if type(clear) is not bool:
                raise ValueError('A native clearance observation is not Boolean')
            key = json.dumps([box, stone])
            if key in seen:
                if rows[seen[key]][2] != clear:
                    raise ValueError('Duplicate native resize queries disagree: ' + version)
                continue
            seen[key] = len(rows)
            rows.append([box, stone, clear])
        if rows not in cases:
            cases.append(rows)
        releases.append({'version': version, 'protocol': protocol, 'case': cases.index(rows), 'client_sha256': receipt['client_sha256'], 'server_sha256': receipt['server_sha256'], 'query_measurement_sha256': sha((voxel if protocol >= 393 else classic) / version / 'clearance.json'), 'phase_measurement_sha256': sha(phase / version / 'clearance.json')})
    single = {'schema': 1, 'scope': 'Original null-actor native client-world clearance and player-resize query witnesses; controlled air/stone states, original loaded-block/availability/validity methods; no complete ticks or bed/death handlers', 'releases': releases, 'cases': cases}
    composites = []
    observations = read_run(composite, official)
    if set(observations) != {'1.13', '1.13.1', '1.13.2'}:
        raise ValueError('The composite clearance cohort differs')
    tools = json.loads((composite / 'tools.json').read_text())
    for version, (receipt, raw) in observations.items():
        if len(raw['rows']) != raw['query_count'] * len(raw['masks']) or len(raw['rows']) != 4488 or any(type(row[2]) is not bool for row in raw['rows']):
            raise ValueError('The native composite query matrix is incomplete: ' + version)
        composites.append({'version': version, 'protocol': receipt['protocol'], 'cells': raw['cells'], 'rows': [row[:3] for row in raw['rows']], 'proof': {'scope': raw['scope'], 'client_sha256': receipt['client_sha256'], 'server_sha256': receipt['server_sha256'], 'measurement_sha256': receipt['files_sha256']['clearance.json'], 'probe_sha256': tools['probe_source_sha256'], 'sources_sha256': receipt['sources_sha256']}})
    combined = {'schema': 1, 'releases': composites}
    return {'LegacyPlayerClearance.native.json.gz': fixture_bytes(single, compressed=True), 'LegacyCompositeClearance.native.json.gz': gzip.compress((json.dumps(combined, separators=(',', ':')) + '\n').encode(), mtime=0)}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    for key in ['classic', 'voxel', 'phase', 'composite', 'official', 'out']:
        parser.add_argument('--' + key, required=True, type=Path)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    outputs = fixtures(args.classic, args.voxel, args.phase, args.composite, args.official)
    for name, contents in outputs.items():
        path = args.out / name
        if args.check:
            if not path.is_file() or path.read_bytes() != contents:
                raise ValueError('The generated legacy clearance fixture differs: ' + str(path))
        else:
            path.write_bytes(contents)
    print(('Checked' if args.check else 'Wrote'), len(outputs), 'original legacy resize-query fixtures')


if __name__ == '__main__':
    main()
