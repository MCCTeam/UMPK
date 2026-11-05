import copy
import unittest

from extract_legacy_player_partial_context import validate_rows
import test_extract_legacy_player_block_context as context_tests


class LegacyPlayerPartialContextImportTests(unittest.TestCase):
    def rows(self, *, traversal: bool, full: bool = False) -> list[dict]:
        rows = []
        for point, a in enumerate([[1, 64, 1], [-17, 64, -17], [15, 64, 15], [16, 64, 16]]):
            for terrain in range(4):
                for target in range(2):
                    for payload in range(2):
                        kind = 'full_payload' if full else 'partial_payload'
                        actions = ['seed_bed_a'] + (['full_b' if terrain == 2 else 'full_a'] if terrain else []) + (['unload_a'] if terrain == 3 else []) + [kind]
                        phases = ['initial'] + ([f'{phase}_tick_{tick}' for tick in range(8) for phase in ['entity', 'world']] if traversal else ['after_' + action for action in actions] + ['after_sleep', 'after_wake'])
                        def snapshot(phase: str) -> dict:
                            item = context_tests.LegacyPlayerBlockContextImportTests().snapshot(phase)
                            item['reads'] = [{key: item[key] for key in ['bed_native_state', 'bed_available', 'empty_chunk', 'chunk_class']} | {'same_as_previous_chunk': True} for _ in range(3)]
                            if traversal:
                                item['placement_clear'] = True
                            return item
                        row = {'point': point, 'terrain': terrain, 'partial_cell': target, 'payload': payload, 'cells': [a, [a[0] + 16, a[1], a[2] + 16], [a[0] + 17, a[1], a[2] + 16]], 'world_packets': [dict(action=action, bytes=1, sha256='a' * 64) for action in actions], 'observations': [snapshot(phase) for phase in phases]}
                        if traversal:
                            row['setup'] = [snapshot(phase) for phase in ['after_spawn', 'after_' + kind]]
                        else:
                            row.update(sleep={'result': 'completed'}, wake={'result': 'completed'}, packets=[dict(kind=kind, body='00') for kind in ['bed', 'wake']])
                        rows.append(row)
        return rows

    def test_callback_fields_are_retained(self) -> None:
        rows = self.rows(traversal=False)
        self.assertEqual(rows, validate_rows(rows, traversal=False))

    def test_traversal_fields_are_retained(self) -> None:
        rows = self.rows(traversal=True)
        self.assertEqual(rows, validate_rows(rows, traversal=True))

    def test_full_control_fields_are_retained(self) -> None:
        rows = self.rows(traversal=True, full=True)
        self.assertEqual(rows, validate_rows(rows, traversal=True, full=True))

    def test_partial_payload_cannot_replace_the_full_control(self) -> None:
        rows = self.rows(traversal=True)
        with self.assertRaisesRegex(ValueError, 'packet'):
            validate_rows(rows, traversal=True, full=True)

    def test_missing_scenario_is_rejected(self) -> None:
        rows = self.rows(traversal=True)
        rows.pop()
        with self.assertRaisesRegex(ValueError, 'scenario'):
            validate_rows(rows, traversal=True)

    def test_duplicate_scenario_is_rejected(self) -> None:
        rows = self.rows(traversal=False)
        rows[-1] = copy.deepcopy(rows[0])
        with self.assertRaisesRegex(ValueError, 'scenario'):
            validate_rows(rows, traversal=False)

    def test_wrong_local_alias_is_rejected(self) -> None:
        rows = self.rows(traversal=False)
        rows[0]['cells'][1][0] += 1
        with self.assertRaisesRegex(ValueError, 'coordinate'):
            validate_rows(rows, traversal=False)

    def test_unknown_object_identity_is_rejected(self) -> None:
        rows = self.rows(traversal=False)
        rows[0]['observations'][0]['reads'][0]['same_as_previous_chunk'] = None
        with self.assertRaisesRegex(ValueError, 'identity'):
            validate_rows(rows, traversal=False)

    def test_unknown_query_is_rejected(self) -> None:
        rows = self.rows(traversal=True)
        rows[0]['setup'][0]['placement_clear'] = None
        with self.assertRaisesRegex(ValueError, 'query'):
            validate_rows(rows, traversal=True)

    def test_wrong_packet_order_is_rejected(self) -> None:
        rows = self.rows(traversal=True)
        rows[0]['world_packets'].reverse()
        with self.assertRaisesRegex(ValueError, 'packet'):
            validate_rows(rows, traversal=True)

    def test_wrong_setup_order_is_rejected(self) -> None:
        rows = self.rows(traversal=True)
        rows[0]['setup'].reverse()
        with self.assertRaisesRegex(ValueError, 'phase'):
            validate_rows(rows, traversal=True)

    def test_missing_tick_is_rejected(self) -> None:
        rows = self.rows(traversal=True)
        rows[0]['observations'].pop()
        with self.assertRaisesRegex(ValueError, 'phase'):
            validate_rows(rows, traversal=True)

    def test_missing_cell_read_is_rejected(self) -> None:
        rows = self.rows(traversal=False)
        rows[0]['observations'][0]['reads'].pop()
        with self.assertRaisesRegex(ValueError, 'cell'):
            validate_rows(rows, traversal=False)

    def test_invalid_callback_body_is_rejected(self) -> None:
        rows = self.rows(traversal=False)
        rows[0]['packets'][0]['body'] = 'invalid'
        with self.assertRaisesRegex(ValueError, 'body'):
            validate_rows(rows, traversal=False)

    def test_aborted_sleep_cannot_wake(self) -> None:
        rows = self.rows(traversal=False)
        rows[0]['sleep'] = {'result': 'native_property_exception', 'exception': 'java.lang.IllegalArgumentException', 'message': 'missing property'}
        with self.assertRaisesRegex(ValueError, 'wake'):
            validate_rows(rows, traversal=False)


if __name__ == '__main__':
    unittest.main()
