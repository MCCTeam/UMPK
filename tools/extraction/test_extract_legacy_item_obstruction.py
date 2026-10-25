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

    def test_classic_aliases_keep_their_original_class_names(self) -> None:
        from extract_legacy_item_obstruction import item_alias_rules, item_rules

        aliases = [row for row in self.native_measurements() if row["protocol"] == 47]
        self.assertEqual(10, len(aliases))
        self.assertEqual({"minecraft:item": 1}, item_alias_rules(aliases, {"minecraft:item"}))
        for raw in aliases:
            with self.subTest(version=raw["version"]):
                self.assertEqual({"minecraft:item": 1}, item_rules(raw, {"minecraft:item"}))
                raw["class"] = "yd"
                with self.assertRaises(ValueError):
                    item_rules(raw, {"minecraft:item"})

    def test_classic_alias_coverage_cannot_stop_at_one_release(self) -> None:
        from extract_legacy_item_obstruction import item_alias_rules

        aliases = [row for row in self.native_measurements() if row["protocol"] == 47]
        with self.assertRaises(ValueError):
            item_alias_rules(aliases[:-1], {"minecraft:item"})

    def test_legacy_registry_names_keep_their_spelling_after_identity_join(self) -> None:
        from extract_legacy_item_obstruction import registry_identities

        raw = {"mob_entries": [{"id": "villager"}], "object_entries": [{"id": "item"}, {"id": "minecraft:boat"}]}
        self.assertEqual({"minecraft:villager": "villager", "minecraft:item": "item", "minecraft:boat": "minecraft:boat"}, registry_identities(raw))
        raw["object_entries"].append({"id": "minecraft:item"})
        with self.assertRaises(ValueError):
            registry_identities(raw)

    def test_flattening_does_not_reuse_the_earlier_building_flag(self) -> None:
        from extract_legacy_item_obstruction import item_rules

        for raw in self.native_measurements():
            if raw["protocol"] in (393, 401, 404):
                self.assertEqual("j", raw["field_name"])
                raw["field_name"] = "i"
                with self.assertRaises(ValueError):
                    item_rules(raw, {"minecraft:item", "minecraft:villager"})

    def test_1_14_restores_the_native_i_field_binding(self) -> None:
        from extract_legacy_item_obstruction import item_rules

        for raw in self.native_measurements():
            if raw["protocol"] in (477, 480, 485, 490):
                with self.subTest(version=raw["version"]):
                    self.assertEqual("i", raw["field_name"])
                    self.assertEqual({"minecraft:item": 1}, item_rules(raw, {"minecraft:item"}))
                    raw["field_name"] = "j"
                    with self.assertRaises(ValueError):
                        item_rules(raw, {"minecraft:item"})

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


class LegacySourceNonblockingProofTests(unittest.TestCase):
    def bound_measurement(self) -> dict:
        return {"protocol": 315, "version": "1.11", "entity": "minecraft:item", "class": "zi", "field_owner": "sm", "field_name": "i", "rows": [{"constructor": "default", "blocks_building": False}, {"constructor": "network_xyz", "blocks_building": False}], "origins": {name: "original-1.11-client.jar" for name in ("zi", "sm", "ajq")}}

    def proof(self) -> dict:
        path = Path(__file__).parent / "tests/fixtures/legacy315-xp-obstruction-source-proof.json"
        return json.loads(path.read_text(encoding="utf-8"))

    def test_bound_source_proof_promotes_xp_without_inventing_other_entity_geometry(self) -> None:
        from extract_legacy_item_obstruction import item_rules

        raw = self.bound_measurement()
        raw["source_nonblocking_proof"] = self.proof()
        entities = {"minecraft:item", "minecraft:xp_orb", "minecraft:zombie", "minecraft:unknown"}
        self.assertEqual({"minecraft:item": 1, "minecraft:unknown": 0, "minecraft:xp_orb": 1, "minecraft:zombie": 0}, item_rules(raw, entities))

    def test_changed_proof_cannot_promote_a_type_or_claim_executed_native_construction(self) -> None:
        from extract_legacy_item_obstruction import item_rules

        for field, value in (("entity", "minecraft:zombie"), ("protocol", 316), ("initial_building_flag", True), ("native_xp_constructor_executed", True), ("source_proof_sha256", "0" * 64), ("client_sha256", "0" * 64), ("writer_audit_sha256", "0" * 64), ("root_flag_writers", []), ("direct_superclass", "sv"), ("class", "sv"), ("world_query_body_sha256", "0" * 64)):
            with self.subTest(field=field):
                raw = self.bound_measurement()
                proof = self.proof()
                proof[field] = value
                raw["source_nonblocking_proof"] = proof
                with self.assertRaises(ValueError):
                    item_rules(raw, {"minecraft:item", "minecraft:xp_orb", "minecraft:zombie"})

    def test_source_proof_cannot_cross_era_or_missing_registry_identity(self) -> None:
        from extract_legacy_item_obstruction import item_rules

        for defect in ("version", "protocol", "identity"):
            with self.subTest(defect=defect):
                raw = self.bound_measurement()
                raw["source_nonblocking_proof"] = self.proof()
                entities = {"minecraft:item", "minecraft:xp_orb"}
                if defect == "version": raw["version"] = "1.11.1"
                if defect == "protocol": raw.update(protocol=210, version="1.10.2", **{"class": "yk", "field_owner": "rw", "origins": {name: "original-client.jar" for name in ("yk", "rw", "aid")}})
                if defect == "identity": entities.remove("minecraft:xp_orb")
                with self.assertRaises(ValueError):
                    item_rules(raw, entities)

    def test_without_proof_the_original_legacy_rules_stay_unchanged(self) -> None:
        from extract_legacy_item_obstruction import item_rules

        self.assertEqual({"minecraft:item": 1, "minecraft:xp_orb": 0}, item_rules(self.bound_measurement(), {"minecraft:item", "minecraft:xp_orb"}))


class LegacyXpFamilyProofTests(unittest.TestCase):
    def bundle(self) -> dict:
        path = Path(__file__).parent / "tests/fixtures/legacy-xp-obstruction-source-proofs.json"
        return json.loads(path.read_text(encoding="utf-8"))

    def native_measurements(self) -> list[dict]:
        path = Path(__file__).parent / "tests/fixtures/legacy-item-obstruction-bindings.json"
        rows = [row["native"] for row in json.loads(path.read_text(encoding="utf-8"))]
        rows.append(LegacyItemObstructionTests().measurement())
        return rows

    def test_all_thirty_native_bindings_promote_only_their_exact_xp_type(self) -> None:
        from extract_legacy_item_obstruction import item_rules

        native = {row["version"]: row for row in self.native_measurements()}
        proofs = self.bundle()["bindings"]
        self.assertEqual(30, len(proofs))
        self.assertEqual(17, len({proof["protocol"] for proof in proofs}))
        for proof in proofs:
            with self.subTest(version=proof["version"]):
                raw = copy.deepcopy(native[proof["version"]])
                raw["source_nonblocking_proof"] = proof
                entities = {"minecraft:item", proof["entity"], "minecraft:unknown", "minecraft:zombie"}
                self.assertEqual({name: int(name in {"minecraft:item", proof["entity"]}) for name in sorted(entities)}, item_rules(raw, entities))

    def test_bundle_selection_requires_the_exact_protocol_and_named_release(self) -> None:
        from extract_legacy_item_obstruction import select_source_nonblocking_proof

        bundle = self.bundle()
        for proof in bundle["bindings"]:
            with self.subTest(version=proof["version"]):
                self.assertEqual(proof, select_source_nonblocking_proof(bundle, proof["protocol"], proof["version"]))
        old = LegacySourceNonblockingProofTests().proof()
        self.assertEqual(old, select_source_nonblocking_proof(old, 315, "1.11"))
        for protocol, version in [(315, "1.11"), (47, "1.8.10"), (498, "1.14.4"), (107, "1.9.1")]:
            with self.assertRaises(ValueError):
                select_source_nonblocking_proof(bundle, protocol, version)

    def test_every_native_alias_agrees_while_protocol47_stays_the_1_8_9_profile(self) -> None:
        from extract_legacy_item_obstruction import item_alias_rules

        proofs = {row["version"]: row for row in self.bundle()["bindings"]}
        groups = {}
        for row in self.native_measurements():
            if row["version"] in proofs:
                row["source_nonblocking_proof"] = proofs[row["version"]]
                groups.setdefault(row["protocol"], []).append(row)
        self.assertEqual(10, len(groups[47]))
        self.assertIn("1.8.9", proofs)
        for protocol, rows in groups.items():
            with self.subTest(protocol=protocol):
                xp = rows[0]["source_nonblocking_proof"]["entity"]
                self.assertEqual({"minecraft:item": 1, xp: 1}, item_alias_rules(rows, {"minecraft:item", xp}))

    def test_bound_fact_tampering_and_unknown_identity_are_rejected(self) -> None:
        from extract_legacy_item_obstruction import item_rules

        native = {row["version"]: row for row in self.native_measurements()}
        for original in self.bundle()["bindings"]:
            for field, replacement in [("client_sha256", "0" * 64), ("writer_audit_sha256", "0" * 64), ("unrelated_writer_receivers", []), ("direct_superclass", "unproven"), ("placement_predicate", {}), ("native_xp_constructor_executed", True), ("entity", "minecraft:zombie")]:
                with self.subTest(version=original["version"], field=field):
                    proof = copy.deepcopy(original)
                    proof[field] = replacement
                    raw = copy.deepcopy(native[original["version"]])
                    raw["source_nonblocking_proof"] = proof
                    with self.assertRaises(ValueError):
                        item_rules(raw, {"minecraft:item", original["entity"], "minecraft:zombie"})
            raw = copy.deepcopy(native[original["version"]])
            raw["source_nonblocking_proof"] = original
            with self.assertRaises(ValueError):
                item_rules(raw, {"minecraft:item"})

    def test_absent_source_proof_keeps_each_xp_entry_unknown(self) -> None:
        from extract_legacy_item_obstruction import item_rules

        native = {row["version"]: row for row in self.native_measurements()}
        for proof in self.bundle()["bindings"]:
            with self.subTest(version=proof["version"]):
                xp = proof["entity"]
                self.assertEqual({"minecraft:item": 1, xp: 0}, item_rules(native[proof["version"]], {"minecraft:item", xp}))

    def test_changed_bundle_missing_alias_or_extra_binding_is_rejected(self) -> None:
        from extract_legacy_item_obstruction import select_source_nonblocking_proof

        for defect in ["missing", "duplicate", "extra", "schema"]:
            with self.subTest(defect=defect):
                bundle = self.bundle()
                if defect == "missing": bundle["bindings"].pop()
                if defect == "duplicate": bundle["bindings"].append(copy.deepcopy(bundle["bindings"][0]))
                if defect == "extra": bundle["unreviewed"] = True
                if defect == "schema": bundle["schema"] = 1
                with self.assertRaises(ValueError):
                    select_source_nonblocking_proof(bundle, 47, "1.8.9")
