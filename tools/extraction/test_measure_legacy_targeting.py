import hashlib
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile

from measure_legacy_targeting import run_checked, unsigned_client, verify_artifact


class LegacyMeasurementPipelineTests(unittest.TestCase):
    def test_unsigned_copy_preserves_classes_and_service_resources(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source, target = root / "signed.jar", root / "unsigned.jar"
            with zipfile.ZipFile(source, "w") as jar:
                jar.writestr("Block.class", b"original class bytes")
                jar.writestr("META-INF/MANIFEST.MF", b"signature digests")
                jar.writestr("META-INF/MOJANG.SF", b"signature")
                jar.writestr("META-INF/MOJANG.RSA", b"certificate")
                jar.writestr("META-INF/services/example", b"original service")
            result = unsigned_client(source, target)
            with zipfile.ZipFile(target) as jar:
                self.assertEqual(b"original class bytes", jar.read("Block.class"))
                self.assertEqual(b"original service", jar.read("META-INF/services/example"))
                self.assertFalse(any(name.endswith((".SF", ".RSA", "MANIFEST.MF")) for name in jar.namelist()))
            self.assertEqual(1, result["unchanged_class_files"])

    def test_artifact_hash_mismatch_is_refused(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            file = Path(temporary) / "client.jar"
            file.write_bytes(b"official artifact")
            verify_artifact(file, hashlib.sha1(file.read_bytes()).hexdigest())
            file.write_bytes(b"changed artifact")
            with self.assertRaisesRegex(ValueError, "artifact hash"):
                verify_artifact(file, hashlib.sha1(b"official artifact").hexdigest())

    def test_native_command_failure_remains_a_failure_with_its_log(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            with self.assertRaisesRegex(RuntimeError, "exited with 3"):
                run_checked([sys.executable, "-c", "print('native failure'); raise SystemExit(3)"], root, root / "native.log", 10)
            self.assertIn("native failure", (root / "native.log").read_text())


if __name__ == "__main__":
    unittest.main()
