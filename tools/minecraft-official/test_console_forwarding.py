"""Exercise the real launcher with owned pipes, sockets, and a tiny child process."""

from contextlib import contextmanager
import json
import os
from pathlib import Path
import select
import signal
import socket
import subprocess
import sys
from tempfile import TemporaryDirectory
import time
import unittest


CHILD = r'''#!/usr/bin/env python3
import json
import os
from pathlib import Path
import signal
import socket
import struct
import sys
from threading import Thread

def terminate(signum, frame):
    Path('terminated.json').write_text(json.dumps({'signal': signum}))
    raise SystemExit(0)

signal.signal(signal.SIGTERM, terminate)
properties = dict(line.split('=', 1) for line in Path('server.properties').read_text().splitlines())

def receive(connection, size):
    result = b''
    while len(result) < size:
        chunk = connection.recv(size - len(result))
        if not chunk:
            raise ConnectionError('readiness peer closed')
        result += chunk
    return result

def respond(connection, identifier, kind, body):
    encoded = body.encode('utf-8')
    connection.sendall(struct.pack('<iii', len(encoded) + 10, identifier, kind) + encoded + b'\0\0')

def readiness():
    with rcon_socket.accept()[0] as connection:
        length = struct.unpack('<i', receive(connection, 4))[0]
        auth = receive(connection, length)
        assert auth[8:-2].decode() == properties['rcon.password']
        respond(connection, 1, 2, '')
        length = struct.unpack('<i', receive(connection, 4))[0]
        command = receive(connection, length)
        assert command[8:-2] == b'list'
        respond(connection, 2, 0, 'owned console fixture ready')

with socket.socket() as game_socket, socket.socket() as rcon_socket:
    game_socket.bind(('127.0.0.1', int(properties['server-port'])))
    game_socket.listen()
    rcon_socket.bind(('127.0.0.1', int(properties['rcon.port'])))
    rcon_socket.listen()
    Thread(target=readiness, daemon=True).start()
    print('Done (owned console fixture)', flush=True)
    pending = b''
    with Path('received.bin').open('wb', buffering=0) as received:
        while True:
            chunk = os.read(sys.stdin.fileno(), 4096)
            if not chunk:
                Path('eof.json').write_text(json.dumps({'received': received.tell()}))
                if os.environ.get('CONSOLE_FIXTURE_KEEP_ALIVE') == '1':
                    while True:
                        signal.pause()
                break
            received.write(chunk)
            pending += chunk
            while b'\n' in pending:
                line, pending = pending.split(b'\n', 1)
                if line == b'stop':
                    raise SystemExit(0)
'''


def wait_for(predicate, timeout=3):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if predicate():
            return True
        time.sleep(.01)
    return predicate()


class OwnedLauncher:
    def __init__(self, root, *, keep_alive=False, smoke=False, arguments=()):
        seed = root / 'downloads' / '1.8.9'
        seed.mkdir(parents=True)
        (seed / 'server.jar').write_bytes(b'owned non-Java fixture')
        (seed / 'eula.txt').write_text('eula=true\n', encoding='utf-8')
        executable = root / 'owned-child'
        executable.write_text(CHILD, encoding='utf-8')
        executable.chmod(0o700)
        with socket.socket() as first, socket.socket() as second:
            first.bind(('127.0.0.1', 0))
            second.bind(('127.0.0.1', 0))
            self.ports = first.getsockname()[1], second.getsockname()[1]
        self.directory = root / 'run'
        environment = os.environ.copy()
        for name in ('MINECRAFT_SERVER_ROOT', 'UMPK_SERVER_ROOT', 'MCC_SERVERS'):
            environment.pop(name, None)
        environment['CONSOLE_FIXTURE_KEEP_ALIVE'] = '1' if keep_alive else '0'
        command = [sys.executable, str(Path(__file__).with_name('server.py')), '1.8.9', '--root', str(root), '--run-dir', str(self.directory), '--port', str(self.ports[0]), '--rcon-port', str(self.ports[1]), '--java', str(executable), '--timeout', '8']
        command.extend(arguments)
        if smoke:
            command.append('--smoke')
        self.process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, env=environment)
        self.output = bytearray()

    def ready(self):
        deadline = time.monotonic() + 10
        while time.monotonic() < deadline:
            readable, _, _ = select.select([self.process.stdout], [], [], .1)
            if readable:
                chunk = os.read(self.process.stdout.fileno(), 4096)
                self.output.extend(chunk)
                if b'READY 1.8.9 ' in self.output:
                    return
                if not chunk:
                    break
        raise AssertionError(f'Launcher did not become ready: {self.output.decode(errors="replace")}')

    def send(self, data):
        self.process.stdin.write(data)
        self.process.stdin.flush()

    def received(self):
        path = self.directory / 'received.bin'
        return path.read_bytes() if path.exists() else b''

    def finish(self, timeout=4):
        self.process.wait(timeout=timeout)
        self.output.extend(self.process.stdout.read())
        return self.process.returncode

    def cleanup(self):
        if self.process.poll() is None:
            if not self.process.stdin.closed:
                try:
                    self.send(b'\nstop\n')
                    self.process.stdin.close()
                except (BrokenPipeError, OSError):
                    pass
            try:
                self.process.wait(timeout=2)
            except subprocess.TimeoutExpired:
                ready = self.directory / 'ready.json'
                if ready.exists():
                    child_pid = json.loads(ready.read_text())['pid']
                    try:
                        os.kill(child_pid, signal.SIGTERM)
                    except ProcessLookupError:
                        pass
                try:
                    self.process.wait(timeout=3)
                except subprocess.TimeoutExpired:
                    self.process.terminate()
                    self.process.wait(timeout=5)
        self.process.stdin.close()
        self.process.stdout.close()


@contextmanager
def launcher(**options):
    with TemporaryDirectory(prefix='umpk-console-test-') as temporary:
        owned = OwnedLauncher(Path(temporary), **options)
        try:
            owned.ready()
            yield owned
        finally:
            owned.cleanup()


@unittest.skipUnless(os.name == 'posix', 'The launcher uses POSIX selectable console pipes')
class ConsoleForwardingTests(unittest.TestCase):
    def assert_stopped(self, owned, exit_code):
        self.assertEqual(exit_code, owned.finish(), owned.output.decode(errors='replace'))
        ready = json.loads((owned.directory / 'ready.json').read_text())
        stopped = json.loads((owned.directory / 'stopped.json').read_text())
        self.assertEqual({'pid': ready['pid'], 'exit_code': 0}, stopped)
        with self.assertRaises(ProcessLookupError):
            os.kill(ready['pid'], 0)
        for port in owned.ports:
            with socket.socket() as probe:
                self.assertNotEqual(0, probe.connect_ex(('127.0.0.1', port)))

    def test_two_queued_commands_arrive_while_input_stays_open(self):
        with launcher() as owned:
            commands = b'time set day\nsay ordered-completion-marker\n'
            owned.send(commands)
            arrived = wait_for(lambda: owned.received() == commands)
            self.assertFalse(owned.process.stdin.closed)
            self.assertTrue(arrived, f'Child received {owned.received()!r}, expected {commands!r} before EOF')
            owned.send(b'stop\n')
            self.assert_stopped(owned, 0)

    def test_partial_lines_and_split_utf8_arrive_without_newline_or_decoding(self):
        with launcher() as owned:
            chunks = [b'say Gr\xc3', b'\xbc\xc3', b'\x9fe ', b'\xf0\x9f', b'\x8c\x8d\n']
            expected = b''
            observations = []
            for chunk in chunks:
                expected += chunk
                owned.send(chunk)
                observations.append(wait_for(lambda: owned.received() == expected, timeout=.25))
            self.assertEqual('say Grüße 🌍\n', expected.decode('utf-8'))
            self.assertEqual([True] * len(chunks), observations, f'Raw pipe fragments were buffered: {observations}')
            self.assertEqual(expected, owned.received())
            owned.send(b'stop\n')
            self.assert_stopped(owned, 0)

    def test_eof_delivers_trailing_partial_input_and_closes_child_stdin(self):
        with launcher() as owned:
            command = 'say final Grüße 🌍'.encode('utf-8')
            owned.send(command)
            owned.process.stdin.close()
            self.assertTrue(wait_for(lambda: (owned.directory / 'eof.json').exists()), f'EOF did not reach child; received {owned.received()!r}')
            self.assertEqual(command, owned.received())
            self.assert_stopped(owned, 0)

    def test_termination_after_eof_cleans_up_a_child_that_keeps_running(self):
        with launcher(keep_alive=True) as owned:
            owned.process.stdin.close()
            self.assertTrue(wait_for(lambda: (owned.directory / 'eof.json').exists()), 'EOF did not reach child')
            owned.process.send_signal(signal.SIGTERM)
            self.assert_stopped(owned, 130)
            self.assertEqual({'signal': signal.SIGTERM}, json.loads((owned.directory / 'terminated.json').read_text()))

    def test_smoke_readiness_still_stops_the_owned_child(self):
        with launcher(smoke=True) as owned:
            self.assert_stopped(owned, 0)
            self.assertEqual(b'stop\n', owned.received())


if __name__ == '__main__':
    unittest.main()
