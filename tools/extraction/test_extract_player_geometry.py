"""Check native player snapshots, pose callbacks and protocol aliases together."""

import copy
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from extract_player_geometry import group_players, player_profile


class PlayerGeometryExtractionTests(unittest.TestCase):
    def setUp(self):
        path = Path(__file__).parent / 'tests/fixtures/player-box-transitions-1.14.4.json'
        fixture = json.loads(path.read_text(encoding='utf-8'))
        self.native = fixture['native']
        self.transitions = fixture['transitions']

    def test_native_pose_callbacks_retain_the_measured_minimum_on_growth(self):
        profile = player_profile(self.native, self.transitions)

        self.assertEqual([-1, 'none', 0, 0], profile['selector'])
        self.assertEqual(6, profile['pose_index'])
        self.assertEqual('retain_minimum_on_growth', profile['refresh'])
        self.assertEqual([[0, 0, .6, 1.8, False, True], [0, 2, .2, .2, True, True], [0, 5, .6, 1.5, False, True]], profile['variants'])

    def test_native_scale_contract_is_kept_separate_from_default_dimensions(self):
        profile = player_profile(self.native, self.transitions, scale_attribute='minecraft:scale')
        self.assertEqual('minecraft:scale', profile['scale_attribute'])
        with self.assertRaises(ValueError):
            player_profile(self.native, self.transitions, scale_attribute='minecraft:missing')

    def test_changed_snapshot_geometry_and_callback_owners_are_rejected(self):
        for defect in ('constructor', 'blocking', 'class', 'pose-index', 'dimensions', 'snapshot-box', 'pose-count', 'dimension-owner', 'box-owner', 'callback-owner'):
            with self.subTest(defect=defect):
                native, transitions = copy.deepcopy(self.native), copy.deepcopy(self.transitions)
                if defect == 'constructor': native['constructor_blocks_building'] = False
                if defect == 'blocking': native['blocks_building'] = False
                if defect == 'class': native['class'] = 'UnauditedAdapter'
                if defect == 'pose-index': native['geometry']['pose_index'] = 7
                if defect == 'dimensions': native['dimensions']['STANDING'][0] = .7
                if defect == 'snapshot-box': native['geometry']['default']['poses'][0][-1][0] += .1
                if defect == 'pose-count': native['dimensions'].pop('SLEEPING')
                if defect == 'dimension-owner': native['method_owners']['getDimensions'] = 'EntityPlayerAdapter'
                if defect == 'box-owner': native['method_owners']['getBoundingBox'] = 'EntityPlayerAdapter'
                if defect == 'callback-owner': transitions['method_owners']['setPose'] = 'EntityPlayerAdapter'
                with self.assertRaises(ValueError):
                    player_profile(native, transitions)

    def test_incomplete_or_changed_native_phase_witnesses_are_rejected(self):
        for defect in ('missing', 'duplicate', 'unknown-pose', 'same-pose-dimensions', 'synchronized-box', 'refresh-box', 'position-box'):
            with self.subTest(defect=defect):
                transitions = copy.deepcopy(self.transitions)
                if defect == 'missing': transitions['rows'].pop()
                if defect == 'duplicate': transitions['rows'].append(copy.deepcopy(transitions['rows'][0]))
                if defect == 'unknown-pose': transitions['poses'][0] = 'UNKNOWN'
                if defect == 'same-pose-dimensions': transitions['rows'][0]['to_size'][0] += .1
                if defect == 'synchronized-box': transitions['rows'][0]['after_sync'][0] += .1
                if defect == 'refresh-box': transitions['rows'][0]['after_refresh'][0] += .1
                if defect == 'position-box': transitions['rows'][0]['after_position'][0] += .1
                with self.assertRaises(ValueError):
                    player_profile(self.native, transitions)

    def test_aliases_require_equal_complete_native_profiles(self):
        first = {'version': 'first', 'protocol': 498, 'native': self.native, 'transitions': self.transitions, 'scale_attribute': ''}
        second = copy.deepcopy(first) | {'version': 'alias'}
        self.assertEqual(2, len(group_players([first, second])[498]))
        for defect in ('duplicate', 'changed-scale', 'changed-box'):
            with self.subTest(defect=defect):
                other = copy.deepcopy(second)
                if defect == 'duplicate': other['version'] = first['version']
                if defect == 'changed-scale': other['scale_attribute'] = 'minecraft:scale'
                if defect == 'changed-box': other['transitions']['rows'][0]['after_sync'][0] += .1
                with self.assertRaises(ValueError):
                    group_players([first, other])

    def test_source_provenance_is_identical_across_python_hash_seeds(self):
        owners = {'net.minecraft.world.entity.Entity', 'net.minecraft.world.entity.LivingEntity', 'net.minecraft.world.entity.player.Player'}
        with tempfile.TemporaryDirectory(prefix='umpk-player-provenance-') as temporary:
            root = Path(temporary)
            for owner in owners:
                path = root / (owner.replace('.', '/') + '.java')
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text('// temporary source input: ' + owner + '\n')
            script = 'import json,sys;from pathlib import Path;from extract_player_geometry import source_hashes;print(json.dumps(source_hashes(Path(sys.argv[1]), set(sys.argv[2:]))))'
            outputs = []
            for seed in ('1', '7', '19'):
                environment = {**os.environ, 'PYTHONHASHSEED': seed}
                outputs.append(subprocess.check_output([sys.executable, '-c', script, str(root), *sorted(owners)], cwd=Path(__file__).parent, env=environment, text=True))
            self.assertEqual(outputs[0], outputs[1])
            self.assertEqual(outputs[0], outputs[2])
            self.assertEqual(sorted(json.loads(outputs[0])), list(json.loads(outputs[0])))


if __name__ == '__main__':
    unittest.main()
