import json
from pathlib import Path
import unittest

from extract_legacy_bed_facings import reduce_states


class LegacyBedFacingImportTests(unittest.TestCase):
    def inputs(self, protocol: int = 47) -> tuple[dict, dict]:
        data = Path(__file__).resolve().parents[2] / 'data/java' / str(protocol)
        blocks = json.loads((data / 'blocks.json').read_text())
        states = json.loads((data / 'bed-facings.json').read_text())['states']
        directions = {2: (0, -1), 3: (0, 1), 4: (-1, 0), 5: (1, 0)}
        rows = []
        counts: dict[str, int] = {}
        for state, facing in states:
            name = 'minecraft:bed' if protocol < 393 else next(block['name'] for block in blocks['blocks'] if block['min_state'] <= state <= block['max_state'])
            row = [name, state, *directions[facing]]
            if protocol < 393:
                row.append((state >> 4) | ((state & 15) << 12))
            rows.append(row)
            counts[name] = counts.get(name, 0) + 1
        return {'rows': rows, 'bed_counts': counts}, blocks

    def test_complete_legacy_and_flattened_states_are_preserved(self) -> None:
        for protocol, count in [(47, 16), (404, 256)]:
            with self.subTest(protocol=protocol):
                raw, blocks = self.inputs(protocol)
                self.assertEqual(count, len(reduce_states(protocol, raw, blocks)))

    def test_missing_raw_metadata_alias_is_rejected(self) -> None:
        raw, blocks = self.inputs()
        raw['rows'].pop()
        with self.assertRaisesRegex(ValueError, 'matrix is incomplete'):
            reduce_states(47, raw, blocks)

    def test_duplicate_state_is_rejected(self) -> None:
        raw, blocks = self.inputs()
        raw['rows'][-1] = raw['rows'][0]
        with self.assertRaisesRegex(ValueError, 'repeated'):
            reduce_states(47, raw, blocks)

    def test_nonhorizontal_native_direction_is_rejected(self) -> None:
        raw, blocks = self.inputs()
        raw['rows'][0][2:4] = [1, 1]
        with self.assertRaisesRegex(ValueError, 'nonhorizontal'):
            reduce_states(47, raw, blocks)

    def test_decoded_legacy_state_cannot_change_block_identity(self) -> None:
        raw, blocks = self.inputs()
        raw['rows'][0][4] ^= 1
        with self.assertRaisesRegex(ValueError, 'block identity'):
            reduce_states(47, raw, blocks)

    def test_an_unknown_flattened_state_is_rejected(self) -> None:
        raw, blocks = self.inputs(404)
        raw['rows'][0][1] = 0
        with self.assertRaisesRegex(ValueError, 'unknown'):
            reduce_states(404, raw, blocks)

    def test_unbalanced_facing_coverage_is_rejected(self) -> None:
        raw, blocks = self.inputs(404)
        raw['rows'][0][2:4] = [-1, 0]
        with self.assertRaisesRegex(ValueError, 'matrix is incomplete'):
            reduce_states(404, raw, blocks)


if __name__ == '__main__':
    unittest.main()
