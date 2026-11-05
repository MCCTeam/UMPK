import copy
import gzip
import json
from pathlib import Path
import unittest

from extract_legacy_player_lifecycle import reduce_release, reduce_size_release, validate_owners


class LegacyPlayerLifecycleImportTests(unittest.TestCase):
    def lifecycle(self, classic: bool = True) -> tuple[dict, dict]:
        path = Path(__file__).resolve().parents[2] / 'tests/Umpk.Client.Tests/Fixtures/LegacyPlayerLifecycle.native.json.gz'
        fixture = json.loads(gzip.decompress(path.read_bytes()))
        release = next(row for row in fixture['releases'] if row['version'] == ('1.8' if classic else '1.13.2'))
        case = fixture['cases'][release['case']]
        raw = {'positions': case['positions'], 'bed_faces': [[index, *face] for index, face in enumerate(case['facings'])], 'rows': []}
        for row in case['rows']:
            transitions = [{**step, 'state': {'position': step['position'], 'box': step['box']}, 'shared_flags': step['flags']} for step in row['transitions']]
            raw['rows'].append({**row, 'transitions': transitions})
        return release, raw

    def size_phases(self) -> tuple[dict, dict]:
        path = Path(__file__).resolve().parents[2] / 'tests/Umpk.Client.Tests/Fixtures/LegacyPlayerSizePhase.native.json.gz'
        fixture = json.loads(gzip.decompress(path.read_bytes()))
        release = next(row for row in fixture['releases'] if row['version'] == '1.13.2')
        return release, copy.deepcopy(fixture['cases'][release['case']]) | {'flag_mask': 146}

    def test_complete_native_lifecycle_matrices_are_preserved(self) -> None:
        for classic in [True, False]:
            with self.subTest(classic=classic):
                receipt, raw = self.lifecycle(classic)
                reduced = reduce_release(receipt, raw)
                self.assertEqual(144 if classic else 288, len(reduced['rows']))

    def test_duplicate_lifecycle_case_is_rejected(self) -> None:
        receipt, raw = self.lifecycle()
        raw['rows'][-1] = raw['rows'][0]
        with self.assertRaisesRegex(ValueError, 'Duplicate'):
            reduce_release(receipt, raw)

    def test_missing_lifecycle_case_is_rejected(self) -> None:
        receipt, raw = self.lifecycle()
        raw['rows'].pop()
        with self.assertRaisesRegex(ValueError, 'matrix is incomplete'):
            reduce_release(receipt, raw)

    def test_missing_native_callback_is_rejected(self) -> None:
        receipt, raw = self.lifecycle()
        raw['rows'][0]['transitions'].pop()
        with self.assertRaisesRegex(ValueError, 'callback sequence'):
            reduce_release(receipt, raw)

    def test_missing_facing_is_rejected(self) -> None:
        receipt, raw = self.lifecycle()
        raw['bed_faces'][1] = raw['bed_faces'][0]
        with self.assertRaisesRegex(ValueError, 'facings are incomplete'):
            reduce_release(receipt, raw)

    def test_complete_native_size_matrix_is_preserved(self) -> None:
        receipt, raw = self.size_phases()
        self.assertEqual(3072, len(reduce_size_release(receipt, raw)['rows']))

    def test_missing_size_phase_is_rejected(self) -> None:
        receipt, raw = self.size_phases()
        raw['rows'].pop()
        with self.assertRaisesRegex(ValueError, 'matrix is incomplete'):
            reduce_size_release(receipt, raw)

    def test_duplicate_size_phase_is_rejected(self) -> None:
        receipt, raw = self.size_phases()
        raw['rows'][-1] = raw['rows'][0]
        with self.assertRaisesRegex(ValueError, 'Duplicate'):
            reduce_size_release(receipt, raw)

    def test_a_replaced_lifecycle_callback_is_rejected(self) -> None:
        contract = {'entity': 'NativeEntity', 'player': 'NativePlayer'}
        raw = {'method_owners': {'sleep': 'OwnedAdapter', 'wake': 'NativePlayer', 'status': 'NativePlayer', 'size_phase': 'absent', 'set_position': 'NativeEntity'}}
        with self.assertRaisesRegex(ValueError, 'callback owners differ'):
            validate_owners(contract, raw, classic=True)

    def test_a_replaced_size_phase_is_rejected(self) -> None:
        contract = {'entity': 'NativeEntity', 'player': 'NativePlayer'}
        raw = {'method_owners': {'setSize': 'NativeEntity', 'sizePhase': 'OwnedAdapter', 'setPosition': 'NativeEntity', 'setFlags': 'NativeEntity', 'getBoundingBox': 'NativeEntity'}}
        with self.assertRaisesRegex(ValueError, 'callback owners differ'):
            validate_owners(contract, raw, size_phase=True)


if __name__ == '__main__':
    unittest.main()
