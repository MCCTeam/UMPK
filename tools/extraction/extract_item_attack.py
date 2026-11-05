"""Fold native item attack component measurements into canonical datasets."""

import argparse
from collections import defaultdict
import hashlib
import json
import os
from pathlib import Path
import zipfile


def canonical_items(native: dict, names: set[str]) -> dict:
    if set(native) != names:
        raise ValueError('Native attack defaults must cover every registered item exactly once')
    result = {}
    for name, value in sorted(native.items()):
        if not isinstance(value, dict) or set(value) - {'piercing_weapon', 'minimum_attack_charge'}:
            raise ValueError(f'Unsupported measured attack fields for {name}')
        result[name] = {'piercing_weapon': value.get('piercing_weapon'), 'minimum_attack_charge': value.get('minimum_attack_charge')}
    return result


def fold_aliases(rows: list[dict]) -> dict:
    if not rows or any(row != rows[0] for row in rows[1:]):
        raise ValueError('Native item attack defaults differ across protocol aliases')
    return rows[0]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--measurements', type=Path, required=True)
    parser.add_argument('--data', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--official-root', type=Path, default=Path(os.environ.get('MINECRAFT_OFFICIAL_ROOT', Path.home() / 'Projects/MinecraftOfficial')))
    args = parser.parse_args()
    catalog = json.loads((args.data / 'versions.json').read_text())['versions']
    protocols = {row['protocol'] for row in catalog}
    measured = json.loads((args.measurements / 'results.json').read_text())
    groups = defaultdict(list)
    source = Path(__file__).parent / 'shape-extractor/PiercingComponentDump.java'
    source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
    original_tools = json.loads((args.measurements / 'tools.json').read_text())
    original_hashes = [digest for name, digest in original_tools.items() if Path(name).name == source.name]
    if original_hashes != [source_hash]:
        raise ValueError('The measured native helper differs from the maintained source')
    for measurement in measured:
        version = measurement['version']
        client = args.official_root / 'downloads' / version / 'client.jar'
        if hashlib.sha256(client.read_bytes()).hexdigest() != measurement['client_sha256']:
            raise ValueError(f'The measured client artifact changed for {version}')
        with zipfile.ZipFile(client) as jar:
            protocol = json.loads(jar.read('version.json'))['protocol_version']
        if protocol not in protocols:
            raise ValueError(f'No canonical protocol dataset exists for {version}')
        raw = (args.measurements / version / 'native.json').read_bytes()
        if measurement['status'] != 'complete' or hashlib.sha256(raw).hexdigest() != measurement['measurement_sha256']:
            raise ValueError(f'Incomplete or changed native report for {version}')
        if not measurement['origins'] or not measurement['preservation']:
            raise ValueError(f'Native client origin evidence is missing for {version}')
        names = {item['name'] for item in json.loads((args.data / str(protocol) / 'items.json').read_text())['entries']}
        items = canonical_items(json.loads(raw)['default_components'], names)
        groups[protocol].append((measurement, items))
    for protocol, rows in sorted(groups.items()):
        expected = {row['name'] for row in catalog if row['protocol'] == protocol}
        versions = [row['version'] for row, _ in rows]
        if not expected.issubset(versions) or len(versions) != len(set(versions)):
            raise ValueError(f'Missing or duplicate native alias reports for protocol {protocol}')
        items = fold_aliases([items for _, items in rows])
        provenance = {
            'kind': 'extracted',
            'tool': 'tools/extraction/shape-extractor/PiercingComponentDump.java',
            'tool_sha256': source_hash,
            'measurements': [{key: row[key] for key in ['version', 'client_sha256', 'server_sha256', 'measurement_sha256']} for row, _ in rows],
            'note': 'Native default item components read from every registered item. Missing native values are explicit nulls. PiercingWeapon.CODEC uses RegistryOps<JsonOps>; all protocol aliases must match.',
        }
        output = args.out / str(protocol) / 'item-attack.json'
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps({'_provenance': provenance, 'schema': 1, 'items': items}, indent=2) + '\n')
        print(f'{protocol}: {len(items)} measured items')


if __name__ == '__main__':
    main()
