"""Reject uncertain legacy building flags instead of inventing item geometry."""

import copy
import json
from pathlib import Path
import unittest


class LegacyItemObstructionTests(unittest.TestCase):
    def native_measurements(self) -> list[dict]:
        path = Path(__file__).parent / "tests/fixtures/legacy-item-obstruction-bindings.json"
        return [row["native"] for row in json.loads(path.read_text(encoding="utf-8"))]

    def test_native_bindings_cover_each_legacy_release(self) -> None:
        from extract_legacy_item_obstruction import item_rules

        for raw in self.native_measurements():
            with self.subTest(version=raw["version"]):
                self.assertEqual({"minecraft:item": 1, "minecraft:villager": 0}, item_rules(raw, {"minecraft:item", "minecraft:villager"}))

    def test_aliases_must_share_the_protocol_and_keep_distinct_measurements(self) -> None:
        from extract_legacy_item_obstruction import item_alias_rules

        aliases = [row for row in self.native_measurements() if row["protocol"] == 316]
        entities = {"minecraft:item", "minecraft:villager"}
        self.assertEqual({"minecraft:item": 1, "minecraft:villager": 0}, item_alias_rules(aliases, entities))
        for rows in ([], [aliases[0], aliases[0]], [aliases[0], self.measurement()]):
            with self.assertRaises(ValueError):
                item_alias_rules(rows, entities)
        aliases[1]["rows"][0]["blocks_building"] = True
        with self.assertRaises(ValueError):
            item_alias_rules(aliases, entities)

    def test_flattening_does_not_reuse_the_earlier_building_flag(self) -> None:
        from extract_legacy_item_obstruction import item_rules

        for raw in self.native_measurements():
            if raw["protocol"] >= 393:
                self.assertEqual("j", raw["field_name"])
                raw["field_name"] = "i"
                with self.assertRaises(ValueError):
                    item_rules(raw, {"minecraft:item", "minecraft:villager"})

    def measurement(self) -> dict:
        return {"protocol": 210, "version": "1.10.2", "entity": "minecraft:item", "class": "yk", "field_owner": "rw", "field_name": "i", "rows": [{"constructor": "default", "blocks_building": False}, {"constructor": "network_xyz", "blocks_building": False}], "origins": {name: "original-client.jar" for name in ("yk", "rw", "aid")}}

    def test_only_the_measured_item_rule_is_promoted(self) -> None:
        from extract_legacy_item_obstruction import item_rules

        self.assertEqual({"minecraft:item": 1, "minecraft:villager": 0}, item_rules(self.measurement(), {"minecraft:item", "minecraft:villager"}))

    def test_1_11_uses_its_own_audited_class_binding(self) -> None:
        from extract_legacy_item_obstruction import item_rules

        raw = self.measurement()
        raw.update(protocol=315, version="1.11", **{"class": "zi", "field_owner": "sm", "origins": {name: "original-1.11-client.jar" for name in ("zi", "sm", "ajq")}})
        self.assertEqual({"minecraft:item": 1, "minecraft:villager": 0}, item_rules(raw, {"minecraft:item", "minecraft:villager"}))

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
