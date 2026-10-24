import io
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

from measure_block_interactions import class_bindings, interaction_catalog, unpack_jar, verify_client_origins


class InteractionMeasurementPipelineTests(unittest.TestCase):
    def test_entity_class_origins_require_complete_official_bindings(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            mapping = Path(temporary) / "client_mappings.txt"
            mapping.write_text("net.minecraft.world.entity.Entity -> abc:\n")
            self.assertEqual({"net.minecraft.world.entity.Entity": "abc"}, class_bindings(mapping, {"net.minecraft.world.entity.Entity"}))
            with self.assertRaisesRegex(ValueError, "Missing"):
                class_bindings(mapping, {"net.minecraft.world.entity.Entity", "net.minecraft.world.entity.EntityType"})

    def test_includes_official_patch_aliases_by_their_embedded_protocol(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "versions.json").write_text(json.dumps([{"id": "26.1"}, {"id": "26.1.1"}, {"id": "26.1.2"}]))
            for version in ("26.1", "26.1.1", "26.1.2"):
                directory = root / "downloads" / version
                directory.mkdir(parents=True)
                with zipfile.ZipFile(directory / "client.jar", "w") as archive:
                    archive.writestr("version.json", json.dumps({"id": version, "protocol_version": 775}))
            selected = interaction_catalog([{"name": "26.1", "protocol": 775}], root)
            self.assertEqual(["26.1", "26.1.1", "26.1.2"], [entry["name"] for entry in selected])
            self.assertEqual({775}, {entry["protocol"] for entry in selected})

    def test_unpack_preserves_class_and_service_bytes(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            data = io.BytesIO()
            with zipfile.ZipFile(data, "w") as output:
                output.writestr("game/Block.class", b"native class")
                output.writestr("META-INF/MOJANG.RSA", b"signature")
                output.writestr("META-INF/services/example", b"service")
            with zipfile.ZipFile(data) as archive:
                unpack_jar(archive, Path(temporary))
            self.assertEqual(b"native class", (Path(temporary) / "game/Block.class").read_bytes())
            self.assertEqual(b"service", (Path(temporary) / "META-INF/services/example").read_bytes())
            self.assertFalse((Path(temporary) / "META-INF/MOJANG.RSA").exists())

    def test_unpack_refuses_parent_path(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            data = io.BytesIO()
            with zipfile.ZipFile(data, "w") as output:
                output.writestr("../outside.class", b"bad")
            with zipfile.ZipFile(data) as archive, self.assertRaisesRegex(ValueError, "Unsafe"):
                unpack_jar(archive, Path(temporary))

    def test_origin_check_refuses_server_fallback_and_missing_classes(self) -> None:
        expected = Path("/tmp/client-unsigned.jar")
        log = "[info][class,load] a source: file:/tmp/client-unsigned.jar\n"
        self.assertEqual({"Block": "a"}, verify_client_origins(log, {"Block": "a"}, expected))
        for invalid in (log.replace("client-unsigned", "server"), ""):
            with self.subTest(log=invalid), self.assertRaisesRegex(ValueError, "client artifact"):
                verify_client_origins(invalid, {"Block": "a"}, expected)


if __name__ == "__main__":
    unittest.main()
