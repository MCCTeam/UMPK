"""Check launcher fixture preparation without executing a child or opening a socket."""

from contextlib import ExitStack, redirect_stderr, redirect_stdout
import gzip
import hashlib
from io import StringIO
import json
from pathlib import Path
from tempfile import TemporaryDirectory
import sys
import unittest
from unittest.mock import MagicMock, patch

import server


class PreparedLaunch(Exception):
    """Stop at the child boundary after checking the generated fixture."""


class LevelDataSeedTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = TemporaryDirectory(prefix='umpk-level-data-seed-')
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.seed = self.root / 'downloads' / '1.17'
        self.seed.mkdir(parents=True)
        (self.seed / 'server.jar').write_bytes(b'owned launcher fixture')
        (self.seed / 'eula.txt').write_text('eula=true\n', encoding='utf-8')
        self.metadata = self.root / 'native-metadata' / 'level.dat'
        self.metadata.parent.mkdir()
        self.payload = gzip.compress(b'\x0a\x00\x00\x00', mtime=0)
        self.metadata.write_bytes(self.payload)
        (self.metadata.parent / 'region').mkdir()
        (self.metadata.parent / 'region' / 'must-not-copy.mca').write_bytes(b'not metadata')
        self.digest = hashlib.sha256(self.payload).hexdigest()
        self.run = self.root / 'run'

    def invoke(self, *arguments: str, child=PreparedLaunch) -> None:
        command = ['server.py', '1.17', '--root', str(self.root), '--run-dir', str(self.run), '--smoke', *arguments]
        with ExitStack() as stack:
            stack.enter_context(patch.dict('os.environ', {}, clear=True))
            stack.enter_context(patch.object(sys, 'argv', command))
            self.ports = stack.enter_context(patch.object(server, 'check_port'))
            stack.enter_context(patch.object(server, 'java_for', return_value='never-executed'))
            self.child = stack.enter_context(patch.object(server.subprocess, 'Popen', side_effect=child))
            stack.enter_context(redirect_stderr(StringIO()))
            stack.enter_context(redirect_stdout(StringIO()))
            server.main()

    def seed_arguments(self) -> list[str]:
        return ['--level-data-seed', str(self.metadata), '--level-data-seed-sha256', self.digest]

    def test_copies_only_pinned_metadata_and_records_provenance(self) -> None:
        with self.assertRaises(PreparedLaunch):
            self.invoke(*self.seed_arguments())
        self.assertEqual(self.payload, (self.run / 'world' / 'level.dat').read_bytes())
        self.assertEqual(['level.dat'], sorted(path.name for path in (self.run / 'world').iterdir()))
        self.assertEqual(self.payload, self.metadata.read_bytes())
        self.assertEqual(b'not metadata', (self.metadata.parent / 'region' / 'must-not-copy.mca').read_bytes())
        self.assertEqual(b'owned launcher fixture', (self.seed / 'server.jar').read_bytes())
        receipt = json.loads((self.run / 'level-data-seed.json').read_text(encoding='utf-8'))
        self.assertEqual({'version': '1.17', 'source': str(self.metadata.resolve()), 'sha256': self.digest, 'bytes': len(self.payload), 'destination': 'world/level.dat'}, receipt)
        properties = (self.run / 'server.properties').read_text(encoding='utf-8')
        self.assertNotIn('level-type=', properties)
        self.assertNotIn('generator-settings=', properties)

    def test_rejects_hash_mismatch_before_creating_run(self) -> None:
        with self.assertRaisesRegex(ValueError, 'SHA256 mismatch'):
            self.invoke('--level-data-seed', str(self.metadata), '--level-data-seed-sha256', '0' * 64)
        self.child.assert_not_called()
        self.ports.assert_not_called()
        self.assertFalse(self.run.exists())
        self.assertEqual(self.payload, self.metadata.read_bytes())

    def test_requires_hash_for_metadata(self) -> None:
        with self.assertRaisesRegex(ValueError, 'together'):
            self.invoke('--level-data-seed', str(self.metadata))
        self.child.assert_not_called()
        self.assertFalse(self.run.exists())

    def test_requires_metadata_for_hash(self) -> None:
        with self.assertRaisesRegex(ValueError, 'together'):
            self.invoke('--level-data-seed-sha256', self.digest)
        self.child.assert_not_called()
        self.assertFalse(self.run.exists())

    def test_rejects_malformed_hash_before_creating_run(self) -> None:
        for digest in ('', 'a' * 63, 'g' * 64):
            with self.subTest(digest=digest), self.assertRaisesRegex(ValueError, '64 hexadecimal'):
                self.invoke('--level-data-seed', str(self.metadata), '--level-data-seed-sha256', digest)
            self.child.assert_not_called()
            self.assertFalse(self.run.exists())

    def test_rejects_directory_as_metadata(self) -> None:
        with self.assertRaises(IsADirectoryError):
            self.invoke('--level-data-seed', str(self.metadata.parent), '--level-data-seed-sha256', self.digest)
        self.child.assert_not_called()
        self.assertFalse(self.run.exists())

    def test_rejects_missing_metadata(self) -> None:
        with self.assertRaises(FileNotFoundError):
            self.invoke('--level-data-seed', str(self.metadata.with_name('missing.dat')), '--level-data-seed-sha256', self.digest)
        self.child.assert_not_called()
        self.assertFalse(self.run.exists())

    def test_rejects_combining_metadata_with_properties_generation(self) -> None:
        with self.assertRaisesRegex(ValueError, 'cannot be combined'):
            self.invoke(*self.seed_arguments(), '--flat-world-settings', '{}')
        self.child.assert_not_called()
        self.assertFalse(self.run.exists())

    def test_without_seed_preserves_default_preparation(self) -> None:
        with self.assertRaises(PreparedLaunch):
            self.invoke()
        self.assertFalse((self.run / 'world').exists())
        self.assertFalse((self.run / 'level-data-seed.json').exists())
        properties = (self.run / 'server.properties').read_text(encoding='utf-8')
        self.assertNotIn('level-type=', properties)
        self.assertNotIn('generator-settings=', properties)
        self.assertIn('view-distance=4\n', properties)


class FlatRegistryFailureTests(unittest.TestCase):
    def invoke(self, request_flat: bool) -> tuple[Path, MagicMock, MagicMock]:
        temporary = TemporaryDirectory(prefix='umpk-registry-error-')
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        seed = root / 'downloads' / '1.17'
        seed.mkdir(parents=True)
        (seed / 'server.jar').write_bytes(b'owned launcher fixture')
        (seed / 'eula.txt').write_text('eula=true\n', encoding='utf-8')
        run = root / 'run'
        command = ['server.py', '1.17', '--root', str(root), '--run-dir', str(run), '--smoke']
        if request_flat:
            command.extend(['--flat-world-settings', '{}'])
        process = MagicMock()
        process.__enter__.return_value = process
        process.poll.return_value = None
        process.pid = 12345
        process.returncode = 0

        def child(*args, **kwargs):
            kwargs['stdout'].write('[main/ERROR]: Not a registry ops\n[Server thread/INFO]: Done (0.1s)!\n')
            kwargs['stdout'].flush()
            return process

        with ExitStack() as stack:
            stack.enter_context(patch.dict('os.environ', {}, clear=True))
            stack.enter_context(patch.object(sys, 'argv', command))
            stack.enter_context(patch.object(server, 'check_port'))
            stack.enter_context(patch.object(server, 'java_for', return_value='never-executed'))
            stack.enter_context(patch.object(server.subprocess, 'Popen', side_effect=child))
            stop = stack.enter_context(patch.object(server, 'stop'))
            self.connection = stack.enter_context(patch.object(server.socket, 'create_connection'))
            stack.enter_context(patch.object(server, 'rcon', return_value='There are 0 players'))
            stack.enter_context(redirect_stdout(StringIO()))
            self.run = run
            self.stop = stop
            self.process = process
            server.main()
        return run, stop, process

    def test_rejects_native_registry_codec_error_before_flat_readiness(self) -> None:
        with self.assertRaisesRegex(RuntimeError, 'Not a registry ops'):
            self.invoke(True)
        self.assertFalse((self.run / 'ready.json').exists())
        self.assertEqual({'pid': 12345, 'exit_code': 0}, json.loads((self.run / 'stopped.json').read_text(encoding='utf-8')))
        self.stop.assert_called_once_with(self.process)
        self.connection.assert_not_called()

    def test_unrequested_flat_error_does_not_change_default_readiness(self) -> None:
        run, stop, process = self.invoke(False)
        self.assertTrue((run / 'ready.json').is_file())
        stop.assert_called_once_with(process)


if __name__ == '__main__':
    unittest.main()
