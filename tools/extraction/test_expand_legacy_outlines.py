import unittest

from expand_legacy_outlines import expand_metadata


class LegacyMetadataExpansionTests(unittest.TestCase):
    def fixture(self) -> tuple[dict, dict, dict]:
        blocks = {"identity": "legacy", "blocks": [{"block_id": 1, "material": "stone"}]}
        shape = {"block": "minecraft:stone", "outline": {"boxes": [[0, 0, 0, 1, 1, 1]], "offset": "none"}, "trace_selection": "nearest_from_start"}
        aliases = {"block_count": 1, "aliases": {str(16 + meta): 16 for meta in range(16)}, "decode_failures": {}, "canonical_states": {"16": "minecraft:stone"}}
        return {"16": shape}, aliases, blocks

    def test_expands_all_metadata_through_the_native_decoder(self) -> None:
        native, aliases, blocks = self.fixture()
        actual = expand_metadata(native, aliases, blocks)
        self.assertEqual(16, len(actual["states"]))
        self.assertEqual(16, actual["states"]["31"]["canonical_state"])
        self.assertEqual(native["16"]["outline"], actual["states"]["31"]["outline"])

    def test_native_rejections_remain_explicit(self) -> None:
        native, aliases, blocks = self.fixture()
        del aliases["aliases"]["31"]
        aliases["decode_failures"]["31"] = ["minecraft:stone", "java.lang.IllegalArgumentException", "invalid property"]
        actual = expand_metadata(native, aliases, blocks)
        self.assertEqual(15, len(actual["states"]))
        self.assertEqual(aliases["decode_failures"], actual["rejected_metadata"])

    def test_refuses_incomplete_wire_metadata_coverage(self) -> None:
        native, aliases, blocks = self.fixture()
        del aliases["aliases"]["31"]
        with self.assertRaisesRegex(ValueError, "coverage"):
            expand_metadata(native, aliases, blocks)

    def test_refuses_an_alias_to_an_unmeasured_native_state(self) -> None:
        native, aliases, blocks = self.fixture()
        aliases["aliases"]["31"] = 17
        aliases["canonical_states"]["17"] = "minecraft:stone"
        with self.assertRaisesRegex(ValueError, "unmeasured"):
            expand_metadata(native, aliases, blocks)

    def test_refuses_an_alias_to_another_block(self) -> None:
        native, aliases, blocks = self.fixture()
        aliases["aliases"]["31"] = 32
        aliases["canonical_states"]["32"] = "minecraft:dirt"
        native["32"] = {"block": "minecraft:dirt"}
        with self.assertRaisesRegex(ValueError, "ownership"):
            expand_metadata(native, aliases, blocks)

    def test_refuses_wrong_native_registry_names(self) -> None:
        native, aliases, blocks = self.fixture()
        native["16"]["block"] = "minecraft:dirt"
        with self.assertRaisesRegex(ValueError, "name"):
            expand_metadata(native, aliases, blocks)

    def test_refuses_a_state_both_accepted_and_rejected(self) -> None:
        native, aliases, blocks = self.fixture()
        aliases["decode_failures"]["31"] = ["minecraft:stone", "java.lang.IllegalArgumentException", "invalid property"]
        with self.assertRaisesRegex(ValueError, "both"):
            expand_metadata(native, aliases, blocks)


if __name__ == "__main__":
    unittest.main()
