import copy
import hashlib
import json
import unittest


def inputs() -> tuple[bytes, list[dict], dict[int, set[str]]]:
    raw = {"entity_profiles": {
        "minecraft:cow": {"class": "net.minecraft.world.entity.animal.Cow", "factory": "native", "controller": "net.minecraft.world.entity.InterpolationHandler", "update_interval": 3, "living": True},
        "minecraft:player": {"class": "EntityPlayerAdapter", "factory": "owned_player_subtype", "controller": "net.minecraft.world.entity.InterpolationHandler", "update_interval": 2, "living": True},
    }}
    contents = json.dumps(raw).encode()
    reports = [{"version": name, "protocol": 772, "status": "complete", "measurement_sha256": hashlib.sha256(contents).hexdigest(), "client_sha256": "1" * 64, "server_sha256": "2" * 64} for name in ("1.21.7", "1.21.8")]
    return contents, reports, {772: set(raw["entity_profiles"])}


class EntityMotionExtractionTests(unittest.TestCase):
    def test_preserves_measured_types_intervals_and_owned_player_scope(self) -> None:
        from extract_entity_motion import extract

        contents, reports, registries = inputs()
        output = extract(reports, {"1.21.7", "1.21.8"}, lambda _: contents, registries)
        entities = output[772]["entities"]
        self.assertEqual(3, entities["minecraft:cow"]["update_interval"])
        self.assertEqual(2, entities["minecraft:player"]["update_interval"])
        self.assertFalse(entities["minecraft:player"]["client_player_measured"])
        self.assertEqual(["1.21.7", "1.21.8"], output[772]["_provenance"]["versions"])

    def test_rejects_incomplete_failed_or_changed_native_coverage(self) -> None:
        from extract_entity_motion import extract

        for defect in ("missing", "duplicate", "failed", "hash", "registry", "alias"):
            contents, reports, registries = inputs()
            other = contents
            if defect == "missing": reports.pop()
            elif defect == "duplicate": reports.append(copy.deepcopy(reports[0]))
            elif defect == "failed": reports[0]["status"] = "failed"
            elif defect == "hash": reports[0]["measurement_sha256"] = "0" * 64
            elif defect == "registry": registries[772].add("minecraft:pig")
            else:
                raw = json.loads(contents); raw["entity_profiles"]["minecraft:cow"]["update_interval"] = 4
                other = json.dumps(raw).encode(); reports[1]["measurement_sha256"] = hashlib.sha256(other).hexdigest()
            with self.subTest(defect=defect), self.assertRaises(ValueError):
                extract(reports, {"1.21.7", "1.21.8"}, lambda name: other if name == "1.21.8" else contents, registries)

    def test_rejects_unproven_factories_invalid_intervals_and_types(self) -> None:
        from extract_entity_motion import extract

        for defect in ("factory", "interval", "living", "class", "controller", "player"):
            contents, reports, registries = inputs(); raw = json.loads(contents)
            cow = raw["entity_profiles"]["minecraft:cow"]
            if defect == "factory": cow["factory"] = "guessed"
            elif defect == "interval": cow["update_interval"] = 0
            elif defect == "living": cow["living"] = 1
            elif defect == "class": cow["class"] = ""
            elif defect == "controller": cow["controller"] = ""
            else: raw["entity_profiles"]["minecraft:player"]["factory"] = "native"
            contents = json.dumps(raw).encode()
            for row in reports: row["measurement_sha256"] = hashlib.sha256(contents).hexdigest()
            with self.subTest(defect=defect), self.assertRaises(ValueError):
                extract(reports, {"1.21.7", "1.21.8"}, lambda _: contents, registries)


if __name__ == "__main__":
    unittest.main()
