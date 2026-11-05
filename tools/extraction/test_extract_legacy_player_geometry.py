"""Check early pose-era player identities and complete native callback profiles."""

import copy
import json
import hashlib
from pathlib import Path
import unittest

from extract_legacy_player_geometry import legacy_player_profile, verify_contract_sources


class LegacyPlayerGeometryExtractionTests(unittest.TestCase):
    def setUp(self):
        fixture = json.loads((Path(__file__).parent / 'tests/fixtures/legacy-player-geometry-1.14.3.json').read_text(encoding='utf-8'))
        self.raw, self.binding = fixture['native'], fixture['binding']

    def test_original_remote_player_callbacks_preserve_each_native_pose(self):
        profile = legacy_player_profile(self.raw, self.binding)
        self.assertEqual(6, profile['pose_index'])
        self.assertEqual('retain_minimum_on_growth', profile['refresh'])
        variants = {row[1]: row for row in profile['variants']}
        self.assertEqual([0, 5, .6, 1.5, False, True], variants[5])
        self.assertEqual([0, 7, .2, .2, True, True], variants[7])
        self.assertEqual(7, len(variants))
        self.assertNotIn(6, variants)

    def test_unverified_native_identity_and_callback_changes_are_rejected(self):
        for defect in ('actor', 'protocol', 'pose-index', 'pose-accessor', 'pose-type', 'building', 'constructor', 'dimensions-owner', 'callback-owner', 'box-owner', 'origin', 'method-names', 'snapshot', 'missing-pose', 'duplicate-row'):
            with self.subTest(defect=defect):
                raw, binding = copy.deepcopy(self.raw), copy.deepcopy(self.binding)
                if defect == 'actor': raw['actor'] = 'UnknownAdapter'
                if defect == 'protocol': binding['protocol'] = 498
                if defect == 'pose-index': raw['pose_index'] = 7
                if defect == 'pose-accessor': raw['pose_accessor_field'] = 'Unknown'
                if defect == 'pose-type': raw['pose_type'] = 'Unknown'
                if defect == 'building': raw['blocks_building'] = False
                if defect == 'constructor': raw['constructor_blocks_building'] = False
                if defect == 'dimensions-owner': raw['method_owners']['getDimensions'] = raw['actor']
                if defect == 'callback-owner': raw['method_owners']['onSyncedDataUpdated'] = raw['actor']
                if defect == 'box-owner': raw['method_owners']['getBoundingBox'] = raw['actor']
                if defect == 'origin': raw['origins'].pop(binding['geometry_contract']['living'])
                if defect == 'method-names': raw['method_names'].pop('setPos')
                if defect == 'snapshot': raw['snapshots'][0][-1][0] += .1
                if defect == 'missing-pose': raw['poses'].pop()
                if defect == 'duplicate-row': raw['rows'].append(copy.deepcopy(raw['rows'][0]))
                with self.assertRaises(ValueError):
                    legacy_player_profile(raw, binding)

    def synthetic_sources(self):
        binding = copy.deepcopy(self.binding)
        c, raw = binding['geometry_contract'], self.raw
        sources = {name: '// Owned source-contract test input.\n' for name in c['sources_sha256']}
        hierarchy = [row['class'] for row in binding['hierarchy']]
        for child, parent in zip(hierarchy, hierarchy[1:]):
            sources[child + '.java'] += 'public abstract class ' + child + ' extends ' + parent + ' {}\n'
        e, l, p, a, pose = (c[key] for key in ('entity', 'living', 'player', 'accessor', 'pose'))
        names = raw['method_names']
        sources[e + '.java'] += f"""
public boolean {c['blocks_building_field']};
protected static final {a}<{pose}> {c['pose_accessor_field']} = {c['data']}.a({e}.class, serializer);
protected void {names['setPose']}({pose} value) {{ this.data.set({c['pose_accessor_field']}, value); }}
public void {names['refreshDimensions']}() {{ refresh(); }}
public void {names['setPos']}(double x, double y, double z) {{ position(); }}
public Box {names['getBoundingBox']}() {{ return box; }}
public void {names['onSyncedDataUpdated']}({a}<?> value) {{ if ({c['pose_accessor_field']}.equals(value)) {{ this.{names['refreshDimensions']}(); }} }}
"""
        sources[l + '.java'] += f"""
protected {l}(Type type, World world) {{ this.{c['blocks_building_field']} = true; }}
public void {names['onSyncedDataUpdated']}({a}<?> value) {{ super.{names['onSyncedDataUpdated']}(value); }}
"""
        sources[p + '.java'] += f"public Size {names['getDimensions']}({pose} value) {{ return sizes.getOrDefault(value, defaultSize); }}\n"
        sources[c['building_predicate_owner'] + '.java'] += f"stream.filter(value -> !value.removed && value.{c['blocks_building_field']} && accepts(value)).noneMatch(value -> value.{names['getBoundingBox']}());\n"
        binding['geometry_contract']['sources_sha256'] = {name: hashlib.sha256(text.encode()).hexdigest() for name, text in sources.items()}
        return binding, sources

    def test_source_contract_checks_inheritance_constructor_and_callback_order(self):
        binding, sources = self.synthetic_sources()
        verify_contract_sources(self.raw, binding, sources)
        c = binding['geometry_contract']
        names = self.raw['method_names']
        mutations = {
            'hierarchy': (binding['actor'] + '.java', ' extends ', ' implements '),
            'constructor': (c['living'] + '.java', f"this.{c['blocks_building_field']} = true;", f"this.{c['blocks_building_field']} = false;"),
            'pose-setter': (c['entity'] + '.java', f"this.data.set({c['pose_accessor_field']}, value);", 'return;'),
            'callback-order': (c['living'] + '.java', f"super.{names['onSyncedDataUpdated']}(value);", f"if (blocked) return; super.{names['onSyncedDataUpdated']}(value);"),
            'callback-refresh': (c['entity'] + '.java', f"this.{names['refreshDimensions']}();", 'unrelated();'),
            'dimension-selector': (c['player'] + '.java', 'return sizes.getOrDefault(value, defaultSize);', 'return extraSelector ? otherSize : sizes.getOrDefault(value, defaultSize);'),
        }
        for defect, (file, before, after) in mutations.items():
            with self.subTest(defect=defect):
                altered, inputs = copy.deepcopy(binding), dict(sources)
                self.assertIn(before, inputs[file])
                inputs[file] = inputs[file].replace(before, after)
                altered['geometry_contract']['sources_sha256'][file] = hashlib.sha256(inputs[file].encode()).hexdigest()
                with self.assertRaises(ValueError):
                    verify_contract_sources(self.raw, altered, inputs)
        with self.assertRaisesRegex(ValueError, 'source hash'):
            verify_contract_sources(self.raw, binding, sources | {c['entity'] + '.java': 'changed'})

    def test_source_contract_requires_every_hashed_native_owner(self):
        with self.assertRaisesRegex(ValueError, 'source coverage'):
            verify_contract_sources(self.raw, self.binding, {})


if __name__ == '__main__':
    unittest.main()
