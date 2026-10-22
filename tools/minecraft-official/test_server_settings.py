"""Check opt-in world settings through the real launcher and its owned non-Java child."""

import hashlib
import json
import os
import socket
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest

from test_console_forwarding import OwnedLauncher, launcher


FLAT_SETTINGS = {"layers": [{"height": 164, "block": "minecraft:stone"}], "biome": "minecraft:the_void", "features": False, "lakes": False, "structure_overrides": []}
FLAT_SERIALIZED = '{"layers":[{"height":164,"block":"minecraft:stone"}],"biome":"minecraft:the_void","features":false,"lakes":false,"structure_overrides":[]}'


@unittest.skipUnless(os.name == 'posix', 'The launcher uses POSIX selectable console pipes')
class WorldSettingsTests(unittest.TestCase):
    @staticmethod
    def expected_default_properties(owned) -> str:
        password = (owned.directory / 'rcon-password.txt').read_text(encoding='utf-8').strip()
        return ('server-ip=127.0.0.1\n'
                f'server-port={owned.ports[0]}\n'
                'online-mode=false\nwhite-list=false\nenforce-whitelist=false\nenforce-secure-profile=false\n'
                f'enable-rcon=true\nrcon.port={owned.ports[1]}\nrcon.password={password}\n'
                'enable-query=false\nbroadcast-rcon-to-ops=false\ngamemode=creative\n'
                'use-native-transport=false\nspawn-protection=0\nview-distance=4\nsimulation-distance=4\n'
                'max-players=5\nlevel-name=world\nmax-tick-time=-1\n')

    def assert_clean_stop(self, owned) -> None:
        self.assertEqual(0, owned.finish(), owned.output.decode(errors='replace'))
        ready = json.loads((owned.directory / 'ready.json').read_text(encoding='utf-8'))
        stopped = json.loads((owned.directory / 'stopped.json').read_text(encoding='utf-8'))
        self.assertEqual({'pid': ready['pid'], 'exit_code': 0}, stopped)
        with self.assertRaises(ProcessLookupError):
            os.kill(ready['pid'], 0)
        for port in owned.ports:
            with socket.socket() as probe:
                self.assertNotEqual(0, probe.connect_ex(('127.0.0.1', port)))

    def test_omitted_option_preserves_all_normal_properties_and_seed_files(self) -> None:
        with launcher() as owned:
            properties = (owned.directory / 'server.properties').read_text(encoding='utf-8')
            self.assertEqual(self.expected_default_properties(owned), properties)
            seed = owned.directory.parent / 'downloads' / '1.8.9'
            self.assertEqual(b'owned non-Java fixture', (seed / 'server.jar').read_bytes())
            self.assertEqual(hashlib.sha256((seed / 'server.jar').read_bytes()).digest(), hashlib.sha256((owned.directory / 'server.jar').read_bytes()).digest())
            self.assertEqual('eula=true\n', (seed / 'eula.txt').read_text(encoding='utf-8'))
            self.assertEqual({'server.jar', 'eula.txt'}, {path.name for path in seed.iterdir()})
            owned.send(b'stop\n')
            self.assert_clean_stop(owned)

    def test_invalid_or_nonobject_json_is_rejected_before_child_spawn(self) -> None:
        for value in ('{', '[1,2]', 'null', 'true', '42', '"object"', ''):
            with self.subTest(value=value), TemporaryDirectory(prefix='umpk-settings-rejection-') as temporary:
                owned = OwnedLauncher(Path(temporary), arguments=['--flat-world-settings', value])
                try:
                    self.assertEqual(2, owned.finish(), owned.output.decode(errors='replace'))
                    self.assertIn('Flat world settings must be a JSON object', owned.output.decode(errors='replace'))
                    self.assertFalse(owned.directory.exists(), 'Rejected settings must not create a run or start its child')
                    for port in owned.ports:
                        with socket.socket() as probe:
                            self.assertNotEqual(0, probe.connect_ex(('127.0.0.1', port)))
                finally:
                    owned.cleanup()

    def test_flat_world_is_an_explicit_serialized_option(self) -> None:
        with launcher(arguments=['--flat-world-settings', json.dumps(FLAT_SETTINGS)]) as owned:
            properties = (owned.directory / 'server.properties').read_text(encoding='utf-8')
            self.assertEqual(self.expected_default_properties(owned) + 'level-type=minecraft:flat\ngenerator-settings=' + FLAT_SERIALIZED + '\n', properties)
            self.assertEqual(FLAT_SETTINGS, json.loads(dict(line.split('=', 1) for line in properties.splitlines())['generator-settings']))
            owned.send(b'stop\n')
            self.assert_clean_stop(owned)


if __name__ == '__main__':
    unittest.main()
