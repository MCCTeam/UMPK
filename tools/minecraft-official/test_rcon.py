"""Keep reconnects before authentication separate from command execution."""

from contextlib import contextmanager
from concurrent.futures import ThreadPoolExecutor
import socket
import struct
from threading import Event
import unittest

from server import rcon, receive_exact


def request(connection):
    length = struct.unpack('<i', receive_exact(connection, 4))[0]
    payload = receive_exact(connection, length)
    return (*struct.unpack('<ii', payload[:8]), payload[8:-2].decode())


def response(connection, identifier, kind, body=''):
    encoded = body.encode()
    connection.sendall(struct.pack('<iii', len(encoded) + 10, identifier, kind) + encoded + b'\0\0')


def reset(connection):
    connection.setsockopt(socket.SOL_SOCKET, socket.SO_LINGER, struct.pack('ii', 1, 0))


@contextmanager
def peer(handler):
    stopped = Event()
    frames = []
    with socket.socket() as listener, ThreadPoolExecutor(max_workers=1) as pool:
        listener.bind(('127.0.0.1', 0))
        listener.listen()
        listener.settimeout(.1)

        def serve():
            attempt = 0
            while not stopped.is_set():
                try:
                    connection, _ = listener.accept()
                except TimeoutError:
                    continue
                with connection:
                    connection.settimeout(2)
                    attempt += 1
                    handler(connection, attempt, frames)

        worker = pool.submit(serve)
        try:
            yield listener.getsockname()[1], frames
        finally:
            stopped.set()
            worker.result(timeout=5)


class RconReconnectTests(unittest.TestCase):
    def test_authentication_resets_reconnect_before_sending_one_command(self):
        def handle(connection, attempt, frames):
            frames.append(request(connection))
            if attempt < 3:
                reset(connection)
                return
            response(connection, 1, 0)
            response(connection, 1, 2)
            frames.append(request(connection))
            response(connection, 2, 0, 'placed')

        with peer(handle) as (port, frames):
            self.assertEqual('placed', rcon(port, 'local-test', 'setblock 0 4 2 stone'))
        self.assertEqual([(1, 3, 'local-test')] * 3 + [(2, 2, 'setblock 0 4 2 stone')], frames)

    def test_authentication_reconnects_stop_after_three_attempts(self):
        def handle(connection, attempt, frames):
            frames.append(request(connection))
            reset(connection)

        with peer(handle) as (port, frames):
            with self.assertRaises(ConnectionResetError):
                rcon(port, 'local-test', 'give ParityBot stone 32')
        self.assertEqual([(1, 3, 'local-test')] * 3, frames)

    def test_lost_command_response_does_not_replay_the_command(self):
        def handle(connection, attempt, frames):
            frames.append(request(connection))
            response(connection, 1, 2)
            frames.append(request(connection))
            reset(connection)

        with peer(handle) as (port, frames):
            with self.assertRaises(ConnectionResetError):
                rcon(port, 'local-test', 'give ParityBot stone 32')
        self.assertEqual([(1, 3, 'local-test'), (2, 2, 'give ParityBot stone 32')], frames)

    def test_rejected_credentials_are_not_retried(self):
        def handle(connection, attempt, frames):
            frames.append(request(connection))
            response(connection, -1, 2)

        with peer(handle) as (port, frames):
            with self.assertRaises(PermissionError):
                rcon(port, 'local-test', 'list')
        self.assertEqual([(1, 3, 'local-test')], frames)

    def test_malformed_authentication_is_not_retried(self):
        def handle(connection, attempt, frames):
            frames.append(request(connection))
            connection.sendall(struct.pack('<i', 9))

        with peer(handle) as (port, frames):
            with self.assertRaisesRegex(ValueError, 'frame length'):
                rcon(port, 'local-test', 'list')
        self.assertEqual([(1, 3, 'local-test')], frames)

    def test_truncated_authentication_is_not_retried(self):
        def handle(connection, attempt, frames):
            frames.append(request(connection))
            connection.sendall(struct.pack('<i', 10) + b'abc')

        with peer(handle) as (port, frames):
            with self.assertRaisesRegex(ConnectionError, 'truncated frame'):
                rcon(port, 'local-test', 'list')
        self.assertEqual([(1, 3, 'local-test')], frames)
