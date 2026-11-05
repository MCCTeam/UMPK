import copy
import unittest

from extract_legacy_player_death import YAWS, validate_rows


class LegacyPlayerDeathImportTests(unittest.TestCase):
    def rows(self) -> list[dict]:
        rows = []
        for angle, yaw in enumerate(YAWS):
            for flags in [0, 1, 2, 3, 128, 129, 130, 131]:
                for health in [20., 0.]:
                    for hurt in [False, True]:
                        phases = ['initial', 'after_health_metadata'] + (['after_hurt_status'] if hurt else []) + ['after_death_status', 'after_repeated_death_status']
                        observations = []
                        for phase in phases:
                            dead = phase in ['after_death_status', 'after_repeated_death_status']
                            observations.append({'operation': phase, 'age': 0, 'added': False, 'in_entity_list': True, 'registered_chunk': [0, 0, 0], 'sleeping': False, 'damage_yaw': 0., 'flags': 0 if phase == 'initial' else flags & ~1 if dead else flags, 'health': 20. if phase == 'initial' else 0. if dead else health, 'velocity': [-.1, .10000000149011612, 0.] if dead else [.125, .25, -.375], 'size': [.2, .2] if dead else [.6, 1.8], 'state': {'position': [1.5, 64., 1.5], 'rotation': [yaw, 0.], 'target': [3.5, 65., 3.5, 90., 45.], 'active': True, 'box': [1.4, 64., 1.4, 1.6, 64.2, 1.6] if dead else [1.2, 64., 1.2, 1.8, 65.8, 1.8]}})
                        actions = ['health_metadata'] + (['hurt_status'] if hurt else []) + ['death_status', 'repeated_death_status']
                        packets = [dict(action=action, bytes=2, sha256='a' * 64) for action in actions]
                        rows.append({'angle': angle, 'flags': flags, 'health': health, 'hurt': hurt, 'observations': observations, 'packets': packets})
        return rows

    def test_complete_prior_state_matrix_is_retained(self) -> None:
        rows = self.rows()
        self.assertEqual(rows, validate_rows(rows, clears_fire=True))

    def test_missing_scenario_is_rejected(self) -> None:
        rows = self.rows()
        rows.pop()
        with self.assertRaisesRegex(ValueError, 'scenario'):
            validate_rows(rows, clears_fire=True)

    def test_repeated_scenario_is_rejected(self) -> None:
        rows = self.rows()
        rows[-1] = copy.deepcopy(rows[0])
        with self.assertRaisesRegex(ValueError, 'scenario'):
            validate_rows(rows, clears_fire=True)

    def test_metadata_must_apply_its_actual_health_value(self) -> None:
        rows = self.rows()
        rows[0]['observations'][1]['health'] = 0.
        with self.assertRaisesRegex(ValueError, 'health'):
            validate_rows(rows, clears_fire=True)

    def test_metadata_cannot_be_classified_as_a_death_resize(self) -> None:
        rows = self.rows()
        rows[0]['observations'][1]['size'] = [.2, .2]
        with self.assertRaisesRegex(ValueError, 'metadata'):
            validate_rows(rows, clears_fire=True)

    def test_metadata_cannot_be_classified_as_a_death_impulse(self) -> None:
        rows = self.rows()
        rows[0]['observations'][1]['velocity'] = [-.1, .1, 0.]
        with self.assertRaisesRegex(ValueError, 'metadata'):
            validate_rows(rows, clears_fire=True)

    def test_changed_yaw_is_rejected(self) -> None:
        rows = self.rows()
        rows[0]['observations'][1]['state']['rotation'][0] = 1.
        with self.assertRaisesRegex(ValueError, 'yaw'):
            validate_rows(rows, clears_fire=True)

    def test_changed_pending_target_is_rejected(self) -> None:
        rows = self.rows()
        rows[0]['observations'][-1]['state']['target'][0] = 0.
        with self.assertRaisesRegex(ValueError, 'pending'):
            validate_rows(rows, clears_fire=True)

    def test_death_must_preserve_nonfire_flags(self) -> None:
        rows = self.rows()
        row = next(row for row in rows if row['flags'] == 2)
        row['observations'][-1]['flags'] = 0
        with self.assertRaisesRegex(ValueError, 'flags'):
            validate_rows(rows, clears_fire=True)

    def test_unknown_damage_yaw_is_rejected(self) -> None:
        rows = self.rows()
        rows[0]['observations'][0]['damage_yaw'] = None
        with self.assertRaisesRegex(ValueError, 'damage yaw'):
            validate_rows(rows, clears_fire=True)

    def test_changed_packet_order_is_rejected(self) -> None:
        rows = self.rows()
        rows[0]['packets'].reverse()
        with self.assertRaisesRegex(ValueError, 'packet'):
            validate_rows(rows, clears_fire=True)

    def test_changed_repeated_death_result_is_rejected(self) -> None:
        rows = self.rows()
        rows[0]['observations'][-1]['velocity'][0] = 1.
        with self.assertRaisesRegex(ValueError, 'repeated'):
            validate_rows(rows, clears_fire=True)

    def test_nonfinite_observation_is_rejected(self) -> None:
        rows = self.rows()
        rows[0]['observations'][0]['state']['box'][0] = float('nan')
        with self.assertRaisesRegex(ValueError, 'geometry'):
            validate_rows(rows, clears_fire=True)


if __name__ == '__main__':
    unittest.main()
