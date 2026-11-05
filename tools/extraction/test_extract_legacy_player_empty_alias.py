import copy
import unittest

from extract_legacy_player_empty_alias import validate_rows
import test_extract_legacy_player_block_context as context_tests


class LegacyPlayerEmptyAliasImportTests(unittest.TestCase):
    def rows(self) -> list[dict]:
        rows = []
        for point, a in enumerate([[1, 64, 1], [-17, 64, -17], [15, 64, 15], [16, 64, 16]]):
            for scene in range(8):
                for sleep_cell in range(2):
                    actions = (['initial_full_a'] if scene == 2 else ['initial_full_b'] if scene == 5 else []) + ['bed_a']
                    actions += ['bed_b'] if scene == 1 else ['full_b'] if scene == 3 else ['full_a', 'unload_a'] if scene == 4 else ['air_c'] if scene == 6 else ['air_b']
                    if scene == 7:
                        actions.append('restore_bed_a')
                    phases = ['initial'] + ['after_' + action for action in actions if not action.startswith('initial_')] + ['after_sleep', 'after_wake']
                    observations = []
                    for phase in phases:
                        item = context_tests.LegacyPlayerBlockContextImportTests().snapshot(phase)
                        item['reads'] = [{key: item[key] for key in ['bed_native_state', 'bed_available', 'empty_chunk', 'chunk_class']} for _ in range(3)]
                        observations.append(item)
                    rows.append({'point': point, 'scene': scene, 'sleep_cell': sleep_cell, 'cells': [a, [a[0] + 16, a[1], a[2] + 16], [a[0] + 17, a[1], a[2] + 16]], 'sleep': {'result': 'completed'}, 'wake': {'result': 'completed'}, 'world_packets': [dict(action=action, bytes=1, sha256='a' * 64) for action in actions], 'packets': [dict(kind=kind, body='00') for kind in ['bed', 'wake']], 'observations': observations})
        return rows

    def test_all_original_fields_are_retained(self) -> None:
        rows = self.rows()
        self.assertEqual(rows, validate_rows(rows))

    def test_missing_alias_scenario_is_rejected(self) -> None:
        rows = self.rows()
        rows.pop()
        with self.assertRaisesRegex(ValueError, 'scenario'):
            validate_rows(rows)

    def test_duplicate_alias_scenario_is_rejected(self) -> None:
        rows = self.rows()
        rows[-1] = copy.deepcopy(rows[0])
        with self.assertRaisesRegex(ValueError, 'scenario'):
            validate_rows(rows)

    def test_cell_must_match_its_native_local_alias(self) -> None:
        rows = self.rows()
        rows[0]['cells'][1][0] += 1
        with self.assertRaisesRegex(ValueError, 'coordinate'):
            validate_rows(rows)

    def test_unknown_cell_state_is_rejected(self) -> None:
        rows = self.rows()
        rows[0]['observations'][0]['reads'][1]['bed_native_state'] = None
        with self.assertRaisesRegex(ValueError, 'state'):
            validate_rows(rows)

    def test_unknown_chunk_identity_is_rejected(self) -> None:
        rows = self.rows()
        rows[0]['observations'][0]['reads'][1]['empty_chunk'] = None
        with self.assertRaisesRegex(ValueError, 'knowledge'):
            validate_rows(rows)

    def test_missing_native_cell_is_rejected(self) -> None:
        rows = self.rows()
        rows[0]['observations'][0]['reads'].pop()
        with self.assertRaisesRegex(ValueError, 'cell'):
            validate_rows(rows)

    def test_aborted_sleep_cannot_be_followed_by_wake(self) -> None:
        rows = self.rows()
        rows[0]['sleep'] = {'result': 'native_property_exception', 'exception': 'java.lang.IllegalArgumentException', 'message': 'missing property'}
        with self.assertRaisesRegex(ValueError, 'wake'):
            validate_rows(rows)

    def test_packet_order_is_checked(self) -> None:
        rows = self.rows()
        rows[0]['world_packets'].reverse()
        with self.assertRaisesRegex(ValueError, 'packet'):
            validate_rows(rows)

    def test_phase_order_is_checked(self) -> None:
        rows = self.rows()
        rows[0]['observations'].reverse()
        with self.assertRaisesRegex(ValueError, 'phase'):
            validate_rows(rows)

    def test_primary_cell_must_equal_its_read(self) -> None:
        rows = self.rows()
        rows[0]['observations'][0]['bed_native_state'] = 1
        with self.assertRaisesRegex(ValueError, 'primary'):
            validate_rows(rows)

    def test_invalid_callback_body_is_rejected(self) -> None:
        rows = self.rows()
        rows[0]['packets'][0]['body'] = 'invalid'
        with self.assertRaisesRegex(ValueError, 'body'):
            validate_rows(rows)


if __name__ == '__main__':
    unittest.main()
