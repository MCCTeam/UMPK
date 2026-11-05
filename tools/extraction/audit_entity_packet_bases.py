"""Audit source-backed entity packet-base ownership before visual interpolation is installed."""

import argparse
import hashlib
import json
from pathlib import Path
import re

from audit_entity_lifecycle import closing, masked, methods, unique_hit
from measure_block_interactions import interaction_catalog

NAME = r'[A-Za-z_$][\w$]*'


def compact(source: str) -> str:
    return re.sub(r'\s+', '', source)


def evidence(method: dict) -> dict:
    return {key: method[key] for key in ('name', 'parameters', 'line', 'source')}


def static_methods(source: str) -> list[dict]:
    code = masked(source)
    result = []
    for match in re.finditer(r'(?:public|private|protected)\s+static\s+void\s+(' + NAME + r')\s*\(([^()]*)\)\s*\{', code):
        end = closing(code, match.end() - 1, '{', '}')
        result.append({'name': match[1], 'parameters': [part.strip().split() for part in match[2].split(',')], 'line': source[:match.start()].count('\n') + 1, 'body': code[match.end():end], 'source': source[match.start():end + 1]})
    return result


def resolve_fixed_setter(root: Path, teleport: dict, entity_type: str, fields: list[str]) -> dict:
    code = masked(teleport['source'])
    declaration = re.search(r'\b' + re.escape(entity_type) + r'\s+(' + NAME + r')\s*=', code)
    if declaration is None:
        raise ValueError('The teleport entity receiver is missing')
    receiver = declaration[1]
    hits = []
    entity_path = root/(entity_type+'.java')
    if entity_path.exists():
        for method in methods(entity_path.read_text()):
            if len(method['parameters']) == 3 and all(p[0] == 'double' for p in method['parameters']) and all('this.'+field+'=' in compact(method['body']) for field in fields) and receiver+'.'+method['name']+'(' in compact(code):
                hits.append({'file': entity_path.name, 'method': evidence(method)})
    for owner, name in re.findall(r'(' + NAME + r')\.(' + NAME + r')\(' + re.escape(receiver) + ',', compact(code)):
        path = root/(owner+'.java')
        if not path.exists():
            continue
        for method in static_methods(path.read_text()):
            parameters = method['parameters']
            if method['name'] == name and len(parameters) == 4 and parameters[0][0] == entity_type and all(p[0] == 'double' for p in parameters[1:]) and all(parameters[0][1]+'.'+field+'=' in compact(method['body']) for field in fields):
                hits.append({'file': path.name, 'method': evidence(method)})
    if len(hits) != 1:
        raise ValueError(f'Expected one bound native packet-cache setter, found {len(hits)}')
    return hits[0]


def inspect_listener(source: str, *, mapped: bool) -> dict:
    rows = methods(source)
    candidates = []
    for method in rows:
        hits = re.findall(r'(' + NAME + r')\.(' + NAME + r')=\1\.\2\+(' + NAME + r')\.(' + NAME + r')\(\)', compact(method['body']))
        if mapped and method['name'] == 'handleMoveEntity' or not mapped and len(hits) == 3:
            candidates.append((method, hits))
    if len(candidates) != 1:
        raise ValueError(f'Expected one native relative-movement handler, found {len(candidates)}')
    relative, fields = candidates[0]
    body = compact(relative['body'])
    if fields:
        if len(fields) != 3 or len({hit[0] for hit in fields}) != 1 or len({hit[1] for hit in fields}) != 3 or len({hit[2] for hit in fields}) != 1:
            raise ValueError('Native coordinate fields do not have distinct shared ownership')
        cache = 'fixed_fields'
    elif 'getPacketCoordinates()' in body and '.setPacketCoordinates(' in body:
        cache = 'packet_vector'
    elif '.getPositionCodec()' in body and '.setBase(' in body:
        cache = 'path_codec' if '.getPositionDelta().decode(' in body else 'delta_codec'
    else:
        raise ValueError('No independent native entity packet base was found')
    if cache != 'fixed_fields' and '.hasPosition()' not in body:
        raise ValueError('The native vector cache has no position-only branch')

    teleports = [method for method in rows if method['name'] == 'handleTeleportEntity'] if mapped else [method for method in rows if len(method['parameters']) == 1 and '0.03125' in method['body'] and '0.015625' in method['body']]
    if len(teleports) != 1:
        raise ValueError(f'Expected one native entity teleport handler, found {len(teleports)}')
    teleport = teleports[0]
    teleport_body = compact(teleport['body'])
    resets = '.setPacketCoordinates(' in teleport_body or '.syncPacketPositionCodec(' in teleport_body
    if fields:
        resets = resets or all(re.search(r'\.' + re.escape(hit[1]) + r'=', teleport_body) for hit in fields)
        if not resets and not mapped:
            # Obfuscated fixed-point entities expose one three-double cache setter. Resolve it in the caller from the actual entity source.
            resets = None
    modern_helper = None
    if not resets and mapped:
        if 'setValuesFromPositionPacket(' not in teleport_body:
            raise ValueError('Teleport has neither a cache replacement nor the modern relative-value helper')
        code = masked(source)
        found = list(re.finditer(r'private\s+static\s+boolean\s+setValuesFromPositionPacket\([^)]*\)\s*\{', code))
        if len(found) != 1:
            raise ValueError('The modern teleport helper is missing or ambiguous')
        match = found[0]
        end = closing(code, match.end() - 1, '{', '}')
        helper_body = code[match.end():end]
        if any(token in helper_body for token in ('getPositionCodec', 'setPacketCoordinates', 'setBase')):
            raise ValueError('Modern teleport unexpectedly changes the relative-packet base')
        modern_helper = source[match.start():end + 1]
    sync = [method for method in rows if method['name'] == 'handleEntityPositionSync']
    if len(sync) > 1:
        raise ValueError('Position-sync handler is ambiguous')
    sync_resets = bool(sync) and '.getPositionCodec().setBase(' in compact(sync[0]['body'])
    if modern_helper and not sync_resets:
        raise ValueError('Modern absolute position-sync does not establish its packet base')
    return {'cache': cache, 'fields': [hit[1] for hit in fields], 'entity_variable': fields[0][0] if fields else None,
            'rotation_uses_packet_base': cache == 'fixed_fields', 'teleport_resets_packet_base': resets,
            'sync_resets_packet_base': sync_resets, 'relative': evidence(relative), 'teleport': evidence(teleport),
            'sync': evidence(sync[0]) if sync else None, 'modern_teleport_helper': modern_helper}


def main() -> None:
    repo = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--official', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    versions = json.loads((repo/'data/java/versions.json').read_text())['versions']
    catalog = {row['name']: row for row in versions}
    catalog.update({row['name']: row for row in interaction_catalog(versions, args.official)})
    output = []
    for name, version in catalog.items():
        root = args.official/(name+'-client-decompiled')
        row = {'version': name, 'protocol': version['protocol'], 'status': 'failed'}
        try:
            mapped = version['protocol'] >= 498
            listener = root/'net/minecraft/client/multiplayer/ClientPacketListener.java' if mapped else unique_hit(root, '"disconnect.lost"')
            source = listener.read_text()
            result = inspect_listener(source, mapped=mapped)
            hashes = {str(listener.relative_to(root)): hashlib.sha256(listener.read_bytes()).hexdigest()}
            if result['fields']:
                relative = next(m for m in methods(source) if m['line'] == result['relative']['line'])
                declaration = re.search(r'\b(' + NAME + r')\s+' + re.escape(result['entity_variable']) + r'\s*=', relative['body'])
                if declaration is None:
                    raise ValueError('Cannot resolve native fixed-coordinate entity class')
                entity = root/(declaration[1]+'.java') if not mapped else root/'net/minecraft/world/entity/Entity.java'
                entity_source = entity.read_text()
                field_types = []
                for field in result['fields']:
                    matches = re.findall(r'public\s+(int|long)\s+'+re.escape(field)+r'\s*;', masked(entity_source))
                    if len(matches) != 1:
                        raise ValueError('Cannot resolve the native packet-field width')
                    field_types.append(matches[0])
                if len(set(field_types)) != 1:
                    raise ValueError('Native packet field widths disagree')
                result['field_type'] = field_types[0]
                if result['teleport_resets_packet_base'] is None:
                    setter = resolve_fixed_setter(root, result['teleport'], declaration[1], result['fields'])
                    result['teleport_resets_packet_base'] = True
                    result['cache_setter'] = setter
                    helper = root/setter['file']
                    hashes[str(helper.relative_to(root))] = hashlib.sha256(helper.read_bytes()).hexdigest()
                hashes[str(entity.relative_to(root))] = hashlib.sha256(entity.read_bytes()).hexdigest()
            downloads = args.official/'downloads'/name
            jar = downloads/'client.jar'
            metadata = json.loads((downloads/'version.json').read_text())
            sha1, sha256 = hashlib.sha1(), hashlib.sha256()
            with jar.open('rb') as stream:
                while chunk := stream.read(4*1024*1024):
                    sha1.update(chunk);sha256.update(chunk)
            if sha1.hexdigest() != metadata['downloads']['client']['sha1']:
                raise ValueError('Official client jar hash differs')
            row.update(status='complete', audit=result, client_sha256=sha256.hexdigest(), source_sha256=hashes)
        except (OSError, ValueError, StopIteration) as error:
            row['error'] = str(error)
        output.append(row)
        print(name,row['status'],row.get('error',''),flush=True)
    args.out.write_text(json.dumps({'kind':'source audit; not native packet-listener execution','tool_sha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'releases':output},indent=2)+'\n')
    if any(row['status']!='complete' for row in output):
        raise SystemExit(1)


if __name__ == '__main__':
    main()
