import gzip
import json
from pathlib import Path
import unittest

from extract_legacy_player_wake import reduce_release


class LegacyPlayerWakeImportTests(unittest.TestCase):
    def inputs(self, protocol: int = 47) -> dict:
        path = Path(__file__).resolve().parents[2] / 'tests/Umpk.Client.Tests/Fixtures/LegacyPlayerWake.native.json.gz'
        fixture = json.loads(gzip.decompress(path.read_bytes()))
        release = next(row for row in fixture['releases'] if row['protocol'] == protocol)
        case = fixture['cases'][release['case']]
        raw = {'positions': case['positions'], 'bed_faces': [[index, *face] for index, face in enumerate(case['facings'])], 'rows': []}
        for row in case['rows']:
            wake = {'operation': 'wake_with_intact_bed', 'sleeping': False, 'block_writes': [[*row['bed'], 4]], 'native_exit_cell': row['exit'], 'exit_query_reads': [[cell[0], cell[1] - 1, cell[2]] for cell in row['candidates']], 'state': {'position': row['wake_position'], 'box': row['box']}, 'size': row['size']}
            raw['rows'].append({**row, 'transitions': [wake]})
        return raw

    def test_complete_original_wake_matrices_are_preserved(self) -> None:
        for protocol, count in [(47, 480), (404, 960)]:
            with self.subTest(protocol=protocol):
                self.assertEqual(count, len(reduce_release(protocol, self.inputs(protocol))['rows']))

    def test_duplicate_wake_case_is_rejected(self) -> None:
        raw = self.inputs()
        raw['rows'][-1] = raw['rows'][0]
        with self.assertRaisesRegex(ValueError, 'Duplicate'):
            reduce_release(47, raw)

    def test_missing_wake_case_is_rejected(self) -> None:
        raw = self.inputs()
        raw['rows'].pop()
        with self.assertRaisesRegex(ValueError, 'matrix is incomplete'):
            reduce_release(47, raw)

    def test_changed_occupied_state_flags_are_rejected(self) -> None:
        raw = self.inputs()
        raw['rows'][0]['transitions'][0]['block_writes'][0][-1] = 3
        with self.assertRaisesRegex(ValueError, 'occupied-state write'):
            reduce_release(47, raw)

    def test_missing_original_wake_callback_is_rejected(self) -> None:
        raw = self.inputs()
        raw['rows'][0]['transitions'].clear()
        with self.assertRaisesRegex(ValueError, 'callback sequence'):
            reduce_release(47, raw)

    def test_more_than_eighteen_candidate_evaluations_are_rejected(self) -> None:
        raw = self.inputs()
        reads = raw['rows'][0]['transitions'][0]['exit_query_reads']
        reads.append(reads[0])
        with self.assertRaisesRegex(ValueError, 'candidate scan'):
            reduce_release(47, raw)


if __name__ == '__main__':
    unittest.main()
