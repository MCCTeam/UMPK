"""Offline checks for artifact integrity, release selection, and protocol framing."""

import hashlib
from pathlib import Path
import socket
import tempfile
import unittest
import zipfile

from official import download, inner_server, select_releases, minecraft_classes
from server import receive_exact


class ArtifactTests(unittest.TestCase):
    def test_hash_failure_preserves_existing_artifact(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / 'source'
            source.write_bytes(b'new')
            target = root / 'target'
            target.write_bytes(b'old')
            with self.assertRaises(ValueError):
                download(source.as_uri(), target, '0' * 40, 'sha1')
            self.assertEqual(b'old', target.read_bytes())
            self.assertFalse((root / 'target.part').exists())

    def test_verified_download_replaces_artifact(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / 'source'
            source.write_bytes(b'new')
            target = root / 'target'
            download(source.as_uri(), target, hashlib.sha1(b'new').hexdigest(), 'sha1')
            self.assertEqual(b'new', target.read_bytes())

    def test_range_excludes_prereleases_and_later_patches(self):
        versions = [{'id': version, 'type': kind} for version, kind in [('1.7.10', 'release'), ('1.8', 'release'), ('26.3', 'release'), ('26.3.1', 'release'), ('26.3-rc1', 'snapshot')]]
        self.assertEqual(['1.8', '26.3'], [entry['id'] for entry in select_releases(versions, '1.8', '26.3')])

    def test_unsafe_bundle_path_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            jar = root / 'server.jar'
            with zipfile.ZipFile(jar, 'w') as archive:
                archive.writestr('META-INF/versions.list', '0\t1.0\t../../outside.jar\n')
            with self.assertRaises(ValueError):
                inner_server(jar, root / 'inner.jar')

    def test_bundled_server_is_hash_verified(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            jar = root / 'server.jar'
            with zipfile.ZipFile(jar, 'w') as archive:
                archive.writestr('META-INF/versions.list', hashlib.sha256(b'inner').hexdigest() + '\t1.0\t1.0/server.jar\n')
                archive.writestr('META-INF/versions/1.0/server.jar', b'inner')
            self.assertEqual(b'inner', inner_server(jar, root / 'inner.jar').read_bytes())


    def test_source_scope_excludes_embedded_third_party_classes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            original = root / 'original.jar'
            with zipfile.ZipFile(original, 'w') as archive:
                for name in ['net/minecraft/Packet.class', 'com/mojang/math/Vector.class', 'abc.class', 'it/unimi/dsi/fastutil/Huge.class']:
                    archive.writestr(name, b'bytecode')
            filtered = minecraft_classes(original, root / 'game.jar')
            with zipfile.ZipFile(filtered) as archive:
                self.assertEqual({'net/minecraft/Packet.class', 'com/mojang/math/Vector.class', 'abc.class'}, set(archive.namelist()))
            with zipfile.ZipFile(original) as archive:
                self.assertIn('it/unimi/dsi/fastutil/Huge.class', archive.namelist())


class RconTests(unittest.TestCase):
    def test_truncated_frame_reports_eof(self):
        reader, writer = socket.socketpair()
        with reader, writer:
            writer.sendall(b'abc')
            writer.shutdown(socket.SHUT_WR)
            with self.assertRaises(ConnectionError):
                receive_exact(reader, 4)

    def test_frame_reader_collects_partial_bytes(self):
        reader, writer = socket.socketpair()
        with reader, writer:
            writer.sendall(b'ab')
            writer.sendall(b'cd')
            self.assertEqual(b'abcd', receive_exact(reader, 4))


if __name__ == '__main__':
    unittest.main()
