"""Require the complete native farm cohort at the first pose boundary."""

import copy
import gzip
import json
from pathlib import Path
import unittest


class PoseFarmGeometryTests(unittest.TestCase):
    @staticmethod
    def records() -> list[dict]:
        root = Path(__file__).parents[2]
        native = json.loads(gzip.decompress((root / "tests/Umpk.Client.Tests/Fixtures/PoseFarmGeometry.native.json.gz").read_bytes()))
        return [{key: row[key] for key in ("version", "protocol", "entity")} | {"raw": copy.deepcopy(native["cases"][row["case"]])} for row in native["releases"]]

    @staticmethod
    def catalog() -> dict[str, int]:
        rows = json.loads((Path(__file__).parents[2] / "data/java/versions.json").read_text())["versions"]
        return {row["name"]: row["protocol"] for row in rows if row["protocol"] in (477, 480, 485, 490)}

    def test_complete_native_cohort_preserves_all_age_pose_variants(self) -> None:
        from extract_pose_farm_geometry import group_pose_farms, pose_farm_profile

        records = self.records()
        groups = group_pose_farms(records, self.catalog())
        self.assertEqual(16, len(records))
        self.assertEqual({477, 480, 485, 490}, set(groups))
        for protocol, entities in groups.items():
            self.assertEqual({"minecraft:cow", "minecraft:pig", "minecraft:sheep", "minecraft:chicken"}, set(entities))
            for rows in entities.values():
                profile = pose_farm_profile(rows[0]["raw"], protocol)
                self.assertEqual([14, "boolean", 1, 0], profile["selector"])
                self.assertEqual(6, profile["pose_index"])
                self.assertEqual("retain_minimum_on_growth", profile["refresh"])
                self.assertEqual(14, len(profile["variants"]))
                self.assertEqual([[0, 2, .2, .2, True, True], [1, 2, .2, .2, True, True]], [v for v in profile["variants"] if v[1] == 2])

    def test_missing_mixed_or_corrupt_phase_evidence_is_rejected(self) -> None:
        from extract_pose_farm_geometry import group_pose_farms

        for defect in ("missing-release", "duplicate", "missing-type", "protocol", "protocol-type", "unknown-type", "raw-identity", "age-index", "pose-index", "pose-coverage", "phase", "refresh-phase", "missing-row", "duplicate-position", "selector-kind", "selector-default"):
            with self.subTest(defect=defect):
                rows = self.records()
                raw = rows[0]["raw"]
                if defect == "missing-release": rows.pop()
                if defect == "duplicate": rows.append(copy.deepcopy(rows[0]))
                if defect == "missing-type": rows = [row for row in rows if row["entity"] != "minecraft:cow"]
                if defect == "protocol": rows[0]["protocol"] = 498
                if defect == "protocol-type": rows[0]["protocol"] = 477.0
                if defect == "unknown-type": rows[0]["entity"] = "minecraft:horse"
                if defect == "raw-identity": raw["entity"] = "minecraft:horse"
                if defect == "age-index": raw["metadata_index"] = 12
                if defect == "pose-index": raw["pose_index"] = 7
                if defect == "pose-coverage": raw["poses"][-1] = "LONG_JUMPING"
                if defect == "phase": raw["rows"][0]["after_age_sync"][0] += .25
                if defect == "refresh-phase": raw["rows"][0]["after_refresh"][0] += .25
                if defect == "missing-row": raw["rows"].pop()
                if defect == "duplicate-position": raw["positions"][0] = raw["positions"][1]
                if defect == "selector-kind": raw["selector_kind"] = "none"
                if defect == "selector-default": raw["metadata_default"] = 0
                with self.assertRaises(ValueError):
                    group_pose_farms(rows, self.catalog())

    @staticmethod
    def identity_inputs() -> tuple:
        binding = {"entity": "minecraft:cow", "root": "Entity", "living": "Living", "world": "World", "client_world": "ClientWorld", "actor": "Cow", "actor_factory": "COW", "ageable": "Ageable", "accessor": "Accessor", "accessor_field": "BABY", "data": "Data", "data_field": "synched", "dimensions_class": "Dimensions", "pose": "Pose", "pose_accessor": "POSE", "default_pose": "a", "sleeping_pose": "c", "entity_registry": "Types", "registry_owner": "Registries", "registry_field": "ENTITY_TYPE", "registry_key_method": "key", "entity_type_getter": "type", "scale_method": "scale", "is_baby_method": "isBaby", "refresh": "refresh"}
        origins = {name: "client.jar" for name in ("Entity", "Living", "World", "ClientWorld", "Cow", "Animal", "Ageable", "Accessor", "Data", "Dimensions", "Pose", "Types", "Registries", "Registry")}
        raw = {"entity": "minecraft:cow", "entity_class": "Cow", "actor_hierarchy": ["Cow", "Animal", "Ageable", "Living", "Entity"], "entity_type_matches_factory": True, "pose_constants": list("abcdefg"), "origins": origins, "method_owners": {"dimensions": "Living", "scale": "Living", "baby": "Ageable", "position": "Entity", "refresh": "Entity", "box": "Entity"}, "registry": {"kind": "type-registry", "id": 10, "name": "minecraft:cow", "id_method": "id", "id_owner": "Registry", "key_method": "key", "key_owner": "Registry", "origins": {"Registries": "client.jar", "Registry": "client.jar"}}}
        receipt = {"entity": "minecraft:cow", "registry_class": "Cow", "registry_numeric_id": 10, "registry_lookup_audited": True, "binding": binding, "helper_sha256": "a" * 64, "helper_class_sha256": "b" * 64}
        sources = {name + ".java": "" for name in origins}
        sources.update({"Cow.java": "public class Cow extends Animal {}", "Animal.java": "public abstract class Animal extends Ageable {}", "Ageable.java": "private static final Accessor<Boolean> BABY = Data.define(Ageable.class, Serializers.BOOL); public boolean isBaby() { return this.age() < 0; } public int age() { return this.synched.get(BABY) ? -1 : 1; } public void changed(Accessor<?> value) { if (BABY.equals(value)) { this.refresh(); } super.changed(value); }", "Living.java": "public float scale() { return this.isBaby() ? 0.5F : 1.0F; }", "Entity.java": "private static final Accessor<Pose> POSE = Data.define(Entity.class, Serializers.POSE); public Types<?> type() { return this.kind; }", "Types.java": 'public static final Types<Cow> COW = register("cow", Builder.of(Cow::new, Category.CREATURE)); public static Key key(Types<?> value) { return Registries.ENTITY_TYPE.key(value); }'})
        sources["Ageable.java"] = "public abstract class Ageable extends Living { " + sources["Ageable.java"] + " }"
        sources["Living.java"] = "public abstract class Living extends Entity { " + sources["Living.java"] + " }"
        return raw, receipt, binding, sources

    def test_registry_factory_type_and_original_owners_are_required(self) -> None:
        from extract_pose_farm_geometry import verify_pose_farm_identity

        raw, receipt, binding, sources = self.identity_inputs()
        verify_pose_farm_identity(raw, receipt, binding, sources, "a" * 64, "b" * 64, 10)

    def test_changed_identity_source_helper_or_callback_chain_is_rejected(self) -> None:
        from extract_pose_farm_geometry import verify_pose_farm_identity

        for defect in ("entity", "binding-entity", "actor", "receipt-class", "factory-match", "factory-source", "registry-id", "registry-name", "registry-kind", "registry-receiver", "registry-origin", "hierarchy-origin", "hierarchy-source", "native-hierarchy", "source-coverage", "helper-source", "helper-class", "age-owner", "dimensions-owner", "box-owner", "scale-source", "selector-source", "predicate-source", "callback-source", "conditional-super", "type-getter", "pose-selector", "pose-constant", "unaudited-registry"):
            with self.subTest(defect=defect):
                raw, receipt, binding, sources = self.identity_inputs()
                if defect == "entity": raw["entity"] = "minecraft:pig"
                if defect == "binding-entity": binding["entity"] = "minecraft:pig"
                if defect == "actor": raw["entity_class"] = "Other"
                if defect == "receipt-class": receipt["registry_class"] = "Other"
                if defect == "factory-match": raw["entity_type_matches_factory"] = False
                if defect == "factory-source": sources["Types.java"] = sources["Types.java"].replace("Cow::new", "Other::new")
                if defect == "registry-id": raw["registry"]["id"] = 9
                if defect == "registry-name": raw["registry"]["name"] = "minecraft:pig"
                if defect == "registry-kind": raw["registry"]["kind"] = "class-registry"
                if defect == "registry-receiver": sources["Types.java"] = sources["Types.java"].replace("Registries.ENTITY_TYPE.key", "Registries.ITEM.key")
                if defect == "registry-origin": raw["registry"]["origins"]["Registry"] = "other.jar"
                if defect == "hierarchy-origin": raw["origins"].pop("Animal")
                if defect == "hierarchy-source": sources["Cow.java"] = "public class Cow extends Entity {}"
                if defect == "native-hierarchy": raw["actor_hierarchy"].remove("Animal")
                if defect == "source-coverage": sources.pop("Ageable.java")
                if defect == "helper-source": receipt["helper_sha256"] = "c" * 64
                if defect == "helper-class": receipt["helper_class_sha256"] = "c" * 64
                if defect == "age-owner": raw["method_owners"]["baby"] = "Living"
                if defect == "dimensions-owner": raw["method_owners"]["dimensions"] = "Cow"
                if defect == "box-owner": raw["method_owners"]["box"] = "Cow"
                if defect == "scale-source": sources["Living.java"] = sources["Living.java"].replace("0.5F", "0.75F")
                if defect == "selector-source": sources["Ageable.java"] = sources["Ageable.java"].replace("Accessor<Boolean>", "Accessor<Byte>")
                if defect == "predicate-source": sources["Ageable.java"] = sources["Ageable.java"].replace("< 0", "> 0")
                if defect == "callback-source": sources["Ageable.java"] = sources["Ageable.java"].replace("this.refresh();", "")
                if defect == "conditional-super": sources["Cow.java"] = "public class Cow extends Animal { public void changed(Accessor<?> value) { if (false) { super.changed(value); } } }"
                if defect == "type-getter": sources["Entity.java"] = sources["Entity.java"].replace("return this.kind;", "return null;")
                if defect == "pose-selector": sources["Entity.java"] = sources["Entity.java"].replace("Accessor<Pose>", "Accessor<Byte>")
                if defect == "pose-constant": raw["pose_constants"][2] = "z"
                if defect == "unaudited-registry": receipt["registry_lookup_audited"] = False
                with self.assertRaises(ValueError):
                    verify_pose_farm_identity(raw, receipt, binding, sources, "a" * 64, "b" * 64, 10)

    def test_unconditional_actor_callback_override_preserves_native_age_phase(self) -> None:
        from extract_pose_farm_geometry import verify_pose_farm_identity

        raw, receipt, binding, sources = self.identity_inputs()
        sources["Cow.java"] = "public class Cow extends Animal { public void changed(Accessor<?> value) { super.changed(value); if (BOOST.equals(value)) { this.boost(); } } }"
        verify_pose_farm_identity(raw, receipt, binding, sources, "a" * 64, "b" * 64, 10)
