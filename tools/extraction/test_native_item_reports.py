"""Synthetic format and provenance checks; these fixtures are not native evidence."""

import contextlib
import hashlib
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

from extract_item_interactions import load_native_reports, main


class NativeReportOriginTests(unittest.TestCase):
    def setUp(self) -> None:
        temporary = self.enterContext(tempfile.TemporaryDirectory())
        self.root = Path(temporary)
        self.data = self.root / "data"
        self.official = self.root / "official"
        self.directory = self.data / "766"
        self.directory.mkdir(parents=True)
        self.write(self.data / "versions.json", {"versions": [{"name": "1.20.5", "protocol": 766}]})
        self.write(self.directory / "items.json", {"entries": [{"name": "minecraft:stick"}, {"name": "minecraft:wooden_pickaxe"}]})
        self.write(self.directory / "blocks.json", {"blocks": [{"name": "minecraft:stone"}]})
        self.write(self.directory / "block-mining.json", {"_provenance": {"unchanged": 17}, "unrelated": [1, 2]})
        downloads = self.official / "downloads" / "1.20.5"
        downloads.mkdir(parents=True)
        jar = downloads / "server.jar"
        jar.write_bytes(b"synthetic unit-test jar")
        self.write(downloads / "version.json", {"downloads": {"server": {"sha1": hashlib.sha1(jar.read_bytes()).hexdigest()}}})
        self.reports = self.root / "reports"
        self.reports.mkdir()
        self.tool = {"rules": [{"blocks": "#minecraft:mineable/pickaxe", "speed": 2.0}, {"blocks": ["minecraft:stone"]}], "default_mining_speed": 1.0}
        self.write(self.reports / "items.json", {"minecraft:stick": {"components": []}, "minecraft:wooden_pickaxe": {"components": [{"type": "minecraft:tool", "value": self.tool}]}})
        self.write(self.reports / "registries.json", {"minecraft:item": {"entries": {"minecraft:stick": {}, "minecraft:wooden_pickaxe": {}}}, "minecraft:block": {"entries": {"minecraft:stone": {}}}})
        self.source = self.root / "synthetic-source-equivalence.json"
        self.execution = self.root / "synthetic-native-execution.json"
        self.write(self.source, {"syntheticUnitFixture": True})
        self.write(self.execution, {"syntheticUnitFixture": True})
        self.recipe = self.root / "recipe.json"
        self.recipe_value = {"schema_version": 1, "origin": "vanilla-server-data-generator", "source_equivalence": self.pin(self.source), "releases": [{"version": "1.20.5", "protocol": 766, "format": "component-array", "reports_root": str(self.reports), "native_execution": self.pin(self.execution), "native_child_exit_code": 0, "all_providers_completed": True, "files": {name: self.pin(self.reports / name) for name in ["items.json", "registries.json"]}}]}
        self.write(self.recipe, self.recipe_value)

    @staticmethod
    def write(path: Path, value: dict) -> None:
        path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")

    @staticmethod
    def pin(path: Path) -> dict:
        return {"path": str(path), "bytes": path.stat().st_size, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}

    def run_extractor(self) -> dict:
        arguments = ["extract_item_interactions.py", "--data", str(self.data), "--official", str(self.official), "--native-reports", str(self.recipe), "--native-reports-sha256", self.pin(self.recipe)["sha256"]]
        with patch.object(sys, "argv", arguments), contextlib.redirect_stdout(io.StringIO()):
            main()
        return json.loads((self.directory / "block-mining.json").read_text(encoding="utf-8"))

    def test_explicit_native_report_mode_retains_values_and_names_its_origin(self) -> None:
        actual = self.run_extractor()
        self.assertEqual(actual["default_components"], {"minecraft:stick": None, "minecraft:wooden_pickaxe": self.tool})
        self.assertEqual(actual["unrelated"], [1, 2])
        self.assertEqual(actual["_provenance"]["unchanged"], 17)
        provenance = actual["_provenance"]["default_components"]
        self.assertEqual(provenance["origin"], "vanilla-server-data-generator")
        self.assertEqual(provenance["native_report_recipe_sha256"], self.pin(self.recipe)["sha256"])
        self.assertEqual(provenance["source_equivalence_sha256"], self.pin(self.source)["sha256"])
        self.assertNotIn("ItemInteractionDump", provenance["tool"])
        self.assertIn("do not recover historical ItemInteractionDump", provenance["note"])
        self.assertNotIn("measurement_sha256", provenance["measurements"][0])

    def test_recipe_rejects_changed_hash_before_reading_any_report(self) -> None:
        with self.assertRaisesRegex(ValueError, "recipe hash"):
            load_native_reports(self.recipe, "0" * 64, [{"name": "1.20.5", "protocol": 766}])

    def test_incomplete_production_never_proves_a_missing_component(self) -> None:
        self.recipe_value["releases"][0]["all_providers_completed"] = False
        self.write(self.recipe, self.recipe_value)
        with self.assertRaisesRegex(ValueError, "availability is unknown"):
            self.run_extractor()
        self.assertNotIn("default_components", json.loads((self.directory / "block-mining.json").read_text()))

    def test_changed_source_or_execution_receipt_is_rejected(self) -> None:
        for path in [self.source, self.execution]:
            with self.subTest(path=path.name):
                original = path.read_bytes()
                path.write_bytes(original + b" ")
                with self.assertRaisesRegex(ValueError, "input hash"):
                    self.run_extractor()
                path.write_bytes(original)

    def test_incomplete_or_foreign_release_cohort_is_rejected(self) -> None:
        for releases in [[], self.recipe_value["releases"] * 2, [self.recipe_value["releases"][0] | {"version": "other"}]]:
            with self.subTest(releases=releases):
                self.write(self.recipe, self.recipe_value | {"releases": releases})
                with self.assertRaisesRegex(ValueError, "exact release cohort"):
                    self.run_extractor()

    def test_missing_extra_or_changed_native_output_is_rejected(self) -> None:
        original = (self.reports / "items.json").read_bytes()
        for fault in ["missing", "extra", "changed"]:
            with self.subTest(fault=fault):
                if fault == "missing":
                    (self.reports / "items.json").unlink()
                elif fault == "extra":
                    (self.reports / "unmanifested.json").write_text("{}")
                else:
                    (self.reports / "items.json").write_bytes(original + b" ")
                with self.assertRaisesRegex(ValueError, "output tree|input hash"):
                    self.run_extractor()
                (self.reports / "items.json").write_bytes(original)
                (self.reports / "unmanifested.json").unlink(missing_ok=True)

    def test_format_and_native_registry_must_match_the_dataset(self) -> None:
        self.recipe_value["releases"][0]["format"] = "component-object"
        self.write(self.recipe, self.recipe_value)
        with self.assertRaisesRegex(ValueError, "format"):
            self.run_extractor()
        self.recipe_value["releases"][0]["format"] = "component-array"
        self.write(self.recipe, self.recipe_value)
        self.write(self.directory / "blocks.json", {"blocks": [{"name": "minecraft:dirt"}]})
        with self.assertRaisesRegex(ValueError, "registries differ"):
            self.run_extractor()

    def test_object_and_sparse_report_formats_reach_the_dataset_writer(self) -> None:
        for protocol, release, shape in [(767, "1.21", "component-object"), (775, "26.1", "per-registry-component-files")]:
            with self.subTest(protocol=protocol):
                previous = self.recipe_value["releases"][0]["version"]
                target = self.data / str(protocol)
                self.directory.rename(target)
                self.directory = target
                (self.official / "downloads" / previous).rename(self.official / "downloads" / release)
                self.write(self.data / "versions.json", {"versions": [{"name": release, "protocol": protocol}]})
                items = {"minecraft:stick": {"components": {}}, "minecraft:wooden_pickaxe": {"components": {"minecraft:tool": self.tool}}}
                if protocol == 767:
                    self.write(self.reports / "items.json", items)
                else:
                    (self.reports / "items.json").unlink()
                    item_file = self.reports / "minecraft/components/item/wooden_pickaxe.json"
                    item_file.parent.mkdir(parents=True)
                    self.write(item_file, items["minecraft:wooden_pickaxe"])
                self.recipe_value["releases"][0].update(version=release, protocol=protocol, format=shape, files={str(file.relative_to(self.reports)): self.pin(file) for file in self.reports.rglob("*") if file.is_file()})
                self.write(self.recipe, self.recipe_value)
                actual = self.run_extractor()
                self.assertEqual(actual["default_components"], {"minecraft:stick": None, "minecraft:wooden_pickaxe": self.tool})
                self.assertEqual(actual["_provenance"]["default_components"]["measurements"][0]["native_report_format"], shape)

    def test_original_measurement_mode_retains_its_distinct_origin(self) -> None:
        measurements = self.root / "measurements/1.20.5"
        measurements.mkdir(parents=True)
        raw = measurements / "item-interaction-raw.json"
        self.write(raw, {"minecraft:stick": None, "minecraft:wooden_pickaxe": self.tool})
        module = self.root / "extract_item_interactions.py"
        helper = self.root / "shape-extractor/ItemInteractionDump.java"
        helper.parent.mkdir()
        helper.write_text("synthetic unit-test helper")
        arguments = [str(module), "--data", str(self.data), "--official", str(self.official), "--measurements", str(measurements.parent)]
        with patch.object(sys, "argv", arguments), patch("extract_item_interactions.__file__", str(module)), contextlib.redirect_stdout(io.StringIO()):
            main()
        provenance = json.loads((self.directory / "block-mining.json").read_text())["_provenance"]["default_components"]
        self.assertEqual(provenance["tool"], "tools/extraction/shape-extractor/ItemInteractionDump.java")
        self.assertEqual(provenance["tool_sha256"], self.pin(helper)["sha256"])
        self.assertEqual(provenance["measurements"][0]["measurement_sha256"], self.pin(raw)["sha256"])
        self.assertNotIn("origin", provenance)


if __name__ == "__main__":
    unittest.main()
