"""Merge source-verified original RemotePlayer pose geometry for 1.14 through 1.14.3."""

import argparse
import hashlib
import json
from pathlib import Path
import re
import zipfile

from extract_entity_geometry import fixture_bytes
from extract_player_geometry import ENTITY, LIVING, PLAYER, fixture, player_profile, sha
from resolve_legacy_mining import methods, params
from verify_entity_box_transitions import verify

RELEASES = {'1.14': 477, '1.14.1': 480, '1.14.2': 485, '1.14.3': 490}
NATIVE_POSES = ['STANDING', 'FALL_FLYING', 'SLEEPING', 'SWIMMING', 'SPIN_ATTACK', 'SNEAKING', 'DYING']


def legacy_player_profile(raw: dict, binding: dict) -> dict:
    contract = binding['geometry_contract']
    entity, living, player = (contract[key] for key in ('entity', 'living', 'player'))
    owners = {'setPose': entity, 'getDimensions': player, 'refreshDimensions': entity, 'setPos': entity, 'getBoundingBox': entity, 'onSyncedDataUpdated': living}
    hierarchy = [row['class'] for row in binding['hierarchy']]
    if RELEASES.get(binding.get('version')) != binding.get('protocol') or hierarchy[-3:] != [player, living, entity] or hierarchy[0] != binding['actor'] or raw.get('actor') != 'LegacyRemotePlayerAdapter':
        raise ValueError('Legacy player identity is outside the audited cohort')
    if raw.get('pose_type') != contract['pose'] or raw.get('pose_accessor_field') != contract['pose_accessor_field'] or type(raw.get('pose_index')) is not int or raw['pose_index'] != 6 or raw.get('poses') != NATIVE_POSES:
        raise ValueError('Legacy player pose accessor or enum identity differs')
    if raw.get('method_owners') != owners or set(raw.get('method_names', {})) != set(owners) or any(not isinstance(name, str) or not name for name in raw['method_names'].values()):
        raise ValueError('Legacy player callbacks have unaudited native implementations')
    for role, key in (('setPos', 'position_setter'), ('getBoundingBox', 'box_getter'), ('refreshDimensions', 'dimension_refresh')):
        if raw['method_names'][role] != binding[key]['name']:
            raise ValueError('Legacy player callback differs from its source binding: ' + role)
    origins = raw.get('origins', {})
    if set(origins) != {Path(name).stem for name in contract['sources_sha256']} or not origins or len(set(origins.values())) != 1 or not all(isinstance(value, str) and value for value in origins.values()):
        raise ValueError('Legacy player native class-origin coverage differs')
    aliases = {entity: ENTITY, living: LIVING, player: PLAYER}
    normalized = {'class': 'EntityPlayerAdapter', 'constructor_blocks_building': raw.get('constructor_blocks_building'), 'blocks_building': raw.get('blocks_building'),
                  'dimensions': raw['dimensions'], 'method_owners': {key: aliases[value] for key, value in owners.items()},
                  'geometry': {'pose_index': raw['pose_index'], 'selectors': [], 'default': {'blocks_building': raw.get('blocks_building'), 'poses': raw['snapshots']}}}
    phases = {key: raw[key] for key in ('poses', 'positions', 'rows')}
    phases['method_owners'] = {key: aliases[value] for key, value in owners.items() if key != 'getBoundingBox'}
    return player_profile(normalized, phases)


def verify_contract_sources(raw: dict, binding: dict, sources: dict[str, str]) -> None:
    contract = binding['geometry_contract']
    if set(sources) != set(contract['sources_sha256']):
        raise ValueError('Legacy player source coverage is incomplete')
    for name, text in sources.items():
        if hashlib.sha256(text.encode()).hexdigest() != contract['sources_sha256'][name]:
            raise ValueError('Legacy player source hash differs: ' + name)
    texts = {Path(name).stem: re.sub(r'/\*.*?\*/', '', text, flags=re.S) for name, text in sources.items()}
    hierarchy = [row['class'] for row in binding['hierarchy']]
    for child, parent in zip(hierarchy, hierarchy[1:]):
        if re.search(r'public (?:abstract )?class ' + child + r' extends ' + parent + r'\b', texts[child]) is None:
            raise ValueError('Legacy player hierarchy differs from native source')
    entity, living, player, accessor = (contract[key] for key in ('entity', 'living', 'player', 'accessor'))
    pose_field = contract['pose_accessor_field']
    if re.search(r'static final ' + accessor + '<' + contract['pose'] + '> ' + pose_field + r' = ' + contract['data'] + r'\.\w+\(' + entity + r'\.class,', texts[entity]) is None:
        raise ValueError('Legacy player pose field is not the original typed accessor')
    building = contract['blocks_building_field']
    constructors = list(methods(texts[living], r'protected\s+(' + re.escape(living) + r')\(([^)]*)\)\s*\{'))
    if len(constructors) != 1 or re.search(r'public boolean ' + building + ';', texts[entity]) is None or re.search(r'this\.' + building + r' = true;', constructors[0][1]) is None:
        raise ValueError('Legacy player building flag is not initialized by its living base')
    predicate = texts[contract['building_predicate_owner']]
    if re.search(r'\.filter\(\w+ -> !\w+\.\w+ && \w+\.' + building + r' &&', predicate) is None or '.noneMatch(' not in predicate or '.' + raw['method_names']['getBoundingBox'] + '()' not in predicate:
        raise ValueError('Legacy player building flag does not participate in the native placement predicate')
    arguments = {'setPose': [contract['pose']], 'getDimensions': [contract['pose']], 'refreshDimensions': [], 'setPos': ['double', 'double', 'double'], 'getBoundingBox': [], 'onSyncedDataUpdated': [accessor + '<?>']}
    bodies = {}
    for role, name in raw['method_names'].items():
        found = []
        for owner in hierarchy:
            declarations = [(m, body) for m, body in methods(texts[owner], r'(?:public|protected)\s+\S+\s+(' + re.escape(name) + r')\(([^)]*)\)\s*\{') if params(m[2]) == arguments[role]]
            if len(declarations) > 1:
                raise ValueError('Legacy player callback source is ambiguous: ' + role)
            if declarations:
                found.append((owner, *declarations[0]))
        if not found or found[0][0] != raw['method_owners'][role]:
            raise ValueError('Legacy player callback owner differs from native source: ' + role)
        bodies[role] = found[0][2]
        if role == 'onSyncedDataUpdated':
            name_argument = found[0][1][2].split()[-1]
            if not re.sub(r'\s+', '', found[0][2]).startswith('super.' + name + '(' + name_argument + ');'):
                raise ValueError('Legacy player living callback does not delegate first to Entity')
            base = next((body for owner, _, body in found if owner == entity), '')
            if re.fullmatch(r'\s*if \(' + pose_field + r'\.equals\(\w+\)\) \{\s*this\.' + raw['method_names']['refreshDimensions'] + r'\(\);\s*\}\s*', base) is None:
                raise ValueError('Legacy player pose callback does not refresh its dimensions')
    if re.fullmatch(r'\s*return \w+\.getOrDefault\(\w+, \w+\);\s*', bodies['getDimensions']) is None:
        raise ValueError('Legacy player dimensions depend on an unaudited selector')
    if re.fullmatch(r'\s*this\.\w+\.\w+\(' + pose_field + r', \w+\);\s*', bodies['setPose']) is None:
        raise ValueError('Legacy player pose setter does not update its typed metadata accessor')


def read_legacy_players(measurements: Path, official: Path, catalog: list[dict]) -> list[dict]:
    expected = {row['name']: row['protocol'] for row in catalog if row['protocol'] in RELEASES.values()}
    if expected != RELEASES:
        raise ValueError('Legacy player version catalog differs from the audited cohort')
    receipts = json.loads((measurements / 'results.json').read_text(encoding='utf-8'))
    if len(receipts) != len(RELEASES) or {row['version'] for row in receipts} != set(RELEASES):
        raise ValueError('Legacy player receipts must cover every audited release exactly once')
    tools = json.loads((measurements / 'tools.json').read_text(encoding='utf-8'))
    probe = Path(__file__).parent / 'shape-extractor/LegacyPlayerGeometryDump.java'
    if tools['probe_source_sha256'] != sha(probe) or sha(measurements / probe.name) != sha(probe) or sha(measurements / 'driver.py') != tools['driver_sha256']:
        raise ValueError('Legacy player probe or driver source changed')
    for path, expected_hash in tools['helper_classes'].items():
        if sha(Path(path)) != expected_hash:
            raise ValueError('Legacy player helper class changed: ' + path)
    records = []
    for receipt in receipts:
        version = receipt['version']
        target = measurements / version
        first, second = target / 'bindings.json', target / 'box-transitions.json'
        if receipt.get('status') != 'complete' or receipt.get('protocol') != RELEASES[version] or receipt['bindings_sha256'] != sha(first) or receipt['measurement_sha256'] != sha(second):
            raise ValueError('Legacy player measurement receipt differs: ' + version)
        binding, raw = json.loads(first.read_text()), json.loads(second.read_text())
        if binding['version'] != version or receipt['verification'] != verify(raw) or receipt['sources_sha256'] != binding['geometry_contract']['sources_sha256'] or receipt['origins'] != raw['origins']:
            raise ValueError('Legacy player verification or binding differs: ' + version)
        downloads = official / 'downloads' / version
        metadata = json.loads((downloads / 'version.json').read_text())
        for side in ('client', 'server'):
            artifact = downloads / (side + '.jar')
            with artifact.open('rb') as stream:
                digest = hashlib.file_digest(stream, 'sha1').hexdigest()
            if digest != metadata['downloads'][side]['sha1'] or receipt[side + '_sha256'] != sha(artifact):
                raise ValueError('Legacy player official artifact differs: ' + version + '/' + side)
        origin = Path(next(iter(raw['origins'].values())))
        if sha(origin) != receipt['unsigned_client_sha256']:
            raise ValueError('Legacy player class-origin artifact hash differs: ' + version)
        with zipfile.ZipFile(downloads / 'client.jar') as original, zipfile.ZipFile(origin) as unsigned:
            names = [name for name in original.namelist() if name.endswith('.class')]
            if len(names) != receipt['unchanged_original_classes'] or any(original.read(name) != unsigned.read(name) for name in names):
                raise ValueError('Legacy player probe changed an original class: ' + version)
        for path, expected_hash in receipt['adapters_sha256'].items():
            if sha(Path(path)) != expected_hash:
                raise ValueError('Legacy player adapter changed: ' + path)
        tree = official / (version + '-client-decompiled')
        sources = {name: (tree / name).read_text(encoding='utf-8') for name in binding['geometry_contract']['sources_sha256']}
        verify_contract_sources(raw, binding, sources)
        profile = legacy_player_profile(raw, binding)
        records.append({'version': version, 'protocol': RELEASES[version], 'transitions': {key: raw[key] for key in ('poses', 'positions', 'rows')}, 'profile': profile,
                        'proof': {'measurement_sha256': sha(second), 'bindings_sha256': sha(first), 'native_witnesses': len(raw['rows']), 'client_sha256': receipt['client_sha256'], 'server_sha256': receipt['server_sha256'], 'sources_sha256': receipt['sources_sha256'],
                                  'probe_source_sha256': tools['probe_source_sha256'], 'scope': raw['scope']}})
    return records


def extract(measurements: Path, official: Path, data: Path, output: Path | None, *, check: bool) -> None:
    catalog = json.loads((data / 'versions.json').read_text())['versions']
    records = read_legacy_players(measurements, official, catalog)
    outputs = []
    for record in records:
        path = data / str(record['protocol']) / 'entity-geometry.json'
        document = json.loads(path.read_text())
        document['entities']['minecraft:player'] = record['profile']
        document['_provenance'].setdefault('entity_roles', {})['minecraft:player'] = record['proof']
        outputs.append((path, (json.dumps(document, indent=2) + '\n').encode()))
    if output is not None:
        outputs.append((output, fixture_bytes(fixture(records), compressed=output.suffix == '.gz')))
    for path, contents in outputs:
        if check:
            if not path.is_file() or path.read_bytes() != contents:
                raise ValueError('Legacy player geometry output differs: ' + str(path))
        else:
            path.write_bytes(contents)
    print(f'Verified {len(records)} legacy releases and {sum(len(row["transitions"]["rows"]) for row in records)} native pose transitions')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--measurements', type=Path, required=True)
    parser.add_argument('--official', type=Path, required=True)
    parser.add_argument('--data', type=Path, default=Path('data/java'))
    parser.add_argument('--fixture', type=Path)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    extract(args.measurements, args.official, args.data, args.fixture, check=args.check)


if __name__ == '__main__':
    main()
