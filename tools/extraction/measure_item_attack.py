"""Measure original native piercing-component codecs and default inputs."""

import hashlib
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import zipfile

REPO = Path(__file__).resolve().parents[2]
OFFICIAL = Path(os.environ.get('MINECRAFT_OFFICIAL_ROOT', Path.home() / 'Projects/MinecraftOfficial'))
sys.path.insert(0, str(REPO / 'tools/extraction'))
from measure_block_interactions import class_bindings, unpack_jar, unsigned_client, verify_client_origins
from measure_legacy_targeting import verify_artifact


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run(command: list[str], cwd: Path, log: Path) -> None:
    with log.open('w') as stream:
        result = subprocess.run(command, cwd=cwd, stdout=stream, stderr=subprocess.STDOUT, timeout=180)
    if result.returncode:
        raise RuntimeError(f'Native component command failed; inspect {log}')


def main() -> None:
    destination = Path(sys.argv[1])
    versions = sys.argv[2:] or ['1.21.11', '26.1', '26.1.1', '26.1.2', '26.2', '26.3']
    destination.mkdir(parents=True, exist_ok=False)
    java = str(OFFICIAL / 'runtimes/java25/bin/java')
    javac = str(OFFICIAL / 'runtimes/java25/bin/javac')
    helper = REPO / 'tools/extraction/shape-extractor'
    sources = [helper / 'PiercingComponentDump.java', helper / 'PushDump.java', helper / 'MiningDump.java']
    classes = destination / 'classes'
    classes.mkdir()
    run([javac, '--release', '17', '-d', str(classes), *map(str, sources)], destination, destination / 'compile.log')
    (destination / 'tools.json').write_text(json.dumps({str(p): sha(p) for p in [*sources, Path(__file__)]}, indent=2) + '\n')
    results: list[dict] = []
    for version in versions:
        output = destination / version
        output.mkdir()
        cp = output / 'libraries'
        cp.mkdir()
        seed = OFFICIAL / 'downloads' / version
        metadata = json.loads((seed / 'version.json').read_text())
        for side in ['server', 'client', 'client_mappings']:
            if side in metadata['downloads']:
                verify_artifact(seed / ('client_mappings.txt' if side == 'client_mappings' else side + '.jar'), metadata['downloads'][side]['sha1'])
        with zipfile.ZipFile(seed / 'server.jar') as bundle:
            for name in bundle.namelist():
                if name.endswith('.jar') and name.startswith(('META-INF/versions/', 'META-INF/libraries/')):
                    with zipfile.ZipFile(io.BytesIO(bundle.read(name))) as nested:
                        unpack_jar(nested, cp)
        client = cp / 'client-unsigned.jar'
        preservation = unsigned_client(seed / 'client.jar', client)
        mapping = seed / 'client_mappings.txt'
        origins = output / 'classes.log'
        measured = output / 'native.json'
        command = [java, '-Xmx1G', '-XX:-UsePerfData', '-Xlog:class+load=info:file=' + str(origins), '-cp', os.pathsep.join(map(str, [classes, client, cp])), 'PiercingComponentDump', str(mapping) if mapping.exists() else 'none', str(measured)]
        run(command, output, output / 'native.log')
        value = json.loads(measured.read_text())
        assert len(value['cases']) == 64 and len(value['default_components']) > 1400
        assert len({(r['flags'], r['sound_kind'], r['hit_sound_kind']) for r in value['cases']}) == 64
        wanted = {'net.minecraft.world.item.component.PiercingWeapon', 'net.minecraft.sounds.SoundEvent', 'net.minecraft.network.protocol.game.ClientboundContainerSetSlotPacket', 'net.minecraft.util.HashOps'}
        verified = verify_client_origins(origins.read_text(), class_bindings(mapping, wanted), client)
        results.append({'version': version, 'status': 'complete', 'command': command, 'client_sha256': sha(seed / 'client.jar'), 'server_sha256': sha(seed / 'server.jar'), 'measurement_sha256': sha(measured), 'cases': len(value['cases']), 'items': len(value['default_components']), 'preservation': preservation, 'origins': verified})
        (destination / 'results.json').write_text(json.dumps(results, indent=2) + '\n')
        print(version, 'complete', len(value['cases']), len(value['default_components']), flush=True)


if __name__ == '__main__':
    main()
