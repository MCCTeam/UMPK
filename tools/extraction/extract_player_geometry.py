"""Extract player pose geometry from original client snapshots and callback sweeps."""

import argparse
from collections.abc import Iterable
import hashlib
import json
from pathlib import Path
from urllib.parse import unquote, urlsplit
import zipfile

from extract_entity_geometry import POSES, fixture_bytes, living_scale_attribute
from measure_block_interactions import class_bindings, verify_client_origins
from verify_entity_box_transitions import centered, size, verify

ENTITY = 'net.minecraft.world.entity.Entity'
LIVING = 'net.minecraft.world.entity.LivingEntity'
PLAYER = 'net.minecraft.world.entity.player.Player'


def player_profile(native: dict, transitions: dict, *, scale_attribute: str = '') -> dict:
    if scale_attribute not in ('', 'minecraft:scale'):
        raise ValueError('Player scale attribute is unsupported')
    if native.get('class') != 'EntityPlayerAdapter' or native.get('constructor_blocks_building') is not True or native.get('blocks_building') is not True:
        raise ValueError('Native player constructor or building contract differs')
    geometry = native.get('geometry', {})
    owners = native.get('method_owners', {})
    if geometry.get('pose_index') != 6 or geometry.get('selectors') != [] or owners.get('getBoundingBox') != ENTITY or owners.get('getDimensions') not in (PLAYER, LIVING):
        raise ValueError('Native player pose, selector or geometry owner differs')
    phases = transitions.get('method_owners', {})
    if set(phases) != {'setPose', 'refreshDimensions', 'setPos', 'getDimensions', 'onSyncedDataUpdated'} or any(owner not in (ENTITY, LIVING, PLAYER) for owner in phases.values()) or phases['getDimensions'] != owners['getDimensions']:
        raise ValueError('Player callbacks do not use the measured native implementations')
    poses = transitions['poses']
    if any(pose not in POSES for pose in poses) or 'STANDING' not in poses or len({POSES[pose] for pose in poses}) != len(poses):
        raise ValueError('Native player pose is unknown')
    verified = verify(transitions)
    dimensions = native['dimensions']
    observed = geometry.get('default', {})
    snapshots = observed.get('poses', [])
    if observed.get('blocks_building') is not True or set(dimensions) != set(poses) or len(snapshots) != len(poses) or {row[0] for row in snapshots} != set(poses):
        raise ValueError('Native player snapshot pose coverage is incomplete')
    for pose, width, height, fixed, box in snapshots:
        if size([width, height, fixed]) != size(dimensions[pose]) or box != centered([1.5, 64.0, 1.5], size(dimensions[pose])):
            raise ValueError('Native player snapshot size or bounds differ')
    for row in transitions['rows']:
        for field, pose in [('from_size', row['from']), ('to_size', row['to'])]:
            if size(row[field]) != size(dimensions[poses[pose]]):
                raise ValueError('Player snapshots disagree with native callback dimensions')
    variants = [[0, POSES[pose], *dimensions[pose], True] for pose in poses]
    return {'selector': [-1, 'none', 0, 0], 'pose_index': 6, 'refresh': verified['profile'],
            'scale_attribute': scale_attribute, 'variants': sorted(variants, key=lambda row: row[1])}


def group_players(records: list[dict]) -> dict[int, list[dict]]:
    groups, seen = {}, set()
    for record in records:
        version, protocol = record['version'], record['protocol']
        if not isinstance(version, str) or not version or type(protocol) is not int or protocol <= 0 or version in seen:
            raise ValueError('Duplicate or invalid player release')
        seen.add(version)
        profile = player_profile(record['native'], record['transitions'], scale_attribute=record['scale_attribute'])
        group = groups.setdefault(protocol, [])
        if group and profile != player_profile(group[0]['native'], group[0]['transitions'], scale_attribute=group[0]['scale_attribute']):
            raise ValueError('Player geometry differs between protocol aliases')
        group.append(record)
    return groups


def sha(path: Path) -> str:
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def receipts(path: Path) -> dict[str, dict]:
    result = {}
    for row in json.loads(path.read_text(encoding='utf-8')):
        if row.get('status') != 'complete' or row['version'] in result:
            raise ValueError(f'Incomplete or duplicate native receipt: {path}')
        result[row['version']] = row
    return result


def source_hashes(tree: Path, owners: Iterable[str]) -> dict[str, str]:
    result = {}
    for owner in sorted(set(owners)):
        path = tree / (owner.replace('.', '/') + '.java')
        result[str(path.relative_to(tree))] = sha(path)
    return result


def read_players(measurements: Path, transitions: Path, official: Path, catalog: list[dict]) -> list[dict]:
    snapshots = receipts(measurements / 'entity-placement-client-results.json')
    phases = receipts(transitions / 'entity-box-transition-results.json')
    selected = [row for row in catalog if row['name'] in phases]
    if not selected or set(phases) != {row['name'] for row in selected} or set(phases) != set(snapshots):
        raise ValueError('Native player release coverage differs from snapshots or the version catalog')
    tools = json.loads((transitions / 'measurement-tools.json').read_text())
    extraction = Path(__file__).parent
    if tools['driver_sha256'] != sha(extraction / 'measure_entity_box_transitions.py') or tools['verifier_sha256'] != sha(extraction / 'verify_entity_box_transitions.py'):
        raise ValueError('Native player driver or verifier source changed')
    for name, expected in tools['source_sha256'].items():
        if sha(extraction / 'shape-extractor' / name) != expected:
            raise ValueError(f'Native player helper source changed: {name}')
    records = []
    for entry in selected:
        version, protocol = entry['name'], entry['protocol']
        snapshot, phase = snapshots[version], phases[version]
        first = measurements / version / 'entity-placement-client.json'
        second = transitions / version / 'entity-box-transitions.json'
        if phase['protocol'] != protocol or sha(first) != snapshot['measurement_sha256'] or sha(second) != phase['measurement_sha256']:
            raise ValueError(f'Native player measurement hash or protocol differs: {version}')
        native, sweep = json.loads(first.read_text())['minecraft:player'], json.loads(second.read_text())
        if verify(sweep) != phase['verification']:
            raise ValueError(f'Native player verification receipt differs: {version}')
        downloads = official / 'downloads' / version
        metadata = json.loads((downloads / 'version.json').read_text())
        for side in ('client', 'server'):
            path = downloads / (side + '.jar')
            with path.open('rb') as stream:
                sha1 = hashlib.file_digest(stream, 'sha1').hexdigest()
            if sha(path) != phase[side + '_sha256'] or phase[side + '_sha256'] != snapshot[side + '_sha256'] or sha1 != metadata['downloads'][side]['sha1']:
                raise ValueError(f'Official player artifact differs: {version}/{side}')
        with zipfile.ZipFile(downloads / 'client.jar') as archive:
            classes = {name: hashlib.sha256(archive.read(name)).hexdigest() for name in archive.namelist() if name.endswith('.class')}
        digest = hashlib.sha256(json.dumps(classes, sort_keys=True).encode()).hexdigest()
        if phase['preservation']['classes_sha256'] != digest or phase['preservation']['unchanged_class_files'] != len(classes) or phase['preservation'] != snapshot['preservation']:
            raise ValueError(f'Player probe changed original client classes: {version}')
        bindings = class_bindings(downloads / 'client_mappings.txt', {ENTITY, LIVING, PLAYER,
            'net.minecraft.world.entity.Pose', 'net.minecraft.world.entity.EntityType',
            'net.minecraft.world.entity.EntityDimensions', 'net.minecraft.world.phys.AABB',
            'net.minecraft.network.syncher.SynchedEntityData'})
        log = (transitions / version / 'client-classes.log').read_text()
        prefix = f'[class,load] {bindings[ENTITY]} source: '
        origins = [line.split(prefix, 1)[1] for line in log.splitlines() if prefix in line]
        if len(origins) != 1:
            raise ValueError(f'Native player class origin is missing or duplicated: {version}')
        artifact = Path(unquote(urlsplit(origins[0]).path)).resolve()
        if artifact.name != 'client-unsigned.jar' or not artifact.parent.name.startswith('native-libraries-') or artifact.parent.parent != (transitions / version).resolve():
            raise ValueError(f'Native player classes came from an unexpected artifact: {version}')
        if verify_client_origins(log, bindings, artifact) != phase['native_client_classes']:
            raise ValueError(f'Native player class-origin receipt differs: {version}')
        for name, expected in phase['adapter_source_sha256'].items():
            if sha(transitions / version / 'world-classes' / name) != expected:
                raise ValueError(f'Native player adapter changed: {version}/{name}')
        tree = official / (version + '-client-decompiled')
        sources = source_hashes(tree, [ENTITY, LIVING, PLAYER, *sweep['method_owners'].values()])
        living = tree / 'net/minecraft/world/entity/LivingEntity.java'
        scale = living_scale_attribute(living.read_text())
        profile = player_profile(native, sweep, scale_attribute=scale)
        records.append({'version': version, 'protocol': protocol, 'native': native, 'transitions': sweep,
                        'scale_attribute': scale, 'profile': profile,
                        'proof': {'measurement_sha256': sha(first), 'transition_sha256': sha(second),
                                  'native_witnesses': len(sweep['rows']), 'client_sha256': phase['client_sha256'],
                                  'server_sha256': phase['server_sha256'], 'sources_sha256': sources,
                                  'scope': 'Original Player dimensions and pose/position callbacks under controlled client-world inputs; default attribute scale, no entity/world ticks or rider positioning'}})
    group_players(records)
    return records


def fixture(records: list[dict]) -> dict:
    cases, releases, seen = [], [], {}
    for record in records:
        key = json.dumps(record['transitions'], sort_keys=True)
        if key not in seen:
            seen[key] = len(cases)
            cases.append(record['transitions'])
        releases.append({'version': record['version'], 'protocol': record['protocol'], 'case': seen[key], **record['proof']})
    return {'schema': 1, 'releases': releases, 'cases': cases}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--measurements', type=Path, required=True)
    parser.add_argument('--transitions', type=Path, required=True)
    parser.add_argument('--official', type=Path, required=True)
    parser.add_argument('--data', type=Path, default=Path('data/java'))
    parser.add_argument('--fixture', type=Path, required=True)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    catalog = json.loads((args.data / 'versions.json').read_text())['versions']
    records = read_players(args.measurements, args.transitions, args.official, catalog)
    groups = group_players(records)
    updates = []
    for protocol, aliases in groups.items():
        path = args.data / str(protocol) / 'entity-geometry.json'
        document = json.loads(path.read_text())
        representative = next((row for row in aliases if row['version'] == document['_provenance']['version']), None)
        if representative is None:
            raise ValueError(f'Canonical player representative has no measurement: {protocol}')
        document['entities']['minecraft:player'] = representative['profile']
        proof = dict(representative['proof'])
        if len(aliases) > 1:
            proof['aliases'] = [{'version': row['version'], **row['proof']} for row in aliases]
        document['_provenance'].setdefault('entity_roles', {})['minecraft:player'] = proof
        updates.append((path, (json.dumps(document, indent=2) + '\n').encode()))
    updates.append((args.fixture, fixture_bytes(fixture(records), compressed=args.fixture.suffix == '.gz')))
    for path, contents in updates:
        if args.check:
            if not path.exists() or path.read_bytes() != contents:
                raise ValueError(f'Player geometry output differs: {path}')
        else:
            path.write_bytes(contents)
    print(f'Verified {len(records)} releases, {len(groups)} protocols and {sum(len(row["transitions"]["rows"]) for row in records)} native player transitions')


if __name__ == '__main__':
    main()
