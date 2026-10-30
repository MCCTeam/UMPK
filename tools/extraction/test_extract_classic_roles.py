"""Require complete aliases and verified class identities for classic farm bounds."""

import copy
import gzip
import json
from pathlib import Path
import unittest


class ClassicRoleExtractionTests(unittest.TestCase):
    @staticmethod
    def records() -> list[dict]:
        path = Path(__file__).parents[2] / "tests/Umpk.Client.Tests/Fixtures/ClassicFarmGeometry.native.json.gz"
        document = json.loads(gzip.decompress(path.read_bytes()))
        return [{"version": row["version"], "entity": row["entity"], "protocol": row["protocol"], "raw": copy.deepcopy(document["cases"][row["case"]])} for row in document["releases"]]

    def test_native_roles_cover_every_alias_and_keep_distinct_dimensions(self) -> None:
        from extract_classic_entity_geometry import group_classic_roles
        from extract_entity_geometry import classic_villager_profile

        records = self.records()
        groups = group_classic_roles(records, {row["version"] for row in records})
        self.assertEqual({"minecraft:cow", "minecraft:pig", "minecraft:sheep", "minecraft:chicken"}, set(groups))
        expected = {"cow": [.9, 1.3, .45, .65], "pig": [.9, .9, .45, .45], "sheep": [.9, 1.3, .45, .65], "chicken": [.4, .7, .2, .35]}
        for entity, rows in groups.items():
            with self.subTest(entity=entity):
                self.assertEqual(10, len(rows))
                profile = classic_villager_profile(rows[0]["raw"])
                self.assertEqual(expected[entity.split(":")[1]], profile["variants"][0][2:4] + profile["variants"][1][2:4])
                self.assertEqual("living_tick", profile["refresh_timing"])

    def test_incomplete_duplicate_or_mixed_role_evidence_is_rejected(self) -> None:
        from extract_classic_entity_geometry import group_classic_roles

        for defect in ("missing-alias", "duplicate", "missing-type", "protocol", "protocol-type", "unknown-type", "raw-identity", "phase"):
            with self.subTest(defect=defect):
                records = self.records()
                expected = {row["version"] for row in records}
                if defect == "missing-alias": records.pop()
                if defect == "duplicate": records.append(copy.deepcopy(records[0]))
                if defect == "missing-type": records = [row for row in records if row["entity"] != "minecraft:cow"]
                if defect == "protocol": records[0]["protocol"] = 107
                if defect == "protocol-type": records[0]["protocol"] = 47.0
                if defect == "unknown-type": records[0]["entity"] = "minecraft:horse"
                if defect == "raw-identity": records[0]["raw"]["entity"] = "minecraft:cow"
                if defect == "phase": records[0]["raw"]["rows"][0]["after_phase"][0] += .25
                with self.assertRaises(ValueError):
                    group_classic_roles(records, expected)

    @staticmethod
    def identity_inputs() -> tuple:
        names = {"root": "Entity", "living": "Living", "world": "World", "villager": "Cow", "ageable": "Ageable", "data": "Data", "entity_registry": "Registry", "hierarchy": ["Cow", "Animal", "Ageable"]}
        origins = {name: "client.jar" for name in ("Entity", "Living", "World", "Cow", "Ageable", "Data", "Animal")}
        raw = {"entity": "minecraft:cow", "entity_class": "Cow", "age_phase_owner": "Ageable", "child_predicate_owner": "Ageable", "origins": origins}
        receipt = {"entity": "minecraft:cow", "registry_class": "Cow", "registry_numeric_id": 92, "helper_source_sha256": "a" * 64, "helper_class_sha256": "b" * 64}
        sources = {name + ".java": "" for name in origins}
        sources.update({"Cow.java": "public class Cow extends Animal {}", "Animal.java": "public abstract class Animal extends Ageable {}", "Registry.java": 'a(Cow.class, "Cow", 92, 1, 2);'})
        return raw, receipt, names, sources

    def test_registry_identity_and_original_age_owners_are_required(self) -> None:
        from extract_entity_geometry import verify_classic_role_identity

        raw, receipt, binding, sources = self.identity_inputs()
        verify_classic_role_identity(raw, receipt, binding, sources, "a" * 64, "b" * 64)

    def test_changed_class_helpers_owners_or_hierarchy_are_rejected(self) -> None:
        from extract_entity_geometry import verify_classic_role_identity

        for defect in ("entity", "class", "receipt-class", "registry-id", "registry-source", "phase-owner", "predicate-owner", "origin", "hierarchy", "source", "helper-source", "helper-class"):
            with self.subTest(defect=defect):
                raw, receipt, binding, sources = self.identity_inputs()
                if defect == "entity": raw["entity"] = "minecraft:pig"
                if defect == "class": raw["entity_class"] = "Other"
                if defect == "receipt-class": receipt["registry_class"] = "Other"
                if defect == "registry-id": receipt["registry_numeric_id"] = 93
                if defect == "registry-source": sources["Registry.java"] = 'a(Other.class, "Cow", 92);'
                if defect == "phase-owner": raw["age_phase_owner"] = "Cow"
                if defect == "predicate-owner": raw["child_predicate_owner"] = "Cow"
                if defect == "origin": raw["origins"].pop("Animal")
                if defect == "hierarchy": binding["hierarchy"] = ["Cow", "Ageable"]
                if defect == "source": sources.pop("Cow.java")
                if defect == "helper-source": receipt["helper_source_sha256"] = "c" * 64
                if defect == "helper-class": receipt["helper_class_sha256"] = "c" * 64
                with self.assertRaises(ValueError):
                    verify_classic_role_identity(raw, receipt, binding, sources, "a" * 64, "b" * 64)
