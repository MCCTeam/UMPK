"""Protect the complete Boolean-age farm cohort and its native identities."""

import copy
import gzip
import json
from pathlib import Path
import unittest


class LegacyAgeableGeometryTests(unittest.TestCase):
    @staticmethod
    def records() -> list[dict]:
        root = Path(__file__).parents[2]
        native = json.loads(gzip.decompress((root / "tests/Umpk.Client.Tests/Fixtures/LegacyFarmGeometry.native.json.gz").read_bytes()))
        return [{key: row[key] for key in ("version", "protocol", "entity")} | {"raw": copy.deepcopy(native["cases"][row["case"]])} for row in native["releases"]]

    @staticmethod
    def catalog() -> dict[str, int]:
        rows = json.loads((Path(__file__).parents[2] / "data/java/versions.json").read_text())["versions"]
        return {row["name"]: row["protocol"] for row in rows if 107 <= row["protocol"] <= 404}

    def test_all_native_aliases_keep_their_age_callback_and_dimension_boundaries(self) -> None:
        from extract_legacy_ageable_geometry import ageable_profile, group_ageable_roles

        records = self.records()
        groups = group_ageable_roles(records, self.catalog())
        self.assertEqual(68, len(records))
        self.assertEqual(13, len(groups))
        for protocol, entities in groups.items():
            self.assertEqual({"minecraft:cow", "minecraft:pig", "minecraft:sheep", "minecraft:chicken"}, set(entities))
            for entity, rows in entities.items():
                profile = ageable_profile(rows[0]["raw"], protocol)
                self.assertEqual([11 if protocol <= 110 else 12, "boolean", 1, 0], profile["selector"])
                self.assertEqual("retain_minimum" if protocol <= 210 else "retain_minimum_on_growth", profile["refresh"])
                self.assertNotIn("refresh_timing", profile)
                if entity == "minecraft:cow":
                    self.assertEqual([[0, 0, .9, 1.4, False, True], [1, 0, .45, .7, False, True]], profile["variants"])

    def test_incomplete_duplicate_mixed_and_corrupt_native_cohorts_are_rejected(self) -> None:
        from extract_legacy_ageable_geometry import group_ageable_roles

        for defect in ("missing-alias", "duplicate", "missing-type", "protocol", "protocol-type", "unknown-type", "raw-identity", "phase", "metadata-index", "child-value", "child-type", "missing-position"):
            with self.subTest(defect=defect):
                records = self.records()
                if defect == "missing-alias": records.pop()
                if defect == "duplicate": records.append(copy.deepcopy(records[0]))
                if defect == "missing-type": records = [row for row in records if row["entity"] != "minecraft:cow"]
                if defect == "protocol": records[0]["protocol"] = 47
                if defect == "protocol-type": records[0]["protocol"] = float(records[0]["protocol"])
                if defect == "unknown-type": records[0]["entity"] = "minecraft:horse"
                if defect == "raw-identity": records[0]["raw"]["entity"] = "minecraft:horse"
                if defect == "phase": records[0]["raw"]["rows"][0]["after_sync"][0] += .25
                if defect == "metadata-index": records[0]["raw"]["metadata_index"] = 12 if records[0]["raw"]["metadata_index"] == 11 else 11
                if defect == "child-value": records[0]["raw"]["rows"][0]["after_child"] = True
                if defect == "child-type": records[0]["raw"]["rows"][0]["after_child"] = 0
                if defect == "missing-position": records[0]["raw"]["rows"].pop()
                with self.assertRaises(ValueError):
                    group_ageable_roles(records, self.catalog())

    @staticmethod
    def identity_inputs(kind: str) -> tuple:
        binding = {"root": "Entity", "living": "Living", "world": "World", "villager": "Cow", "ageable": "Ageable", "accessor": "Accessor", "accessor_field": "BABY", "data": "Data", "data_field": "synched", "entity_registry": "Types", "hierarchy": ["Cow", "Animal", "Ageable"], "registry_owner": "Types", "registry_kind": kind, "registry_field": "REGISTRY", "registry_value_field": "COW", "registry_id_method": "id", "registry_key_method": "key", "child_predicate": "isBaby", "synced_callback": "changed"}
        required = {binding[key] for key in ("root", "living", "world", "ageable", "accessor", "data", "entity_registry", "registry_owner")} | set(binding["hierarchy"])
        origins = {name: "client.jar" for name in required}
        registry = {"kind": kind, "id": 9 if kind == "type-registry" else 92, "name": "Cow" if kind == "entity-list" else "minecraft:cow", "id_method": "id", "key_method": "key", "id_owner": "Types", "key_owner": "Types", "origins": {"Types": "client.jar"}}
        raw = {"entity": "minecraft:cow", "entity_class": "Cow", "child_predicate_owner": "Ageable", "metadata_callback_owner": "Ageable", "origins": origins, "registry": registry}
        receipt = {"entity": "minecraft:cow", "registry_class": "Cow", "registry_numeric_id": registry["id"], "helper_source_sha256": "a" * 64, "helper_class_sha256": "b" * 64, "binding": binding}
        sources = {name + ".java": "" for name in required}
        sources.update({"Cow.java": "public class Cow extends Animal {}", "Animal.java": "public abstract class Animal extends Ageable {}", "Ageable.java": "private static final Accessor<Boolean> BABY = Data.a(Ageable.class, Serializer.BOOL); public void changed(Accessor<?> value) { if (BABY.equals(value)) { this.resize(this.isBaby()); } super.changed(value); } public boolean isBaby() { return this.age() < 0; }"})
        sources["Types.java"] = {"entity-list": 'a(Cow.class, "Cow", 92, 1, 2);', "class-registry": 'public static final Registry<Key, Class<? extends Entity>> REGISTRY = new Registry(); a(92, "cow", Cow.class, "Cow");', "type-registry": 'public static final Types<Cow> COW = a("cow", Builder.a(Cow.class, Cow::new));'}[kind]
        return raw, receipt, binding, sources

    def test_original_registry_forms_and_flattened_ids_are_supported(self) -> None:
        from extract_legacy_ageable_geometry import verify_ageable_identity

        for kind in ("entity-list", "class-registry", "type-registry"):
            with self.subTest(kind=kind):
                raw, receipt, binding, sources = self.identity_inputs(kind)
                verify_ageable_identity(raw, receipt, binding, sources, "a" * 64, "b" * 64, raw["registry"]["id"])

    def test_unaudited_native_identities_sources_callbacks_and_helpers_are_rejected(self) -> None:
        from extract_legacy_ageable_geometry import verify_ageable_identity

        for kind in ("entity-list", "class-registry", "type-registry"):
            for defect in ("entity", "class", "receipt-class", "registry-id", "registry-name", "registry-kind", "registry-source", "callback-owner", "predicate-owner", "origin", "registry-origin", "hierarchy", "source", "helper-source", "helper-class", "selector-source", "callback-source", "conditional-super"):
                with self.subTest(kind=kind, defect=defect):
                    raw, receipt, binding, sources = self.identity_inputs(kind)
                    numeric = raw["registry"]["id"]
                    if defect == "entity": raw["entity"] = "minecraft:pig"
                    if defect == "class": raw["entity_class"] = "Other"
                    if defect == "receipt-class": receipt["registry_class"] = "Other"
                    if defect == "registry-id": raw["registry"]["id"] += 1
                    if defect == "registry-name": raw["registry"]["name"] = "minecraft:pig"
                    if defect == "registry-kind": raw["registry"]["kind"] = "other"
                    if defect == "registry-source": sources["Types.java"] = sources["Types.java"].replace("Cow.class", "Other.class")
                    if defect == "callback-owner": raw["metadata_callback_owner"] = "Cow"
                    if defect == "predicate-owner": raw["child_predicate_owner"] = "Cow"
                    if defect == "origin": raw["origins"].pop("Animal")
                    if defect == "registry-origin": raw["registry"]["origins"]["Types"] = "other.jar"
                    if defect == "hierarchy": binding["hierarchy"] = ["Cow", "Ageable"]
                    if defect == "source": sources.pop("Cow.java")
                    if defect == "helper-source": receipt["helper_source_sha256"] = "c" * 64
                    if defect == "helper-class": receipt["helper_class_sha256"] = "c" * 64
                    if defect == "selector-source": sources["Ageable.java"] = sources["Ageable.java"].replace("Accessor<Boolean>", "Accessor<Byte>")
                    if defect == "callback-source": sources["Ageable.java"] = sources["Ageable.java"].replace("this.resize(this.isBaby());", "")
                    if defect == "conditional-super": sources["Animal.java"] = "public abstract class Animal extends Ageable { public void changed(Accessor<?> value) { if (false) { super.changed(value); } } }"
                    with self.assertRaises(ValueError):
                        verify_ageable_identity(raw, receipt, binding, sources, "a" * 64, "b" * 64, numeric)

    def test_actor_callback_override_reaches_the_original_ageable_phase(self) -> None:
        from extract_legacy_ageable_geometry import verify_ageable_identity

        raw, receipt, binding, sources = self.identity_inputs("class-registry")
        sources["Cow.java"] = "public class Cow extends Animal { public void changed(Accessor<?> value) { super.changed(value); if (OTHER.equals(value)) { this.otherPhase(); } } }"
        raw["metadata_callback_owner"] = "Cow"
        verify_ageable_identity(raw, receipt, binding, sources, "a" * 64, "b" * 64, 92)
