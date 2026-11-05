import json
from pathlib import Path
import unittest

from extract_legacy_bed_occupied import reduce_states


class LegacyBedOccupiedImportTests(unittest.TestCase):
    def inputs(self) -> tuple[dict, list[list[int]]]:
        data = Path(__file__).resolve().parents[2] / 'data/java/47/bed-facings.json'
        beds = json.loads(data.read_text())['states']
        rows = []
        for state, _ in beds:
            after = state & ~4
            rows.append({'wire_state': state, 'decoded_native_state': (state >> 4) | (((state & 15) & ~4 if state & 8 else state & 15) << 12), 'after_wire_state': after, 'after_native_state': (after >> 4) | ((after & 15) << 12), 'after_occupied': False, 'other_properties_equal': True})
        return {'rows': rows}, beds

    def test_complete_native_transitions_preserve_raw_metadata_inputs(self) -> None:
        raw, beds = self.inputs()
        self.assertEqual([[state, state & ~4] for state, _ in beds], reduce_states(47, raw, beds))

    def test_missing_raw_alias_is_rejected(self) -> None:
        raw, beds = self.inputs()
        raw['rows'].pop()
        with self.assertRaisesRegex(ValueError, 'coverage'):
            reduce_states(47, raw, beds)

    def test_duplicate_input_is_rejected(self) -> None:
        raw, beds = self.inputs()
        raw['rows'][-1] = raw['rows'][0]
        with self.assertRaisesRegex(ValueError, 'repeated'):
            reduce_states(47, raw, beds)

    def test_occupied_result_must_be_false(self) -> None:
        raw, beds = self.inputs()
        raw['rows'][0]['after_occupied'] = True
        with self.assertRaisesRegex(ValueError, 'occupied'):
            reduce_states(47, raw, beds)

    def test_another_changed_property_is_rejected(self) -> None:
        raw, beds = self.inputs()
        raw['rows'][0]['other_properties_equal'] = False
        with self.assertRaisesRegex(ValueError, 'property'):
            reduce_states(47, raw, beds)

    def test_changed_block_identity_is_rejected(self) -> None:
        raw, beds = self.inputs()
        raw['rows'][0]['decoded_native_state'] ^= 1
        with self.assertRaisesRegex(ValueError, 'identity'):
            reduce_states(47, raw, beds)

    def test_native_and_wire_outputs_must_agree(self) -> None:
        raw, beds = self.inputs()
        raw['rows'][0]['after_wire_state'] += 1
        with self.assertRaisesRegex(ValueError, 'output'):
            reduce_states(47, raw, beds)


if __name__ == '__main__':
    unittest.main()
