import copy
import hashlib
from pathlib import Path
import tempfile
import unittest

from extract_legacy_player_bed_availability import BED_POSITIONS, read_terrain_bodies, validate_rows


class LegacyPlayerBedAvailabilityImportTests(unittest.TestCase):
    def rows(self) -> list[dict]:
        rows = []
        for point, bed in enumerate(BED_POSITIONS):
            for scene in range(4):
                state = {'position': [1.5, 64., 1.5], 'rotation': [0., 0.], 'target': [3.5, 65., 3.5, 90., 45.], 'box': [1.2, 64., 1.2, 1.8, 65.8, 1.8]}
                initial = {'operation': 'initial', 'age': 0, 'added': False, 'in_entity_list': True, 'registered_chunk': [0, 0, 0], 'sleeping': False, 'health': 20., 'velocity': [0., 0., 0.], 'bed_native_state': 0, 'size': [.6, 1.8], 'state': state}
                observations = [dict(copy.deepcopy(initial), operation=phase) for phase in ['initial', 'after_sleep_packet', 'after_wake_packet']]
                row = {'position': point, 'scene': scene, 'bed': list(bed), 'sleep': {'result': 'completed'}, 'wake': {'result': 'completed'}, 'observations': observations, 'packets': [{'kind': 'bed', 'body': '2a0000004100000001'}, {'kind': 'wake', 'body': '2a02'}]}
                if scene:
                    row['terrain_packet'] = {'action': 'loaded_full', 'bytes': 1, 'sha256': 'a' * 64}
                if scene >= 2:
                    row['bed_write_accepted'] = True
                rows.append(row)
        return rows

    def property_exception(self) -> list[dict]:
        rows = self.rows()
        row = rows[0]
        row['sleep'] = {'result': 'native_property_exception', 'exception': 'java.lang.IllegalArgumentException', 'message': 'Cannot get property facing from air'}
        del row['wake']
        row['observations'].pop()
        row['packets'].pop()
        return rows

    def test_complete_matrix_is_retained(self) -> None:
        rows = self.rows()
        self.assertEqual(rows, validate_rows(rows))

    def test_partial_native_exception_state_is_retained(self) -> None:
        rows = self.property_exception()
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

    def test_changed_bed_coordinate_is_rejected(self) -> None:
        rows = self.rows()
        rows[0]['bed'][1] = 63
        with self.assertRaisesRegex(ValueError, 'bed'):
            validate_rows(rows)

    def test_wake_after_failed_native_sleep_is_rejected(self) -> None:
        rows = self.property_exception()
        rows[0]['wake'] = {'result': 'completed'}
        with self.assertRaisesRegex(ValueError, 'exception'):
            validate_rows(rows)

    def test_unexpected_native_exception_is_rejected(self) -> None:
        rows = self.property_exception()
        rows[0]['sleep']['exception'] = 'java.lang.NullPointerException'
        with self.assertRaisesRegex(ValueError, 'exception'):
            validate_rows(rows)

    def test_missing_exception_message_is_rejected(self) -> None:
        rows = self.property_exception()
        rows[0]['sleep']['message'] = ''
        with self.assertRaisesRegex(ValueError, 'exception'):
            validate_rows(rows)

    def test_missing_terrain_setup_receipt_is_rejected(self) -> None:
        rows = self.rows()
        del rows[1]['terrain_packet']
        with self.assertRaisesRegex(ValueError, 'terrain'):
            validate_rows(rows)

    def test_unknown_native_block_write_is_rejected(self) -> None:
        rows = self.rows()
        rows[2]['bed_write_accepted'] = None
        with self.assertRaisesRegex(ValueError, 'write'):
            validate_rows(rows)

    def test_changed_phase_order_is_rejected(self) -> None:
        rows = self.rows()
        rows[0]['observations'][1]['operation'] = 'after_wake_packet'
        with self.assertRaisesRegex(ValueError, 'phase'):
            validate_rows(rows)

    def test_nonfinite_geometry_is_rejected(self) -> None:
        rows = self.rows()
        rows[0]['observations'][0]['state']['position'][0] = float('inf')
        with self.assertRaisesRegex(ValueError, 'geometry'):
            validate_rows(rows)

    def test_malformed_original_packet_is_rejected(self) -> None:
        rows = self.rows()
        rows[0]['packets'][0]['body'] = 'xyz'
        with self.assertRaisesRegex(ValueError, 'packet'):
            validate_rows(rows)

    def test_terrain_body_is_verified_and_retained(self) -> None:
        body = b'original chunk'
        digest = hashlib.sha256(body).hexdigest()
        rows = [{'terrain_packet': {'bytes': len(body), 'sha256': digest}}]
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / (digest + '.bin')).write_bytes(body)
            self.assertEqual({digest: body.hex()}, read_terrain_bodies(root, rows))
            (root / (digest + '.bin')).write_bytes(b'changed chunk')
            with self.assertRaisesRegex(ValueError, 'packet'):
                read_terrain_bodies(root, rows)


if __name__ == '__main__':
    unittest.main()
