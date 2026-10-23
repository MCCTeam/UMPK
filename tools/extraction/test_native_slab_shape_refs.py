"""Focused guards for the finite native slab reference supplement."""
import copy
import unittest

import shape_extractor as shapes


class NativeSlabReferenceTests(unittest.TestCase):
    def setUp(self) -> None:
        self.pool = {'shapes': [[], [[0, .5, 0, 1, 1, 1]], [[0, 0, 0, 1, .5, 1]], [[0, 0, 0, 1, 1, 1]]]}
        self.refs = {'_provenance': {'kind': 'curated-shape-bootstrap', 'unmatched_blocks': ['white_wool_slab', 'unrelated']}, 'collision': {'minecraft:existing': [3]}}
        self.blocks = {'blocks': [{'name': 'minecraft:white_wool_slab', 'min_state': 0, 'max_state': 5, 'num_states': 6, 'default_state': 3, 'properties': ['type', 'waterlogged']}]}
        self.native = {'schema': 1, 'cell_order': '(x * y_size + y) * z_size + z', 'states': {str(i): i // 2 for i in range(6)}, 'groups': [
            {'canonical_full_block': False, 'planes': [[0, 1], [0, .5, 1], [0, 1]], 'cells': [1], 'boxes': [[0, .5, 0, 1, 1, 1]]},
            {'canonical_full_block': False, 'planes': [[0, 1], [0, .5, 1], [0, 1]], 'cells': [0], 'boxes': [[0, 0, 0, 1, .5, 1]]},
            {'canonical_full_block': True, 'planes': [[0, 1], [0, 1], [0, 1]], 'cells': [0], 'boxes': [[0, 0, 0, 1, 1, 1]]},
        ]}
        self.selected = ['minecraft:white_wool_slab']

    def fold(self) -> dict:
        return shapes._fold_native_slab_refs(self.pool, self.refs, self.blocks, self.native, self.selected)

    def test_native_refs_reuse_pool_and_preserve_other_inputs(self) -> None:
        before = copy.deepcopy((self.pool, self.refs, self.blocks, self.native))
        result = self.fold()
        self.assertEqual([1, 1, 2, 2, 3, 3], result['collision']['minecraft:white_wool_slab'])
        self.assertEqual([3], result['collision']['minecraft:existing'])
        self.assertEqual(['unrelated'], result['_provenance']['unmatched_blocks'])
        self.assertEqual(before, (self.pool, self.refs, self.blocks, self.native))

    def test_repeated_correct_fold_is_idempotent(self) -> None:
        result = self.fold()
        self.refs = copy.deepcopy(result)
        self.assertEqual(result, self.fold())

    def test_changed_existing_reference_is_rejected(self) -> None:
        for previous in ([3] * 6, None):
            with self.subTest(previous=previous):
                self.refs['collision']['minecraft:white_wool_slab'] = previous
                with self.assertRaisesRegex(ValueError, 'existing slab reference'):
                    self.fold()

    def test_wrong_native_half_is_rejected(self) -> None:
        self.native['states']['0'] = 1
        with self.assertRaisesRegex(ValueError, 'native slab geometry'):
            self.fold()

    def test_double_without_native_singleton_identity_is_rejected(self) -> None:
        self.native['groups'][2]['canonical_full_block'] = False
        with self.assertRaisesRegex(ValueError, 'native slab geometry'):
            self.fold()

    def test_missing_original_state_is_rejected(self) -> None:
        del self.native['states']['5']
        with self.assertRaisesRegex(ValueError, 'state ids'):
            self.fold()

    def test_mismatched_state_schema_is_rejected(self) -> None:
        self.blocks['blocks'][0]['properties'] = ['waterlogged', 'type']
        with self.assertRaisesRegex(ValueError, 'state schema'):
            self.fold()

    def test_wrong_native_default_state_is_rejected(self) -> None:
        self.blocks['blocks'][0]['default_state'] = 1
        with self.assertRaisesRegex(ValueError, 'state schema'):
            self.fold()

    def test_missing_block_identity_is_rejected(self) -> None:
        self.blocks['blocks'][0]['name'] = 'minecraft:other'
        with self.assertRaisesRegex(ValueError, 'Missing slab identity'):
            self.fold()

    def test_unknown_pool_shape_cannot_be_added(self) -> None:
        del self.pool['shapes'][2]
        with self.assertRaisesRegex(ValueError, 'existing shape pool'):
            self.fold()

    def test_duplicate_selected_identity_is_rejected(self) -> None:
        self.selected *= 2
        with self.assertRaisesRegex(ValueError, 'unique slab identities'):
            self.fold()

    def test_a_present_but_incomplete_ref_is_not_silently_repaired(self) -> None:
        self.refs['collision']['minecraft:white_wool_slab'] = [1]
        with self.assertRaisesRegex(ValueError, 'existing slab reference'):
            self.fold()


if __name__ == '__main__':
    unittest.main()
