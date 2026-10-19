import copy
import unittest

from extract_legacy_player_block_context import validate_context, validate_resize


class LegacyPlayerBlockContextImportTests(unittest.TestCase):
    def snapshot(self, phase: str) -> dict:
        return {'operation': phase, 'age': 0, 'added': False, 'in_entity_list': True, 'registered_chunk': [0, 0, 0], 'size': [.6, 1.8], 'sleeping': False, 'health': 20., 'velocity': [0., 0., 0.], 'bed_native_state': 0, 'bed_available': True, 'empty_chunk': True, 'chunk_class': 'native.EmptyChunk', 'state': {'position': [1.5, 64., 1.5], 'rotation': [0., 0.], 'target': [3.5, 65., 3.5, 90., 45.], 'active': True, 'box': [1.2, 64., 1.2, 1.8, 65.8, 1.8]}}

    def packets(self, actions: list[str]) -> list[dict]:
        return [dict(action=action, bytes=1, sha256='a' * 64) for action in actions]

    def context(self) -> list[dict]:
        points = [[1, 64, 1], [-17, 64, -17], [15, 64, 15], [16, 64, 16]]
        rows = []
        for point, bed in enumerate(points):
            for scene in range(8):
                actions = (['initial_full'] if scene in (1, 2, 4, 5, 7) else []) + (['unload_before_update'] if scene == 2 else []) + ['bed_update']
                actions += (['unload_after_update'] if scene == 5 else []) + (['full_after_update'] if scene in (3, 4, 5) else []) + (['air_update'] if scene in (6, 7) else [])
                phases = ['initial'] + ['after_' + action for action in actions if action != 'initial_full'] + ['after_sleep_packet', 'after_wake_packet']
                rows.append({'point': point, 'scene': scene, 'bed': bed, 'sleep': {'result': 'completed'}, 'wake': {'result': 'completed'}, 'observations': [self.snapshot(phase) for phase in phases], 'world_packets': self.packets(actions), 'packets': [dict(kind=kind, body='00') for kind in ['bed', 'wake']]})
        return rows

    def resize(self) -> list[dict]:
        points = [[1, 64, 1], [-18, 64, -18], [15, 64, 15], [16, 64, 16]]
        rows = []
        for point, bed in enumerate(points):
            for flags in [2, 128]:
                for scene in range(6):
                    actions = (['initial_full'] if scene in (1, 2, 3, 5) else []) + (['unload_before_update'] if scene == 2 else []) + ['bed_update']
                    actions += (['unload_after_update', 'second_bed_update'] if scene == 3 else []) + (['full_after_update'] if scene in (4, 5) else [])
                    setup = ['before_updates'] + ['after_' + action for action in actions if action != 'initial_full']
                    phases = ['initial'] + [f'{phase}_tick_{tick}' for tick in range(8) for phase in ['entity', 'world']]
                    rows.append({'point': point, 'flags': flags, 'scene': scene, 'bed': bed, 'setup': [self.snapshot(phase) for phase in setup], 'observations': [self.snapshot(phase) for phase in phases], 'packets': self.packets(actions)})
        return rows

    def test_context_cohort_is_retained(self) -> None:
        rows = self.context()
        self.assertEqual(rows, validate_context(rows))

    def test_resize_cohort_is_retained(self) -> None:
        rows = self.resize()
        self.assertEqual(rows, validate_resize(rows))

    def test_missing_context_scenario_is_rejected(self) -> None:
        rows = self.context()
        rows.pop()
        with self.assertRaisesRegex(ValueError, 'scenario'):
            validate_context(rows)

    def test_duplicate_context_scenario_is_rejected(self) -> None:
        rows = self.context()
        rows[-1] = copy.deepcopy(rows[0])
        with self.assertRaisesRegex(ValueError, 'scenario'):
            validate_context(rows)

    def test_missing_resize_scenario_is_rejected(self) -> None:
        rows = self.resize()
        rows.pop()
        with self.assertRaisesRegex(ValueError, 'scenario'):
            validate_resize(rows)

    def test_context_cell_must_match_its_input(self) -> None:
        rows = self.context()
        rows[0]['bed'][0] = 100
        with self.assertRaisesRegex(ValueError, 'coordinate'):
            validate_context(rows)

    def test_unknown_native_availability_is_rejected(self) -> None:
        rows = self.context()
        rows[0]['observations'][0]['bed_available'] = None
        with self.assertRaisesRegex(ValueError, 'knowledge'):
            validate_context(rows)

    def test_actual_chunk_class_must_be_present(self) -> None:
        rows = self.context()
        rows[0]['observations'][0]['chunk_class'] = ''
        with self.assertRaisesRegex(ValueError, 'class'):
            validate_context(rows)

    def test_nonfinite_geometry_is_rejected(self) -> None:
        rows = self.context()
        rows[0]['observations'][0]['state']['box'][0] = float('nan')
        with self.assertRaisesRegex(ValueError, 'geometry'):
            validate_context(rows)

    def test_aborted_sleep_cannot_be_followed_by_wake(self) -> None:
        rows = self.context()
        rows[0]['sleep'] = {'result': 'native_property_exception', 'exception': 'java.lang.IllegalArgumentException', 'message': 'missing property'}
        with self.assertRaisesRegex(ValueError, 'wake'):
            validate_context(rows)

    def test_world_packet_order_is_checked(self) -> None:
        rows = self.context()
        rows[1]['world_packets'].reverse()
        with self.assertRaisesRegex(ValueError, 'packet'):
            validate_context(rows)

    def test_callback_phase_order_is_checked(self) -> None:
        rows = self.context()
        rows[0]['observations'].reverse()
        with self.assertRaisesRegex(ValueError, 'phase'):
            validate_context(rows)

    def test_resize_setup_phase_order_is_checked(self) -> None:
        rows = self.resize()
        rows[0]['setup'].reverse()
        with self.assertRaisesRegex(ValueError, 'phase'):
            validate_resize(rows)

    def test_resize_tick_phase_order_is_checked(self) -> None:
        rows = self.resize()
        rows[0]['observations'].reverse()
        with self.assertRaisesRegex(ValueError, 'phase'):
            validate_resize(rows)

    def test_packet_hash_is_checked(self) -> None:
        rows = self.resize()
        rows[0]['packets'][0]['sha256'] = 'missing'
        with self.assertRaisesRegex(ValueError, 'packet'):
            validate_resize(rows)

    def test_fixed_spawn_coordinate_must_be_packet_representable(self) -> None:
        rows = self.resize()
        rows[0]['observations'][0]['state']['position'][1] = 63.3
        with self.assertRaisesRegex(ValueError, 'packet coordinate'):
            validate_resize(rows, fixed_positions=True)

    def test_fixed_target_coordinate_must_be_packet_representable(self) -> None:
        rows = self.resize()
        rows[0]['observations'][0]['state']['target'][1] = 64.3
        with self.assertRaisesRegex(ValueError, 'packet coordinate'):
            validate_resize(rows, fixed_positions=True)


if __name__ == '__main__':
    unittest.main()
