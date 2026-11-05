"""Resolve legacy vanilla mining symbols from each release's own decompiled source."""
import argparse
import hashlib
import json
import pathlib
import re

def methods(source: str, pattern: str):
    for m in re.finditer(pattern, source):
        start = m.end()
        depth = 1
        end = start
        while depth and end < len(source):
            if source[end] == '{':
                depth += 1
            elif source[end] == '}':
                depth -= 1
            end += 1
        yield (m, source[start:end - 1])

def one(rows: list, label: str):
    if len(rows) != 1:
        raise ValueError(label + ': ' + str(rows))
    return rows[0]

def params(text: str) -> list[str]:
    return [re.sub('@\\w+\\s+|\\bfinal\\s+', '', p).strip().split()[0] for p in text.split(',') if p.strip()]

def is_progress(source: str) -> bool:
    return bool(re.search('/\\s*100\\.0F', source)) and bool(re.search('/\\s*30\\.0F', source)) or bool(re.search('\\?\\s*30\\s*:\\s*100', source))

def resolve(root: pathlib.Path, version: str) -> dict:
    tree = root / (version + '-decompiled')
    sources = {p.stem: re.sub('/\\*.*?\\*/', '', p.read_text(errors='strict'), flags=re.S) for p in tree.glob('*.java')}
    block, source = one([(name, s) for name, s in sources.items() if is_progress(s)], 'block progress')
    progress, body = one([(m, b) for m, b in methods(source, 'public\\s+float\\s+(\\w+)\\(([^)]*)\\)\\s*\\{') if is_progress(b)], 'progress method')
    progress_params = params(progress.group(2))
    player = progress_params[0] if len(progress_params) == 3 else progress_params[1]
    statelike = block if len(progress_params) == 3 else progress_params[0]
    state = one([params(m.group(2))[0] for m, b in methods(source, 'public\\s+static\\s+int\\s+(\\w+)\\(([^)]*)\\)\\s*\\{') if len(params(m.group(2))) == 1 and params(m.group(2))[0] not in (block, 'int', 'long', 'float', 'String')], 'state type')
    item = one([params(m.group(1))[0] for m, b in methods(source, 'public\\s+static\\s+' + re.escape(block) + '\\s+\\w+\\(([^)]*)\\)\\s*\\{') if 'instanceof' in b and len(params(m.group(1))) == 1], 'item type')
    stack = one([params(m.group(2))[0] for m, b in methods(sources[item], 'public\\s+float\\s+(\\w+)\\(([^)]*)\\)\\s*\\{') if len(params(m.group(2))) == 2 and params(m.group(2))[1] == statelike], 'stack type')
    speed = one([m.group(1) for m, b in methods(sources[stack], 'public\\s+float\\s+(\\w+)\\(([^)]*)\\)\\s*\\{') if params(m.group(2)) == [statelike]], 'stack speed')
    correct = one([m.group(1) for m, b in methods(sources[stack], 'public\\s+boolean\\s+(\\w+)\\(([^)]*)\\)\\s*\\{') if params(m.group(2)) == [statelike] and re.fullmatch('\\s*return\\s+this\\.\\w+(?:\\(\\))?\\.\\w+\\(\\w+\\);\\s*', b)], 'stack harvest')
    hardness, hardbody = one([(m, b) for m, b in methods(source, 'public\\s+float\\s+(\\w+)\\(([^)]*)\\)\\s*\\{') if params(m.group(2)) == ([statelike] + progress_params[-2:] if len(progress_params) == 4 else progress_params[-2:])], 'hardness')
    material_candidates = []
    for name, s in sources.items():
        for m, b in methods(s, 'public\\s+boolean\\s+\\w+\\(([^)]*)\\)\\s*\\{'):
            if params(m.group(1)) != [statelike]:
                continue
            match = re.search('if\\s*\\(\\s*\\w+\\.(\\w+)\\(\\)\\.(\\w+)\\(\\)\\s*\\)\\s*\\{\\s*return\\s+true;', b)
            if match is None:
                match = re.search('return\\s+\\w+\\.(\\w+)\\(\\)\\.(\\w+)\\(\\)\\s*\\|\\|', b)
            if match:
                material_candidates.append((name, match.group(1), match.group(2)))
    inventory, material, always = one(material_candidates, 'material harvest')
    bootstrap_candidates = []
    for name, s in sources.items():
        if 'Accessed Blocks before Bootstrap' not in s:
            continue
        match = re.search('if\\s*\\(\\s*!(\\w+)\\.\\w+\\(\\)\\s*\\)', s)
        if match:
            bootstrap_candidates.append(match.group(1))
    if not bootstrap_candidates:
        bootstrap_candidates = [name for name, source_text in sources.items() if 'Unable to load registries' in source_text]
    bootstrap = one(bootstrap_candidates, 'bootstrap class')
    boot = one([m.group(1) for m, b in methods(sources[bootstrap], 'public\\s+static\\s+void\\s+(\\w+)\\(([^)]*)\\)\\s*\\{') if re.search('\\b' + re.escape(block) + '\\.\\w+\\(\\)', b) or 'Unable to load registries' in b], 'bootstrap method')
    registry_bindings = {}
    for role, typename in (('block', block), ('item', item)):
        entries = []
        for m, b in methods(sources[typename], 'public\\s+static\\s+int\\s+(\\w+)\\(([^)]*)\\)\\s*\\{'):
            if params(m.group(2)) != [typename]:
                continue
            var = m.group(2).strip().split()[-1]
            match = re.search('(?:(\\w+)\\.)?(\\w+)\\.(\\w+)\\(\\s*' + re.escape(var) + '\\s*\\)', b)
            if match:
                entries.append((match.group(1) or typename, match.group(2), match.group(3)))
        if not entries:
            for owner_name, owner_source in sources.items():
                if owner_name != typename and (not ('"item"' in owner_source and '"block"' in owner_source and re.search('\\b(?:class|interface)\\s+' + re.escape(owner_name) + '<T>', owner_source))):
                    continue
                for match in re.finditer('(?:(?:public|static|final)\\s+)*\\w+<[^;=\\n]*\\b' + re.escape(typename) + '>\\s+(\\w+)\\s*=', owner_source):
                    entries.append((owner_name, match.group(1), None))
        registry_bindings[role] = one(entries, role + ' registry binding')
    row = {'version': version, 'block': block, 'state': state, 'item': item, 'stack': stack, 'statelike': statelike, 'player': player, 'inventory': inventory, 'bootstrap': bootstrap, 'boot': boot, 'hardness': hardness.group(1), 'hardness_args': params(hardness.group(2)), 'material': material, 'always_destroyable': always, 'speed': speed, 'correct': correct, 'registries': registry_bindings, 'sources': {n: hashlib.sha256((tree / (n + '.java')).read_bytes()).hexdigest() for n in (block, state, item, stack, inventory, bootstrap)}}
    if version.startswith(('1.13', '1.14')):
        tree = root / (version + '-decompiled')
        matches = []
        for p in tree.glob('*.java'):
            s = re.sub('/\\*.*?\\*/', '', p.read_text(), flags=re.S)
            if '"wool"' not in s or '"leaves"' not in s:
                continue
            m = re.search('private static (\\w+)<' + row['block'] + '>\\s+\\w+\\s*=', s)
            if not m:
                continue
            tag = re.search('public static final (\\w+)<' + row['block'] + '>', s)
            reset = re.search('public static void (\\w+)\\(' + m[1] + '<' + row['block'] + '>', s)
            if not tag or not reset:
                raise ValueError('incomplete tag binding')
            matches.append({'owner': p.stem, 'collection': m[1], 'tag': tag[1], 'reset': reset[1]})
        if len(matches) != 1:
            raise ValueError(matches)
        row['block_tags'] = matches[0]
        for name in [matches[0]['owner'], matches[0]['collection'], matches[0]['tag']]:
            row['sources'][name] = hashlib.sha256((tree / (name + '.java')).read_bytes()).hexdigest()
    return row

def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('version')
    parser.add_argument('--official', type=pathlib.Path, required=True)
    parser.add_argument('--out', type=pathlib.Path, required=True)
    args = parser.parse_args()
    args.out.write_text(json.dumps(resolve(args.official, args.version), indent=2) + '\n', encoding='utf-8')
if __name__ == '__main__':
    main()
