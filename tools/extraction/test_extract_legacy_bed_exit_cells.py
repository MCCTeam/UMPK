import json
from pathlib import Path
import unittest

from extract_legacy_bed_exit_cells import reduce_states


class LegacyBedExitCellImportTests(unittest.TestCase):
    def inputs(self, protocol: int = 47) -> tuple[dict, dict]:
        root = Path(__file__).resolve().parents[2] / 'data/java' / str(protocol)
        data = json.loads((root / 'bed-exit-cells.json').read_text())
        blocks = json.loads((root / 'blocks.json').read_text())
        rows = []
        for first, last, flags in data['ranges']:
            for state in range(first, last + 1):
                raw_flags = (flags & 1) | (6 if flags & 2 else 0)
                traces = [[[0, 63, 0]] + ([[0, 64, 0], [0, 65, 0]] if flags & 1 else []), [[0, 63, 0], [0, 64, 0]] + ([[0, 65, 0]] if flags & 2 else []), [[0, 63, 0], [0, 64, 0], [0, 65, 0]]]
                row = [state, raw_flags, traces]
                if protocol < 393:
                    row.append((state >> 4) | ((state & 15) << 12))
                rows.append(row)
        rejected = [[state, 'java.lang.IllegalArgumentException', 'Native metadata decode rejection'] for state in data['rejected_states']]
        return {'rows': rows, 'rejected_metadata': rejected}, blocks

    def test_complete_valid_and_rejected_registry_partition_is_preserved(self) -> None:
        for protocol in [47, 404]:
            with self.subTest(protocol=protocol):
                raw, blocks = self.inputs(protocol)
                reduced = reduce_states(protocol, raw, blocks)
                self.assertEqual(len(raw['rows']), reduced['state_count'])
                self.assertEqual(len(raw['rejected_metadata']), len(reduced['rejected_states']))

    def test_missing_native_rejection_cannot_be_promoted_to_known(self) -> None:
        raw, blocks = self.inputs()
        raw['rejected_metadata'].pop()
        with self.assertRaisesRegex(ValueError, 'do not cover'):
            reduce_states(47, raw, blocks)

    def test_inconsistent_feet_and_head_material_results_are_rejected(self) -> None:
        raw, blocks = self.inputs()
        raw['rows'][0][1] = 2
        with self.assertRaisesRegex(ValueError, 'inconsistent'):
            reduce_states(47, raw, blocks)

    def test_neighbor_dependent_read_is_rejected(self) -> None:
        raw, blocks = self.inputs()
        raw['rows'][0][2][0].append([1, 63, 0])
        with self.assertRaisesRegex(ValueError, 'unexpected world cells'):
            reduce_states(47, raw, blocks)

    def test_known_and_rejected_states_cannot_overlap(self) -> None:
        raw, blocks = self.inputs()
        raw['rejected_metadata'][0][0] = raw['rows'][0][0]
        with self.assertRaisesRegex(ValueError, 'rejection is inconsistent'):
            reduce_states(47, raw, blocks)

    def test_an_unexpected_native_exception_cannot_be_ignored(self) -> None:
        raw, blocks = self.inputs()
        raw['rejected_metadata'][0][1] = 'java.lang.NullPointerException'
        with self.assertRaisesRegex(ValueError, 'rejection is inconsistent'):
            reduce_states(47, raw, blocks)

    def test_duplicate_native_state_is_rejected(self) -> None:
        raw, blocks = self.inputs()
        raw['rows'][-1] = raw['rows'][0]
        with self.assertRaisesRegex(ValueError, 'repeated'):
            reduce_states(47, raw, blocks)

    def test_native_decoded_block_identity_is_checked(self) -> None:
        raw, blocks = self.inputs()
        raw['rows'][0][3] ^= 1
        with self.assertRaisesRegex(ValueError, 'block identity'):
            reduce_states(47, raw, blocks)


if __name__ == '__main__':
    unittest.main()
