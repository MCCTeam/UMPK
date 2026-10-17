"""Reject uncertain legacy building flags instead of inventing item geometry."""

import copy
import unittest


class LegacyItemObstructionTests(unittest.TestCase):
    def measurement(self) -> dict:
        return {"protocol": 210, "version": "1.10.2", "entity": "minecraft:item", "class": "yk", "field_owner": "rw", "field_name": "i", "rows": [{"constructor": "default", "blocks_building": False}, {"constructor": "network_xyz", "blocks_building": False}], "origins": {name: "original-client.jar" for name in ("yk", "rw", "aid")}}

    def test_only_the_measured_item_rule_is_promoted(self) -> None:
        from extract_legacy_item_obstruction import item_rules

        self.assertEqual({"minecraft:item": 1, "minecraft:villager": 0}, item_rules(self.measurement(), {"minecraft:item", "minecraft:villager"}))

    def test_missing_constructors_wrong_types_and_origins_are_rejected(self) -> None:
        from extract_legacy_item_obstruction import item_rules

        for defect in ("protocol", "class", "field", "missing", "duplicate", "flag", "flag-type", "origin"):
            with self.subTest(defect=defect):
                raw = copy.deepcopy(self.measurement())
                if defect == "protocol": raw["protocol"] = 47
                if defect == "class": raw["class"] = "unmeasured"
                if defect == "field": raw["field_name"] = "unknown"
                if defect == "missing": raw["rows"].pop()
                if defect == "duplicate": raw["rows"].append(copy.deepcopy(raw["rows"][0]))
                if defect == "flag": raw["rows"][1]["blocks_building"] = True
                if defect == "flag-type": raw["rows"][1]["blocks_building"] = 0
                if defect == "origin": raw["origins"]["yk"] = "adapter.jar"
                with self.assertRaises(ValueError):
                    item_rules(raw, {"minecraft:item", "minecraft:villager"})
