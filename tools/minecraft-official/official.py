#!/usr/bin/env python3
"""Provision verified vanilla artifacts and decompile them outside source checkouts."""

import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import platform
import shutil
import subprocess
import tarfile
import tempfile
import urllib.error
import urllib.request
import zipfile

MANIFEST = 'https://piston-meta.mojang.com/mc/game/version_manifest_v2.json'
DECOMPILER_VERSION = 'v3.3.2'
VINEFLOWER_VERSION = '1.12.0'


def read_json(url: str) -> dict:
    with urllib.request.urlopen(url, timeout=60) as response:
        return json.load(response)


def digest(path: Path, algorithm: str = 'sha256') -> str:
    with path.open('rb') as source:
        return hashlib.file_digest(source, algorithm).hexdigest()


def download(url: str, destination: Path, expected: str | None, algorithm: str = 'sha256') -> None:
    if destination.is_file() and expected and digest(destination, algorithm) == expected:
        return
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_name(destination.name + '.part')
    try:
        with urllib.request.urlopen(url, timeout=120) as response, temporary.open('wb') as output:
            shutil.copyfileobj(response, output, 1024 * 1024)
        if expected and digest(temporary, algorithm) != expected:
            raise ValueError(f'{algorithm} mismatch for {destination}')
        temporary.replace(destination)
    finally:
        temporary.unlink(missing_ok=True)


def numeric(version: str) -> tuple[int, ...]:
    return tuple(map(int, version.split('.')))


def select_releases(versions: list[dict], first: str, last: str) -> list[dict]:
    return sorted((entry for entry in versions if entry['type'] == 'release' and numeric(first) <= numeric(entry['id']) <= numeric(last)), key=lambda entry: numeric(entry['id']))


def default_root() -> Path:
    return Path(os.environ.get('MINECRAFT_OFFICIAL_ROOT') or os.environ.get('UMPK_ORACLE_ROOT') or Path.home() / 'Projects' / 'MinecraftOfficial').expanduser().resolve()


def provision(root: Path, entry: dict, sides: list[str], accept_eula: bool) -> dict:
    target = root / 'downloads' / entry['id']
    download(entry['url'], target / 'version.json', entry['sha1'], 'sha1')
    metadata = json.loads((target / 'version.json').read_text(encoding='utf-8'))
    records = []
    for side in sides:
        for key in [side, side + '_mappings']:
            artifact = metadata['downloads'].get(key)
            if artifact is None:
                continue
            path = target / (key + ('.txt' if key.endswith('_mappings') else '.jar'))
            download(artifact['url'], path, artifact['sha1'], 'sha1')
            records.append({'name': path.name, 'sha1': artifact['sha1'], 'sha256': digest(path), 'size': path.stat().st_size, 'url': artifact['url']})
    existing = target / 'artifacts.json'
    if existing.exists():
        retained = {record['name']: record for record in json.loads(existing.read_text(encoding='utf-8'))}
        retained.update({record['name']: record for record in records})
        records = list(retained.values())
    existing.write_text(json.dumps(records, indent=2) + '\n', encoding='utf-8')
    if 'server' in sides:
        eula = target / 'eula.txt'
        if accept_eula or not eula.exists():
            eula.write_text(f'eula={str(accept_eula).lower()}\n', encoding='utf-8')
        properties = target / 'server.properties'
        if not properties.exists():
            properties.write_text('server-ip=127.0.0.1\nserver-port=25565\nonline-mode=false\nwhite-list=false\nenforce-whitelist=false\nenforce-secure-profile=false\nenable-rcon=false\nenable-query=false\ngamemode=creative\nspawn-protection=0\nview-distance=4\nmax-players=5\n', encoding='utf-8')
    return metadata


def release_asset(root: Path, repository: str, tag: str, name: str, destination: Path) -> None:
    if destination.is_file():
        record = destination.with_name(destination.name + '.provenance.json')
        if record.exists() and json.loads(record.read_text(encoding='utf-8'))['sha256'] == digest(destination):
            return
    release = read_json(f'https://api.github.com/repos/{repository}/releases/tags/{tag}')
    asset = next(asset for asset in release['assets'] if asset['name'] == name)
    checksum = (asset.get('digest') or '').removeprefix('sha256:') or None
    download(asset['browser_download_url'], destination, checksum)
    destination.with_name(destination.name + '.provenance.json').write_text(json.dumps({'repository': repository, 'tag': tag, 'url': asset['browser_download_url'], 'sha256': digest(destination), 'publisher_sha256': checksum}, indent=2) + '\n', encoding='utf-8')


def install_runtime(root: Path, major: int) -> Path:
    alias = root / 'runtimes' / f'java{major}'
    java = alias / 'bin' / 'java'
    if java.is_file():
        return java
    if platform.system() != 'Linux' or platform.machine() not in ('x86_64', 'aarch64'):
        raise ValueError('Automatic runtime installation supports Linux x64/ARM64. Supply --java on other systems.')
    architecture = 'x64' if platform.machine() == 'x86_64' else 'aarch64'
    release = read_json(f'https://api.github.com/repos/adoptium/temurin{major}-binaries/releases/latest')
    asset = next(asset for asset in release['assets'] if asset['name'].startswith(f'OpenJDK{major}U-jdk_{architecture}_linux_hotspot_') and asset['name'].endswith('.tar.gz'))
    with urllib.request.urlopen(asset['browser_download_url'] + '.sha256.txt', timeout=60) as response:
        checksum = response.read().decode().split()[0]
    archive = root / 'downloads' / 'runtimes' / asset['name']
    download(asset['browser_download_url'], archive, checksum)
    alias.parent.mkdir(parents=True, exist_ok=True)
    with tarfile.open(archive) as source:
        top = source.getmembers()[0].name.split('/')[0]
        source.extractall(alias.parent, filter='data')
    alias.symlink_to(top, target_is_directory=True)
    (alias.parent / f'java{major}.json').write_text(json.dumps({'tag': release['tag_name'], 'url': asset['browser_download_url'], 'sha256': checksum}, indent=2) + '\n', encoding='utf-8')
    return java


def java_for(root: Path, version: str, override: str | None = None) -> str:
    major = 11 if numeric(version) <= (1, 16, 5) else 25
    selected = override or os.environ.get(f'UMPK_JAVA{major}') or os.environ.get('UMPK_JAVA') or str(root / 'runtimes' / f'java{major}' / 'bin' / 'java')
    if not Path(selected).is_file():
        selected = shutil.which(selected) or shutil.which('java') or selected
    if not Path(selected).is_file():
        raise FileNotFoundError(f'Java {major} is absent. Use --install-runtimes or --java.')
    return selected


def inner_server(jar: Path, destination: Path) -> Path:
    with zipfile.ZipFile(jar) as archive:
        if 'META-INF/versions.list' not in archive.namelist():
            return jar
        rows = archive.read('META-INF/versions.list').decode().strip().splitlines()
        if len(rows) != 1:
            raise ValueError('Expected exactly one bundled server jar')
        checksum, _, relative = rows[0].split()
        path = PurePosixPath(relative)
        if path.is_absolute() or '..' in path.parts:
            raise ValueError('Unsafe bundled server path')
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(archive.read('META-INF/versions/' + relative))
        if digest(destination) != checksum:
            destination.unlink()
            raise ValueError('Bundled server SHA-256 mismatch')
    return destination


def minecraft_classes(jar: Path, destination: Path) -> Path:
    """Select game classes without rewriting the original Mojang artifact."""
    with zipfile.ZipFile(jar) as source, zipfile.ZipFile(destination, 'w', zipfile.ZIP_DEFLATED) as output:
        for name in source.namelist():
            if name.endswith('.class') and ('/' not in name or name.startswith(('net/minecraft/', 'com/mojang/'))):
                output.writestr(name, source.read(name))
    return destination


def decompile(root: Path, version: str, side: str, metadata: dict, java: str, timeout: int) -> None:
    suffix = '' if side == 'server' else '-client'
    destination = root / f'{version}{suffix}-decompiled'
    marker = destination / '.complete.json'
    artifact = metadata['downloads'][side]
    if marker.exists():
        previous = json.loads(marker.read_text(encoding='utf-8'))
        if previous['artifact_sha1'] == artifact['sha1'] and any(destination.rglob('*.java')) and 'OutOfMemoryError' not in Path(previous['log']).read_text(encoding='utf-8', errors='replace'):
            print(f'Reuse {destination}', flush=True)
            return
    if destination.exists():
        raise FileExistsError(f'Incomplete or unverified source tree: {destination}. Inspect it before moving it aside.')
    tools = root / 'downloads' / 'decompiler'
    vineflower = tools / 'vineflower.jar'
    release_asset(root, 'Vineflower/vineflower', VINEFLOWER_VERSION, f'vineflower-{VINEFLOWER_VERSION}.jar', vineflower)
    decompiler = root / 'MinecraftDecompiler.jar'
    log = root / 'logs' / f'{version}{suffix}-decompile.log'
    log.parent.mkdir(parents=True, exist_ok=True)
    temporary = Path(tempfile.mkdtemp(prefix=f'.{version}{suffix}-', dir=root))
    source = temporary / 'source'
    source.mkdir()
    jar = root / 'downloads' / version / f'{side}.jar'
    if side == 'server':
        jar = inner_server(jar, temporary / 'inner.jar')
    mapping = metadata['downloads'].get(side + '_mappings')
    java = java_for(root, '26.3', java)
    try:
        with log.open('w', encoding='utf-8') as output:
            if mapping:
                release_asset(root, 'MaxPixelStudios/MinecraftDecompiler', DECOMPILER_VERSION, 'MinecraftDecompiler.jar', decompiler)
                command = [java, '-Xmx2G', '-jar', str(decompiler), '--input', str(jar), '--mapping-path', str(root / 'downloads' / version / f'{side}_mappings.txt'), '--temp', str(temporary / 'mapper-temp'), '--output', str(temporary / 'remapped.jar')]
                subprocess.run(command, cwd=root, stdout=output, stderr=subprocess.STDOUT, timeout=timeout, check=True)
                jar = temporary / 'remapped.jar'
                mapping_source = 'mojang-proguard'
            else:
                mapping_source = 'unobfuscated' if numeric(version) >= (26, 1) else 'obfuscated-no-community-mappings'
            classpath = jar
            jar = minecraft_classes(jar, temporary / 'minecraft.jar')
            command = [java, '-Xmx4G', '-jar', str(vineflower), '-thr=2', '-log=WARN', '--add-external=' + str(classpath), str(jar), str(source)]
            subprocess.run(command, cwd=root, stdout=output, stderr=subprocess.STDOUT, timeout=timeout, check=True)
        if 'OutOfMemoryError' in log.read_text(encoding='utf-8', errors='replace'):
            raise RuntimeError(f'Decompiler exhausted the heap. Inspect {log}')
        for packed in list(source.glob('*.jar')):
            with zipfile.ZipFile(packed) as archive:
                for name in archive.namelist():
                    path = PurePosixPath(name)
                    if path.is_absolute() or '..' in path.parts:
                        raise ValueError('Unsafe decompiler output path')
                archive.extractall(source)
            packed.unlink()
        count = sum(1 for _ in source.rglob('*.java'))
        if count == 0:
            raise RuntimeError(f'Decompiler produced no Java source. Inspect {log}')
        (source / '.complete.json').write_text(json.dumps({'version': version, 'side': side, 'artifact_sha1': artifact['sha1'], 'mapping_source': mapping_source, 'class_scope': 'minecraft-and-mojang', 'java_files': count, 'decompiler_version': DECOMPILER_VERSION if mapping else None, 'vineflower_version': VINEFLOWER_VERSION, 'log': str(log)}, indent=2) + '\n', encoding='utf-8')
        source.replace(destination)
        print(f'Decompiled {version} {side}: {count} Java files ({mapping_source})', flush=True)
    finally:
        shutil.rmtree(temporary)


def verify(root: Path, versions: list[str]) -> None:
    for version in versions:
        target = root / 'downloads' / version
        metadata = json.loads((target / 'version.json').read_text(encoding='utf-8'))
        for key, entry in metadata['downloads'].items():
            if key not in ('server', 'client', 'server_mappings', 'client_mappings'):
                continue
            path = target / (key + ('.txt' if key.endswith('_mappings') else '.jar'))
            if not path.is_file() or digest(path, 'sha1') != entry['sha1']:
                raise ValueError(f'Missing artifact or SHA-1 mismatch: {path}')
        print(f'Verified {version}', flush=True)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    choice = parser.add_mutually_exclusive_group(required=True)
    choice.add_argument('--version')
    choice.add_argument('--all-releases', action='store_true')
    parser.add_argument('--from-version', default='1.8')
    parser.add_argument('--through', default='26.3')
    parser.add_argument('--root', type=Path, default=default_root())
    parser.add_argument('--side', type=str.lower, choices=('server', 'client', 'both'), default='server')
    parser.add_argument('--download-only', action='store_true')
    parser.add_argument('--accept-eula', action='store_true')
    parser.add_argument('--install-runtimes', action='store_true')
    parser.add_argument('--java')
    parser.add_argument('--timeout', type=int, default=1200)
    parser.add_argument('--verify', action='store_true')
    args = parser.parse_args()
    root = args.root.expanduser().resolve()
    root.mkdir(parents=True, exist_ok=True)
    cached = root / 'version_manifest_v2.json'
    if args.verify and cached.exists():
        manifest = json.loads(cached.read_text(encoding='utf-8'))
    else:
        manifest = read_json(MANIFEST)
        cached.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    entries = select_releases(manifest['versions'], args.from_version, args.through) if args.all_releases else [next(entry for entry in manifest['versions'] if entry['id'] == args.version)]
    if args.all_releases and not any(entry['id'] == args.through for entry in entries):
        parser.error(f'The upper-bound release {args.through} is absent from Mojang metadata')
    if args.verify:
        verify(root, [entry['id'] for entry in entries])
        return 0
    if args.install_runtimes:
        for major in (11, 25):
            install_runtime(root, major)
    sides = ['server', 'client'] if args.side == 'both' else [args.side]
    for entry in entries:
        print(f'Provision {entry["id"]}', flush=True)
        metadata = provision(root, entry, sides, args.accept_eula)
        if not args.download_only:
            for side in sides:
                decompile(root, entry['id'], side, metadata, java_for(root, '26.3', args.java), args.timeout)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
