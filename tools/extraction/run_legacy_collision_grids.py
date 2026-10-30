"""Measure all audited 1.13–1.14.3 collision grids without changing an original jar."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import zipfile

from extract_legacy_collision_grids import RELEASES, sha
from verify_collision_grids import verify


def run(official: Path, output: Path, java: Path) -> None:
    output = output.resolve()
    official = official.resolve()
    java = java.resolve()
    output.mkdir(exist_ok=False)
    source = Path(__file__).parent
    probe = source / 'shape-extractor/LegacyCollisionGridDump.java'
    bindings = json.loads((source / 'tests/fixtures/legacy-collision-grid-bindings.json').read_text())
    if bindings['schema'] != 1 or {row['version'] for row in bindings['releases']} != set(RELEASES) or len(bindings['releases']) != len(RELEASES):
        raise ValueError('The audited collision-grid symbol cohort differs')
    for path, name in [(probe, probe.name), (Path(__file__), 'driver.py'), (source / 'verify_collision_grids.py', 'verify_collision_grids.py'), (source / 'tests/fixtures/legacy-collision-grid-bindings.json', 'audited-bindings.json')]:
        shutil.copy2(path, output / name)
    classes = output / 'classes'
    classes.mkdir()
    compile_command = [str(java.with_name('javac')), '--release', '11', '-d', str(classes), str(output / probe.name)]
    with (output / 'compile.log').open('x') as log:
        subprocess.run(compile_command, cwd=output, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=120)
    results = []
    for binding in bindings['releases']:
        version = binding['version']
        directory = output / version
        directory.mkdir()
        row = {'version': version, 'protocol': RELEASES[version], 'status': 'failed'}
        try:
            downloads = official / 'downloads' / version
            metadata = json.loads((downloads / 'version.json').read_text())
            for side in ['client', 'server']:
                with (downloads / (side + '.jar')).open('rb') as stream:
                    if hashlib.file_digest(stream, 'sha1').hexdigest() != metadata['downloads'][side]['sha1']:
                        raise ValueError('Official artifact hash differs: ' + side)
            tree = official / (version + '-client-decompiled')
            if not (tree / '.complete.json').is_file():
                raise ValueError('Missing completed vanilla client source tree')
            for name, digest in binding['sources_sha256'].items():
                if sha(tree / name) != digest:
                    raise ValueError('Audited native source differs: ' + name)
            client = directory / 'client-unsigned.jar'
            # Strip signatures only in this run-owned copy. Every original class byte must remain identical.
            with zipfile.ZipFile(downloads / 'client.jar') as original, zipfile.ZipFile(client, 'x', zipfile.ZIP_DEFLATED) as unsigned:
                for entry in original.infolist():
                    upper = entry.filename.upper()
                    if upper.startswith('META-INF/') and (upper.endswith(('.SF', '.RSA', '.DSA', '.EC')) or upper == 'META-INF/MANIFEST.MF'):
                        continue
                    unsigned.writestr(entry, original.read(entry.filename))
            with zipfile.ZipFile(downloads / 'client.jar') as original, zipfile.ZipFile(client) as unsigned:
                names = [name for name in original.namelist() if name.endswith('.class')]
                if any(original.read(name) != unsigned.read(name) for name in names):
                    raise ValueError('The owned signature-free copy changed an original class')
            (directory / 'bindings.json').write_text(json.dumps(binding, indent=2) + '\n')
            command = [str(java), '-Xmx1G', '-XX:-UsePerfData', '-cp', os.pathsep.join(map(str, [classes, client, downloads / 'server.jar'])), 'LegacyCollisionGridDump', str(directory / 'bindings.json'), str(client), str(directory / 'grids.json')]
            with (directory / 'native.log').open('x') as log:
                subprocess.run(command, cwd=directory, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=180)
            raw = json.loads((directory / 'grids.json').read_text())
            verify(raw)
            row.update(status='complete', states=len(raw['states']), groups=len(raw['groups']), scope=raw['scope'], command=command, original_classes_equal=len(names), origins=raw['origins'], sources_sha256=binding['sources_sha256'], client_sha256=sha(downloads / 'client.jar'), server_sha256=sha(downloads / 'server.jar'), unsigned_sha256=sha(client))
        except (OSError, ValueError, subprocess.SubprocessError, zipfile.BadZipFile) as error:
            row['error'] = repr(error)
        row['files_sha256'] = {p.name: sha(p) for p in directory.iterdir() if p.is_file()}
        results.append(row)
        (output / 'results.json').write_text(json.dumps(results, indent=2) + '\n')
        print(version, row['status'], row.get('error', (row.get('states'), row.get('groups'))), flush=True)
    (output / 'tools.json').write_text(json.dumps({'compile_command': compile_command, 'files_sha256': {str(p.relative_to(output)): sha(p) for p in output.rglob('*') if p.is_file() and p.name not in ['tools.json', 'results.json']}}, indent=2) + '\n')
    if len(results) != len(RELEASES) or any(row['status'] != 'complete' for row in results):
        raise ValueError('The original collision-grid campaign is incomplete; inspect results.json')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--official', required=True, type=Path)
    parser.add_argument('--out', required=True, type=Path)
    parser.add_argument('--java', type=Path, help='Java 11 executable; defaults to the shared java11 runtime')
    args = parser.parse_args()
    run(args.official, args.out, args.java or args.official / 'runtimes/java11/bin/java')


if __name__ == '__main__':
    main()
