"""Keep the optional native view distance separate from the default isolated budget."""

from contextlib import redirect_stderr
from io import StringIO
import json
from pathlib import Path
from tempfile import TemporaryDirectory
import sys
import unittest
from unittest.mock import patch

import server


class PreparedProperties(Exception):
    """Stop before starting a child; this test checks only written launch properties."""


class ViewDistanceTests(unittest.TestCase):
    def properties_for(self, *arguments: str, release: str = '1.21.5') -> dict[str, str]:
        with TemporaryDirectory(prefix='umpk-view-distance-') as temporary:
            root = Path(temporary)
            seed = root / 'downloads' / release
            seed.mkdir(parents=True)
            (seed / 'server.jar').write_bytes(b'owned launcher option fixture')
            (seed / 'eula.txt').write_text('eula=true\n', encoding='utf-8')
            run = root / 'run'
            command = ['server.py', release, '--root', str(root), '--run-dir', str(run), *arguments]
            with patch.dict('os.environ', {}, clear=True), patch.object(sys, 'argv', command), patch.object(server, 'check_port'), patch.object(server, 'java_for', return_value='not-executed'), patch.object(server.subprocess, 'Popen', side_effect=PreparedProperties):
                with self.assertRaises(PreparedProperties):
                    server.main()
            self.assertEqual(b'owned launcher option fixture', (seed / 'server.jar').read_bytes())
            self.assertEqual('eula=true\n', (seed / 'eula.txt').read_text())
            return dict(line.split('=', 1) for line in (run / 'server.properties').read_text().splitlines())

    def test_flat_world_preserves_117_and_modern_settings_before_child(self) -> None:
        v117 = {"layers": [{"height": 100, "block": "minecraft:stone"}], "biome": "minecraft:the_void", "structures": {"structures": {}}, "features": False, "lakes": False}
        modern = {"layers": [{"height": 164, "block": "minecraft:stone"}], "biome": "minecraft:the_void", "features": False, "lakes": False, "structure_overrides": []}
        for release, settings in (("1.17", v117), ("1.20.6", modern), ("26.3", modern)):
            with self.subTest(release=release):
                serialized = json.dumps(settings, separators=(',', ':'))
                properties = self.properties_for('--flat-world-settings', serialized, release=release)
                self.assertEqual('flat', properties['level-type'])
                self.assertEqual(serialized, properties['generator-settings'])
                self.assertEqual(settings, json.loads(properties['generator-settings']))

    def test_default_property_is_four(self) -> None:
        properties = self.properties_for()
        self.assertEqual('4', properties['view-distance'])
        self.assertEqual('4', properties['simulation-distance'])
        self.assertEqual('false', properties['online-mode'])
        self.assertEqual('127.0.0.1', properties['server-ip'])

    def test_explicit_property_is_eight(self) -> None:
        properties = self.properties_for('--view-distance', '8')
        self.assertEqual('8', properties['view-distance'])
        self.assertEqual('4', properties['simulation-distance'])

    def test_native_range_edges_are_accepted(self) -> None:
        for distance in (2, 32):
            with self.subTest(distance=distance):
                self.assertEqual(str(distance), self.properties_for('--view-distance', str(distance))['view-distance'])

    def test_out_of_range_cli_values_are_rejected(self) -> None:
        for distance in ('1', '33', 'bad'):
            with self.subTest(distance=distance), patch.object(sys, 'argv', ['server.py', '1.21.5', '--view-distance', distance]), patch.object(server, 'run') as run, redirect_stderr(StringIO()):
                with self.assertRaises(SystemExit) as error:
                    server.main()
                self.assertEqual(2, error.exception.code)
                run.assert_not_called()


if __name__ == '__main__':
    unittest.main()
