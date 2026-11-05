"""Import verified original collision-grid measurements for Java 1.13 through 1.14.3."""

import argparse
import hashlib
import json
from pathlib import Path
import zipfile

from verify_collision_grids import verify

RELEASES = {'1.13': 393, '1.13.1': 401, '1.13.2': 404, '1.14': 477, '1.14.1': 480, '1.14.2': 485, '1.14.3': 490}


def sha(path: Path) -> str:
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def read_measurements(measurements: Path, official: Path) -> list[tuple[int, dict]]:
    receipts = json.loads((measurements / 'results.json').read_text())
    if len(receipts) != len(RELEASES) or {row['version'] for row in receipts} != set(RELEASES):
        raise ValueError('Collision-grid receipts must cover each audited release exactly once')
    manifest = json.loads((measurements / 'tools.json').read_text())['files_sha256']
    probe = Path(__file__).parent / 'shape-extractor/LegacyCollisionGridDump.java'
    if sha(probe) != manifest.get(probe.name):
        raise ValueError('The original collision-grid probe differs from the reviewed source')
    for relative, digest in manifest.items():
        path = measurements / relative
        if not path.resolve().is_relative_to(measurements.resolve()) or sha(path) != digest:
            raise ValueError('Collision-grid run file changed: ' + relative)
    results = []
    for receipt in receipts:
        version = receipt['version']
        protocol = RELEASES[version]
        if receipt['status'] != 'complete' or receipt['protocol'] != protocol:
            raise ValueError('Incomplete original collision-grid measurement: ' + version)
        root = measurements / version
        for name, digest in receipt['files_sha256'].items():
            if sha(root / name) != digest:
                raise ValueError('Collision-grid receipt file changed: ' + version + '/' + name)
        raw = json.loads((root / 'grids.json').read_text())
        binding = json.loads((root / 'bindings.json').read_text())
        verify(raw)
        if binding['version'] != version or raw['origins'] != receipt['origins'] or raw['scope'] != receipt['scope'] or len(raw['states']) != receipt['states'] or len(raw['groups']) != receipt['groups']:
            raise ValueError('Collision-grid identity, scope or measurement counts differ: ' + version)
        if receipt['sources_sha256'] != binding['sources_sha256']:
            raise ValueError('Collision-grid source coverage differs: ' + version)
        tree = official / (version + '-client-decompiled')
        if not (tree / '.complete.json').is_file():
            raise ValueError('Missing completed vanilla source tree: ' + version)
        for name, digest in binding['sources_sha256'].items():
            if sha(tree / name) != digest:
                raise ValueError('Original collision-grid source changed: ' + version + '/' + name)
        downloads = official / 'downloads' / version
        metadata = json.loads((downloads / 'version.json').read_text())
        for side in ['client', 'server']:
            path = downloads / (side + '.jar')
            with path.open('rb') as stream:
                artifact_sha1 = hashlib.file_digest(stream, 'sha1').hexdigest()
            if artifact_sha1 != metadata['downloads'][side]['sha1'] or sha(path) != receipt[side + '_sha256']:
                raise ValueError('Original collision-grid artifact changed: ' + version + '/' + side)
        origins = set(raw['origins'].values())
        if len(origins) != 1 or set(raw['origins']) != {Path(name).stem for name in binding['sources_sha256']}:
            raise ValueError('Collision-grid native class origins are incomplete: ' + version)
        origin = Path(next(iter(origins)))
        if sha(origin) != receipt['unsigned_sha256']:
            raise ValueError('Original collision-grid class container changed: ' + version)
        with zipfile.ZipFile(downloads / 'client.jar') as original, zipfile.ZipFile(origin) as unsigned:
            names = [name for name in original.namelist() if name.endswith('.class')]
            if len(names) != receipt['original_classes_equal'] or any(original.read(name) != unsigned.read(name) for name in names):
                raise ValueError('Collision-grid probe changed an original class: ' + version)
        provenance = {'kind': 'extracted', 'version': version, 'protocol': protocol, 'tool': 'tools/extraction/shape-extractor/LegacyCollisionGridDump.java', 'sources': ['downloads/' + version + '/client.jar'], 'client_sha256': receipt['client_sha256'], 'server_sha256': receipt['server_sha256'], 'measurement_sha256': sha(root / 'grids.json'), 'bindings_sha256': sha(root / 'bindings.json'), 'probe_sha256': sha(probe), 'native_sources_sha256': binding['sources_sha256'], 'note': raw['scope'] + '. Contextual or position-offset runtime substitutions remain separate.'}
        results.append((protocol, {'_provenance': provenance, 'schema': 1, 'state_count': len(raw['states']), 'cell_order': raw['cell_order'], 'states': raw['states'], 'groups': raw['groups']}))
    return results


def extract(measurements: Path, official: Path, data: Path, *, check: bool) -> None:
    catalog = json.loads((data / 'versions.json').read_text())['versions']
    if {row['name']: row['protocol'] for row in catalog if row['protocol'] in RELEASES.values()} != RELEASES:
        raise ValueError('The version catalog differs from the audited collision-grid cohort')
    outputs = [(data / str(protocol) / 'block-collision-grids.json', (json.dumps(document, indent=2) + '\n').encode()) for protocol, document in read_measurements(measurements, official)]
    for path, contents in outputs:
        if check:
            if not path.is_file() or path.read_bytes() != contents:
                raise ValueError('Generated collision-grid data differs: ' + str(path))
        else:
            path.write_bytes(contents)
    print(('Checked' if check else 'Extracted'), len(outputs), 'complete original collision-grid tables')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--measurements', required=True, type=Path)
    parser.add_argument('--official', required=True, type=Path)
    parser.add_argument('--data', type=Path, default=Path('data/java'))
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    extract(args.measurements, args.official, args.data, check=args.check)


if __name__ == '__main__':
    main()
