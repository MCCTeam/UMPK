import unittest

from extract_item_attack import canonical_items, fold_aliases


class ItemAttackExtractionTests(unittest.TestCase):
    def test_absence_and_native_components_are_preserved(self):
        native = {
            'minecraft:stone': {},
            'minecraft:test': {'piercing_weapon': {'sound': 'minecraft:item.spear.attack'}, 'minimum_attack_charge': 1.0},
        }
        result = canonical_items(native, set(native))
        self.assertEqual({'piercing_weapon': None, 'minimum_attack_charge': None}, result['minecraft:stone'])
        self.assertEqual(native['minecraft:test'], result['minecraft:test'])

    def test_missing_or_unknown_items_are_rejected(self):
        for names in [{'minecraft:stone', 'minecraft:missing'}, set()]:
            with self.subTest(names=names), self.assertRaises(ValueError):
                canonical_items({'minecraft:stone': {}}, names)

    def test_unknown_measured_fields_are_rejected(self):
        with self.assertRaises(ValueError):
            canonical_items({'minecraft:stone': {'unrecognized': True}}, {'minecraft:stone'})

    def test_protocol_aliases_must_match(self):
        first = canonical_items({'minecraft:stone': {}}, {'minecraft:stone'})
        self.assertEqual(first, fold_aliases([first, first]))
        changed = canonical_items({'minecraft:stone': {'minimum_attack_charge': 1.0}}, {'minecraft:stone'})
        with self.assertRaises(ValueError):
            fold_aliases([first, changed])


if __name__ == '__main__':
    unittest.main()
