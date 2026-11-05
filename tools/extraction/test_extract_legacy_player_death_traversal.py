import copy
import unittest

from extract_legacy_player_death_traversal import validate_rows


class LegacyPlayerDeathTraversalImportTests(unittest.TestCase):
    def rows(self) -> list[dict]:
        rows = []
        for flags in [0, 2, 128]:
            for mode in range(5):
                for terrain in range(4):
                    phases = ['initial'] + [f'{phase}_tick_{tick}' for tick in range(40) for phase in ['entity', 'world']]
                    snapshots = []
                    for phase in phases:
                        tick = -1 if phase == 'initial' else int(phase.rsplit('_', 1)[1])
                        health = 20. if mode == 0 or mode in (3, 4) and tick >= 10 and (mode == 3 or tick < 15) else 0.
                        snapshots.append({'operation': phase, 'health': health, 'age': 0, 'added': False, 'in_entity_list': True, 'registered_chunk': [0, 0, 0], 'removed': False, 'death_time': 0, 'alive': health > 0, 'lookup_same': True, 'placement_clear': True, 'velocity': [0., 0., 0.], 'size': [.6, 1.8], 'state': {'position': [1.5, 64., 1.5], 'rotation': [0., 0.], 'target': [3.5, 65., 3.5, 90., 45.], 'active': True, 'box': [1.2, 64., 1.2, 1.8, 65.8, 1.8]}})
                    actions = (['initial_full'] if terrain in (0, 2) else []) + ['initial_health'] + (['death_status'] if mode in (2, 3) else [])
                    for tick in range(40):
                        if terrain == 2 and tick == 5:
                            actions.append('unload_5')
                        if terrain == 2 and tick == 12 or terrain == 3 and tick == 5:
                            actions.append(f'full_{tick}')
                        if mode in (3, 4) and tick == 10:
                            actions.append('revive_10')
                        if mode == 4 and tick == 15:
                            actions.append('zero_15')
                    rows.append({'flags': flags, 'mode': mode, 'terrain': terrain, 'observations': snapshots, 'packets': [dict(action=action, bytes=2, sha256='a' * 64) for action in actions]})
        return rows

    def test_complete_cohort_is_retained(self) -> None:
        rows = self.rows()
        self.assertEqual(rows, validate_rows(rows))

    def test_missing_scenario_is_rejected(self) -> None:
        rows = self.rows()
        rows.pop()
        with self.assertRaisesRegex(ValueError, 'scenario'):
            validate_rows(rows)

    def test_duplicate_scenario_is_rejected(self) -> None:
        rows = self.rows()
        rows[-1] = copy.deepcopy(rows[0])
        with self.assertRaisesRegex(ValueError, 'scenario'):
            validate_rows(rows)

    def test_phase_order_is_checked(self) -> None:
        rows = self.rows()
        rows[0]['observations'].reverse()
        with self.assertRaisesRegex(ValueError, 'phase'):
            validate_rows(rows)

    def test_unknown_membership_is_rejected(self) -> None:
        rows = self.rows()
        rows[0]['observations'][0]['added'] = None
        with self.assertRaisesRegex(ValueError, 'membership'):
            validate_rows(rows)

    def test_missing_lookup_identity_is_rejected(self) -> None:
        rows = self.rows()
        rows[0]['observations'][-1]['lookup_same'] = False
        with self.assertRaisesRegex(ValueError, 'lookup'):
            validate_rows(rows)

    def test_health_metadata_changes_are_retained(self) -> None:
        rows = self.rows()
        rows[0]['observations'][0]['health'] = 0.
        with self.assertRaisesRegex(ValueError, 'health'):
            validate_rows(rows)

    def test_unbounded_counter_is_rejected(self) -> None:
        rows = self.rows()
        rows[0]['observations'][-1]['death_time'] = 21
        with self.assertRaisesRegex(ValueError, 'counter'):
            validate_rows(rows)

    def test_world_phase_cannot_age_the_actor(self) -> None:
        rows = self.rows()
        rows[0]['observations'][2]['age'] = 1
        with self.assertRaisesRegex(ValueError, 'world phase'):
            validate_rows(rows)

    def test_removed_actor_cannot_remain_in_the_world_list(self) -> None:
        rows = self.rows()
        item = rows[0]['observations'][-1]
        item['removed'] = True
        item['death_time'] = 20
        item['alive'] = False
        with self.assertRaisesRegex(ValueError, 'removed'):
            validate_rows(rows)

    def test_nonfinite_geometry_is_rejected(self) -> None:
        rows = self.rows()
        rows[0]['observations'][0]['state']['box'][0] = float('nan')
        with self.assertRaisesRegex(ValueError, 'geometry'):
            validate_rows(rows)

    def test_packet_order_is_checked(self) -> None:
        rows = self.rows()
        rows[0]['packets'].reverse()
        with self.assertRaisesRegex(ValueError, 'packet'):
            validate_rows(rows)

    def test_packet_hash_is_checked(self) -> None:
        rows = self.rows()
        rows[0]['packets'][0]['sha256'] = 'missing'
        with self.assertRaisesRegex(ValueError, 'packet'):
            validate_rows(rows)


if __name__ == '__main__':
    unittest.main()
