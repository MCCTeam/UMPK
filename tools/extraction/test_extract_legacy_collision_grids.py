import json
from pathlib import Path
import shutil
import tempfile
import unittest

from extract_legacy_collision_grids import RELEASES, read_measurements, sha
from extract_legacy_player_clearance import read_run


class LegacyCollisionMeasurementTests(unittest.TestCase):
    def test_incomplete_release_cohort_is_rejected_before_reading_artifacts(self) -> None:
        root = Path(self.enterContext(tempfile.TemporaryDirectory()))
        (root / 'results.json').write_text('[]')
        with self.assertRaisesRegex(ValueError, 'cover each audited release'):
            read_measurements(root, root / 'absent-official')

    def test_an_unreviewed_native_probe_is_rejected(self) -> None:
        root = Path(self.enterContext(tempfile.TemporaryDirectory()))
        (root / 'results.json').write_text(json.dumps([{'version': version} for version in RELEASES]))
        (root / 'tools.json').write_text(json.dumps({'files_sha256': {'LegacyCollisionGridDump.java': '0' * 64}}))
        with self.assertRaisesRegex(ValueError, 'reviewed source'):
            read_measurements(root, root / 'absent-official')

    def test_a_run_manifest_cannot_read_outside_its_measurement_directory(self) -> None:
        parent = Path(self.enterContext(tempfile.TemporaryDirectory()))
        root = parent / 'run'
        root.mkdir()
        probe = Path(__file__).parent / 'shape-extractor/LegacyCollisionGridDump.java'
        shutil.copy2(probe, root / probe.name)
        outside = parent / 'outside'
        outside.write_text('unchanged external file')
        (root / 'results.json').write_text(json.dumps([{'version': version} for version in RELEASES]))
        (root / 'tools.json').write_text(json.dumps({'files_sha256': {probe.name: sha(probe), '../outside': sha(outside)}}))
        with self.assertRaisesRegex(ValueError, 'run file changed'):
            read_measurements(root, root / 'absent-official')
        self.assertEqual('unchanged external file', outside.read_text())

    def test_clearance_import_rejects_a_changed_original_probe(self) -> None:
        root = Path(self.enterContext(tempfile.TemporaryDirectory()))
        (root / 'results.json').write_text('[]')
        (root / 'tools.json').write_text(json.dumps({'probe_source_sha256': '0' * 64}))
        (root / 'OwnedProbe.java').write_text('changed probe')
        with self.assertRaisesRegex(ValueError, 'clearance probe changed'):
            read_run(root, root / 'absent-official')


if __name__ == '__main__':
    unittest.main()
