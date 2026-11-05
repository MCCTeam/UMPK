"""Validate source callbacks before accepting mapped farm geometry."""

import copy
import unittest


class MappedFarmCallbackTests(unittest.TestCase):
    @staticmethod
    def inputs(gated: bool = False) -> tuple[dict, dict[str, str]]:
        namespace = "net.minecraft.world.entity."
        hierarchy = [namespace + "animal.Pig", namespace + "animal.Animal", namespace + "AgeableMob", namespace + "LivingEntity", namespace + "Entity"]
        owners = {"dimensions": hierarchy[3], "scale": hierarchy[3], "baby": hierarchy[2], "position": hierarchy[4], "refresh": hierarchy[4], "box": hierarchy[4], "synced_data": hierarchy[0]}
        raw = {"entity_class": hierarchy[0], "actor_hierarchy": hierarchy, "method_owners": owners, "origins": dict.fromkeys(hierarchy, "client.jar")}
        sources = {
            "net/minecraft/world/entity/animal/Pig.java": "package net.minecraft.world.entity.animal; public class Pig extends Animal { public void onSyncedDataUpdated(final EntityDataAccessor<?> value) { if (BOOST.equals(value)) { this.boost(); } super.onSyncedDataUpdated(value); } }",
            "net/minecraft/world/entity/animal/Animal.java": "package net.minecraft.world.entity.animal; import net.minecraft.world.entity.AgeableMob; public abstract class Animal extends AgeableMob {}",
            "net/minecraft/world/entity/AgeableMob.java": "package net.minecraft.world.entity; public abstract class AgeableMob extends LivingEntity { private static final EntityDataAccessor<Boolean> DATA_BABY_ID = SynchedEntityData.defineId(AgeableMob.class, EntityDataSerializers.BOOLEAN); protected void defineSynchedData() { this.entityData.define(DATA_BABY_ID, false); } public boolean isBaby() { return this.getAge() < 0; } public int getAge() { if (this.level().isClientSide()) { return this.entityData.get(DATA_BABY_ID) ? -1 : 1; } return this.age; } public void onSyncedDataUpdated(EntityDataAccessor<?> value) { if (DATA_BABY_ID.equals(value)) { this.refreshDimensions(); } super.onSyncedDataUpdated(value); } }",
            "net/minecraft/world/entity/LivingEntity.java": "package net.minecraft.world.entity; public abstract class LivingEntity extends Entity { public float getScale() { return this.isBaby() ? 0.5F : 1.0F; } }",
            "net/minecraft/world/entity/Entity.java": "package net.minecraft.world.entity; public abstract class Entity {}",
        }
        if gated:
            key = "net/minecraft/world/entity/AgeableMob.java"
            sources[key] = sources[key].replace("return this.getAge() < 0;", "return this.canBeABaby() && this.getAge() < 0;").replace("public boolean isBaby", "protected boolean canBeABaby() { return true; } public final boolean isBaby")
        return raw, sources

    def test_original_callback_chain_reaches_age_refresh(self) -> None:
        from extract_mapped_farm_geometry import callback_contract

        for gated in (False, True):
            with self.subTest(gated=gated):
                raw, sources = self.inputs(gated)
                contract = callback_contract(raw, sources)
                self.assertEqual(raw["method_owners"]["baby"], contract["age_owner"])
                self.assertEqual(raw["entity_class"], contract["callback_owner"])
                self.assertEqual(gated, contract["gated_baby_predicate"])

    def test_changed_age_semantics_or_conditional_delegation_is_rejected(self) -> None:
        from extract_mapped_farm_geometry import callback_contract

        for defect in ("parent", "import", "native-hierarchy", "source", "origin", "age-owner", "callback-owner", "scale-owner", "byte-selector", "default", "predicate", "client-age", "refresh", "conditional-super", "missing-super", "double-super", "early-return", "gate", "overridden-gate", "overridden-predicate", "overridden-age"):
            with self.subTest(defect=defect):
                raw, sources = self.inputs(True)
                pig = "net/minecraft/world/entity/animal/Pig.java"
                age = "net/minecraft/world/entity/AgeableMob.java"
                if defect == "parent": sources[pig] = sources[pig].replace("extends Animal", "extends Entity")
                if defect == "import": sources["net/minecraft/world/entity/animal/Animal.java"] = sources["net/minecraft/world/entity/animal/Animal.java"].replace("import net.minecraft.world.entity.AgeableMob", "import other.AgeableMob")
                if defect == "native-hierarchy": raw["actor_hierarchy"].remove("net.minecraft.world.entity.animal.Animal")
                if defect == "source": sources.pop(age)
                if defect == "origin": raw["origins"].pop(raw["entity_class"])
                if defect == "age-owner": raw["method_owners"]["baby"] = raw["entity_class"]
                if defect == "callback-owner": raw["method_owners"]["synced_data"] = raw["method_owners"]["baby"]
                if defect == "scale-owner": raw["method_owners"]["scale"] = raw["entity_class"]
                if defect == "byte-selector": sources[age] = sources[age].replace("EntityDataAccessor<Boolean>", "EntityDataAccessor<Byte>")
                if defect == "default": sources[age] = sources[age].replace("DATA_BABY_ID, false", "DATA_BABY_ID, true")
                if defect == "predicate": sources[age] = sources[age].replace("getAge() < 0", "getAge() > 0")
                if defect == "client-age": sources[age] = sources[age].replace("? -1 : 1", "? 1 : -1")
                if defect == "refresh": sources[age] = sources[age].replace("this.refreshDimensions();", "")
                call = "super.onSyncedDataUpdated(value);"
                if defect == "conditional-super": sources[pig] = sources[pig].replace(call, "if (false) { " + call + " }")
                if defect == "missing-super": sources[pig] = sources[pig].replace(call, "")
                if defect == "double-super": sources[pig] = sources[pig].replace(call, call + call)
                if defect == "early-return": sources[pig] = sources[pig].replace(call, "return; " + call)
                if defect == "gate": sources[age] = sources[age].replace("return true;", "return false;")
                if defect == "overridden-gate": sources[pig] = sources[pig].replace("public class Pig extends Animal {", "public class Pig extends Animal { protected boolean canBeABaby() { return false; }")
                if defect == "overridden-predicate": sources[pig] = sources[pig].replace("public class Pig extends Animal {", "public class Pig extends Animal { public boolean isBaby() { return false; }")
                if defect == "overridden-age": sources[pig] = sources[pig].replace("public class Pig extends Animal {", "public class Pig extends Animal { public int getAge() { return 1; }")
                with self.assertRaises(ValueError):
                    callback_contract(raw, sources)


class MappedFarmCohortTests(unittest.TestCase):
    @staticmethod
    def records() -> list[dict]:
        import gzip
        import json
        from pathlib import Path

        fixture = Path(__file__).parents[2] / "tests/Umpk.Client.Tests/Fixtures/PoseFarmGeometry.native.json.gz"
        native = json.loads(gzip.decompress(fixture.read_bytes()))
        template = copy.deepcopy(native["cases"][0])
        for row in template["rows"]:
            row["before_child"] = bool(row["from_age"])
            row["after_child"] = bool(row["to_age"])
        records = []
        for version, protocol in (("1.14.4", 498), ("1.16.4", 754), ("1.16.5", 754)):
            for role in ("pig", "sheep", "chicken"):
                raw = copy.deepcopy(template)
                raw["entity"] = "minecraft:" + role
                records.append({"version": version, "protocol": protocol, "entity": raw["entity"], "scale_attribute": "", "raw": raw})
        return records

    def test_all_release_type_pairs_and_aliases_are_required(self) -> None:
        from extract_mapped_farm_geometry import group_mapped_farms

        expected = {"1.14.4": 498, "1.16.4": 754, "1.16.5": 754}
        groups = group_mapped_farms(self.records(), expected)
        self.assertEqual({498, 754}, set(groups))
        self.assertEqual(2, len(groups[754]["minecraft:pig"]))
        self.assertEqual({"minecraft:pig", "minecraft:sheep", "minecraft:chicken"}, set(groups[498]))

    def test_missing_identity_alias_or_native_phase_is_rejected(self) -> None:
        from extract_mapped_farm_geometry import group_mapped_farms

        expected = {"1.14.4": 498, "1.16.4": 754, "1.16.5": 754}
        for defect in ("missing", "duplicate", "type", "protocol", "raw-entity", "child-type", "child-value", "phase", "missing-phase", "position", "selector", "alias"):
            with self.subTest(defect=defect):
                records = self.records()
                raw = records[0]["raw"]
                if defect == "missing": records.pop()
                if defect == "duplicate": records.append(copy.deepcopy(records[0]))
                if defect == "type": records[0]["entity"] = "minecraft:cow"
                if defect == "protocol": records[0]["protocol"] = 498.0
                if defect == "raw-entity": raw["entity"] = "minecraft:cow"
                if defect == "child-type": raw["rows"][0]["before_child"] = 0
                if defect == "child-value": raw["rows"][0]["after_child"] = not raw["rows"][0]["after_child"]
                if defect == "phase": raw["rows"][0]["after_age_sync"][0] += .25
                if defect == "missing-phase": raw["rows"].pop()
                if defect == "position": raw["positions"][0] = raw["positions"][1]
                if defect == "selector": raw["selector_kind"] = "none"
                if defect == "alias": records[-1]["raw"]["metadata_index"] += 1
                with self.assertRaises(ValueError):
                    group_mapped_farms(records, expected)


class MappedFarmAuditTests(unittest.TestCase):
    def test_source_audit_binds_unchanged_receipt_measurement_and_artifacts(self) -> None:
        from extract_mapped_farm_geometry import audit_binding

        receipt = {"version": "1.14.4", "protocol": 498, "entity": "minecraft:pig", "measurement_sha256": "m", "helper_class_sha256": "h", "client_sha256": "c", "mappings_sha256": "n"}
        audit = receipt | {"receipt_sha256": "r", "measurement_root": "/native"}
        audit_binding(receipt, audit, "r", "/native")
        for field in (*receipt, "receipt_sha256", "measurement_root"):
            with self.subTest(field=field):
                changed = copy.deepcopy(audit)
                changed[field] = "wrong"
                with self.assertRaises(ValueError):
                    audit_binding(receipt, changed, "r", "/native")

    def test_new_format_requires_a_source_audit_before_artifact_access(self) -> None:
        import json
        from pathlib import Path
        import tempfile
        from extract_entity_geometry import read_legacy

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "result.json").write_text(json.dumps({"format": "mapped-farm-role-v1", "entity": "minecraft:pig", "version": "1.14.4", "protocol": 498}))
            (root / "geometry.json").write_text("{}")
            with self.assertRaisesRegex(ValueError, "requires a source audit"):
                read_legacy(root, root / "missing-official", [{"name": "1.14.4", "protocol": 498}])


class MappedFarmAuditFileTests(unittest.TestCase):
    def test_changed_audit_or_input_summary_is_rejected(self) -> None:
        import hashlib
        import json
        from pathlib import Path
        import tempfile
        from extract_mapped_farm_geometry import load_audit

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            measurements = root / "measurements"
            measurements.mkdir()
            summary = measurements / "results.json"
            summary.write_text("[]")
            document = {"schema": 1, "measurements": 0, "records": [], "audit_files_sha256": {}, "inputs": [{"root": str(measurements), "results_sha256": hashlib.sha256(summary.read_bytes()).hexdigest()}]}
            path = root / "audit.json"
            path.write_text(json.dumps(document))
            expected = hashlib.sha256(path.read_bytes()).hexdigest()
            self.assertEqual(document, load_audit(path, expected, [measurements]))
            with self.assertRaisesRegex(ValueError, "audit hash"):
                load_audit(path, "wrong", [measurements])
            summary.write_text("[{}]")
            with self.assertRaisesRegex(ValueError, "input summary"):
                load_audit(path, expected, [measurements])

    def test_fixture_preserves_native_identity_and_child_observations(self) -> None:
        from extract_mapped_farm_geometry import mapped_farm_fixture

        records = MappedFarmCohortTests.records()
        for index, record in enumerate(records):
            record["receipt"] = {key: record[key] for key in ("version", "protocol", "entity")} | {"registry_numeric_id": index, "measurement_sha256": "m", "client_sha256": "c", "server_sha256": "s", "scope": "default attributes", "helper_sha256": {"Helper.java": "h"}, "helper_class_sha256": "b"}
        fixture = mapped_farm_fixture(records, ["native"], "audit-hash")
        self.assertEqual(9, len(fixture["releases"]))
        self.assertEqual(3, len(fixture["cases"]))
        self.assertEqual("audit-hash", fixture["source_audit_sha256"])
        for release in fixture["releases"]:
            raw = fixture["cases"][release["case"]]
            self.assertEqual(release["entity"], raw["entity"])
            self.assertIs(raw["rows"][0]["before_child"], False)
            self.assertNotIn("origins", raw)


if __name__ == "__main__":
    unittest.main()
