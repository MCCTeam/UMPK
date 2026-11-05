import copy
import hashlib
from pathlib import Path
import tempfile
import unittest

from extract_legacy_player_traversal import read_packet_bodies, validate_reentry, validate_rows


class LegacyPlayerTraversalImportTests(unittest.TestCase):
    def reentry(self) -> list[list[dict]]:
        initial = self.rows()[0]['observations'][0]
        phases = ['before_unload_phase'] + [f'{phase}_tick_{tick}' for tick in range(4) for phase in ['entity', 'world']]
        return [[dict(copy.deepcopy(initial), id=index + 1, operation=phase) for index in range(20)] for phase in phases]

    def test_complete_multi_player_reentry_is_retained(self) -> None:
        rows = self.reentry()
        self.assertEqual(rows, validate_reentry(rows))

    def test_missing_reentry_player_is_rejected(self) -> None:
        rows = self.reentry()
        rows[-1].pop()
        with self.assertRaisesRegex(ValueError, 'reentry'):
            validate_reentry(rows)

    def test_repeated_reentry_player_is_rejected(self) -> None:
        rows = self.reentry()
        rows[-1][-1]['id'] = rows[-1][0]['id']
        with self.assertRaisesRegex(ValueError, 'reentry'):
            validate_reentry(rows)
    def rows(self) -> list[dict]:
        rows = []
        for point in range(4):
            for scene in range(8):
                initial = {'operation': 'initial', 'age': 0, 'added': False, 'in_entity_list': True, 'registered_chunk': [0, 0, 0], 'size': [.6, 1.8], 'state': {'position': [1.5, 64., 1.5], 'box': [1.2, 64., 1.2, 1.8, 65.8, 1.8]}}
                observations = [initial]
                for tick in range(8):
                    for phase in ['entity', 'world']:
                        observation = copy.deepcopy(initial)
                        observation.update(operation=f'{phase}_tick_{tick}', age=tick + 1)
                        observations.append(observation)
                rows.append({'position': point, 'scene': scene, 'observations': observations, 'packets': [{'action': 'initial_full', 'bytes': 100, 'sha256': 'a' * 64}]})
        return rows

    def test_complete_phase_witness_is_retained(self) -> None:
        rows = self.rows()
        self.assertEqual(rows, validate_rows(rows))

    def test_missing_scenario_is_rejected(self) -> None:
        rows = self.rows()
        rows.pop()
        with self.assertRaisesRegex(ValueError, 'scenario'):
            validate_rows(rows)

    def test_repeated_scenario_is_rejected(self) -> None:
        rows = self.rows()
        rows[-1] = rows[0]
        with self.assertRaisesRegex(ValueError, 'scenario'):
            validate_rows(rows)

    def test_changed_phase_order_is_rejected(self) -> None:
        rows = self.rows()
        rows[0]['observations'][1]['operation'] = 'world_tick_0'
        with self.assertRaisesRegex(ValueError, 'phase'):
            validate_rows(rows)

    def test_unknown_membership_is_rejected(self) -> None:
        rows = self.rows()
        rows[0]['observations'][0]['added'] = None
        with self.assertRaisesRegex(ValueError, 'membership'):
            validate_rows(rows)

    def test_nonfinite_position_is_rejected(self) -> None:
        rows = self.rows()
        rows[0]['observations'][0]['state']['position'][1] = float('nan')
        with self.assertRaisesRegex(ValueError, 'geometry'):
            validate_rows(rows)

    def test_missing_packet_hash_is_rejected(self) -> None:
        rows = self.rows()
        rows[0]['packets'][0]['sha256'] = ''
        with self.assertRaisesRegex(ValueError, 'packet'):
            validate_rows(rows)

    def test_original_packet_bytes_are_retained(self) -> None:
        body = b'original chunk packet'
        digest = hashlib.sha256(body).hexdigest()
        rows = [{'packets': [{'sha256': digest, 'bytes': len(body)}]}]
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / (digest + '.bin')).write_bytes(body)
            self.assertEqual({digest: body.hex()}, read_packet_bodies(root, rows))

    def test_changed_original_packet_bytes_are_rejected(self) -> None:
        body = b'original chunk packet'
        digest = hashlib.sha256(body).hexdigest()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / (digest + '.bin')).write_bytes(b'changed chunk packet')
            with self.assertRaisesRegex(ValueError, 'packet'):
                read_packet_bodies(root, [{'packets': [{'sha256': digest, 'bytes': len(body)}]}])

    def test_truncated_original_packet_receipt_is_rejected(self) -> None:
        body = b'original chunk packet'
        digest = hashlib.sha256(body).hexdigest()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / (digest + '.bin')).write_bytes(body)
            with self.assertRaisesRegex(ValueError, 'packet'):
                read_packet_bodies(root, [{'packets': [{'sha256': digest, 'bytes': len(body) - 1}]}])


if __name__ == '__main__':
    unittest.main()
