import copy
import unittest

from extract_legacy_player_spawn import validate_rows


class LegacyPlayerSpawnImportTests(unittest.TestCase):
    def rows(self) -> list[dict]:
        rows = []
        for point in range(4):
            for flags in [0, 2, 128, 130]:
                for terrain in [False, True]:
                    observations = []
                    for phase in ['after_spawn', 'after_metadata', 'after_explicit_position', 'after_replacement']:
                        observations.append({'operation': phase, 'actor_class': 'OriginalRemotePlayer', 'flags': flags ^ 2 if phase in ['after_metadata', 'after_explicit_position'] else flags, 'age': 0, 'added': terrain, 'in_entity_list': True, 'registered_chunk': [0, 4, 0], 'sleeping': False, 'size': [.6, 1.8], 'state': {'position': [1.5, 64., 1.5], 'rotation': [0., 0.], 'target': [0., 0., 0., 0., 0.], 'box': [1.2, 64., 1.2, 1.8, 65.8, 1.8], 'active': False}})
                    actions = (['terrain'] if terrain else []) + ['spawn', 'metadata', 'replacement']
                    packets = [dict(action=action, bytes=1, sha256='a' * 64, **({'native_actor_reconstruction_equal': True} if action in ['spawn', 'replacement'] else {})) for action in actions]
                    rows.append({'position': point, 'flags': flags, 'terrain': terrain, 'observations': observations, 'replaced_instance': True, 'packets': packets})
        return rows

    def test_complete_original_spawn_witness_is_retained(self) -> None:
        rows = self.rows()
        self.assertEqual(rows, validate_rows(rows, 'OriginalRemotePlayer'))

    def test_missing_scenario_is_rejected(self) -> None:
        rows = self.rows()
        rows.pop()
        with self.assertRaisesRegex(ValueError, 'scenario'):
            validate_rows(rows, 'OriginalRemotePlayer')

    def test_repeated_scenario_is_rejected(self) -> None:
        rows = self.rows()
        rows[-1] = copy.deepcopy(rows[0])
        with self.assertRaisesRegex(ValueError, 'scenario'):
            validate_rows(rows, 'OriginalRemotePlayer')

    def test_adapter_instead_of_original_actor_is_rejected(self) -> None:
        rows = self.rows()
        rows[0]['observations'][0]['actor_class'] = 'Adapter'
        with self.assertRaisesRegex(ValueError, 'actor'):
            validate_rows(rows, 'OriginalRemotePlayer')

    def test_metadata_dispatch_without_flag_change_is_rejected(self) -> None:
        rows = self.rows()
        rows[0]['observations'][1]['flags'] = 0
        with self.assertRaisesRegex(ValueError, 'flags'):
            validate_rows(rows, 'OriginalRemotePlayer')

    def test_changed_phase_order_is_rejected(self) -> None:
        rows = self.rows()
        rows[0]['observations'].reverse()
        with self.assertRaisesRegex(ValueError, 'phase'):
            validate_rows(rows, 'OriginalRemotePlayer')

    def test_changed_packet_order_is_rejected(self) -> None:
        rows = self.rows()
        rows[0]['packets'].reverse()
        with self.assertRaisesRegex(ValueError, 'packet'):
            validate_rows(rows, 'OriginalRemotePlayer')

    def test_missing_actor_reconstruction_proof_is_rejected(self) -> None:
        rows = self.rows()
        del rows[0]['packets'][0]['native_actor_reconstruction_equal']
        with self.assertRaisesRegex(ValueError, 'reconstruction'):
            validate_rows(rows, 'OriginalRemotePlayer')

    def test_retained_replacement_identity_is_rejected(self) -> None:
        rows = self.rows()
        rows[0]['replaced_instance'] = False
        with self.assertRaisesRegex(ValueError, 'replacement'):
            validate_rows(rows, 'OriginalRemotePlayer')

    def test_nonfinite_native_geometry_is_rejected(self) -> None:
        rows = self.rows()
        rows[0]['observations'][0]['state']['box'][0] = float('nan')
        with self.assertRaisesRegex(ValueError, 'geometry'):
            validate_rows(rows, 'OriginalRemotePlayer')


if __name__ == '__main__':
    unittest.main()
