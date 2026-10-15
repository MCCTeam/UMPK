#!/usr/bin/env python3
"""Start one owned vanilla server in a fresh directory, or send a local RCON command."""

import argparse
import json
import os
from pathlib import Path
import secrets
import select
import signal
import shutil
import socket
import struct
import subprocess
import sys
import tempfile
import time

from official import default_root, java_for


def receive_exact(connection: socket.socket, length: int) -> bytes:
    result = bytearray()
    while len(result) < length:
        chunk = connection.recv(length - len(result))
        if not chunk:
            raise ConnectionError('RCON peer closed a truncated frame')
        result.extend(chunk)
    return bytes(result)


def rcon(port: int, password: str, command: str) -> str:
    with socket.create_connection(('127.0.0.1', port), timeout=5) as connection:
        connection.settimeout(5)

        def send(identifier: int, kind: int, body: str) -> None:
            encoded = body.encode('utf-8')
            connection.sendall(struct.pack('<iii', len(encoded) + 10, identifier, kind) + encoded + b'\0\0')

        def receive() -> tuple[int, int, str]:
            length = struct.unpack('<i', receive_exact(connection, 4))[0]
            if not 10 <= length <= 4 * 1024 * 1024:
                raise ValueError('Invalid RCON frame length')
            payload = receive_exact(connection, length)
            if payload[-2:] != b'\0\0':
                raise ValueError('Invalid RCON frame terminator')
            identifier, kind = struct.unpack('<ii', payload[:8])
            return identifier, kind, payload[8:-2].decode('utf-8')

        send(1, 3, password)
        identifier, kind, _ = receive()
        if kind == 0 and identifier == 1:
            identifier, kind, _ = receive()
        if identifier != 1 or kind != 2:
            raise PermissionError('RCON authentication failed')
        send(2, 2, command)
        identifier, kind, body = receive()
        if identifier != 2 or kind != 0:
            raise ValueError('Unexpected RCON command response')
        return body


def check_port(port: int) -> None:
    with socket.socket() as probe:
        probe.bind(('127.0.0.1', port))


def stop(process: subprocess.Popen) -> None:
    if process.poll() is not None:
        return
    try:
        process.stdin.write('stop\n')
        process.stdin.flush()
    except (BrokenPipeError, OSError):
        process.terminate()
    try:
        process.wait(timeout=20)
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait(timeout=10)


def run(args: argparse.Namespace) -> None:
    root = args.root.expanduser().resolve()
    seed_root = Path(os.environ.get('MINECRAFT_SERVER_ROOT') or os.environ.get('UMPK_SERVER_ROOT') or os.environ.get('MCC_SERVERS') or root / 'downloads').expanduser().resolve()
    seed = seed_root / args.version
    if not (seed / 'server.jar').is_file():
        raise FileNotFoundError(f'Server jar is absent: {seed / "server.jar"}')
    eula = seed / 'eula.txt'
    if not args.accept_eula and (not eula.is_file() or 'eula=true' not in eula.read_text(encoding='utf-8').splitlines()):
        raise ValueError('Accept the Minecraft EULA with --accept-eula, or provision an accepted seed.')
    if not 1024 <= args.port <= 65535 or not 1024 <= args.rcon_port <= 65535 or args.port == args.rcon_port:
        raise ValueError('Select two distinct ports from 1024 through 65535')
    check_port(args.port)
    check_port(args.rcon_port)
    java = java_for(root, args.version, args.java)
    if args.run_dir:
        directory = args.run_dir.expanduser().resolve()
        directory.mkdir(parents=True, exist_ok=False)
    else:
        directory = Path(tempfile.mkdtemp(prefix=f'minecraft-{args.version}-'))
    shutil.copyfile(seed / 'server.jar', directory / 'server.jar')
    password = secrets.token_hex(16)
    (directory / 'eula.txt').write_text('eula=true\n', encoding='utf-8')
    properties = {
        'server-ip': '127.0.0.1', 'server-port': str(args.port), 'online-mode': 'false',
        'white-list': 'false', 'enforce-whitelist': 'false', 'enforce-secure-profile': 'false',
        'enable-rcon': 'true', 'rcon.port': str(args.rcon_port), 'rcon.password': password,
        'enable-query': 'false', 'broadcast-rcon-to-ops': 'false', 'gamemode': 'creative',
        'use-native-transport': 'false',
        'spawn-protection': '0', 'view-distance': '4', 'simulation-distance': '4',
        'max-players': '5', 'level-name': 'world', 'max-tick-time': '-1',
    }
    (directory / 'server.properties').write_text(''.join(f'{key}={value}\n' for key, value in properties.items()), encoding='utf-8')
    (directory / 'rcon-password.txt').write_text(password + '\n', encoding='utf-8')
    (directory / 'rcon-password.txt').chmod(0o600)
    command = [java, '-Xms256M', '-Xmx1G', '-jar', 'server.jar', 'nogui']
    log_path = directory / 'server.log'
    print(f'run_dir={directory}', flush=True)
    with log_path.open('w', encoding='utf-8') as log, subprocess.Popen(command, cwd=directory, stdin=subprocess.PIPE, stdout=log, stderr=subprocess.STDOUT, text=True, start_new_session=True) as process:
        try:
            deadline = time.monotonic() + args.timeout
            while True:
                if process.poll() is not None:
                    raise RuntimeError(f'Server exited with {process.returncode}. Inspect {log_path}')
                if time.monotonic() >= deadline:
                    raise TimeoutError(f'Server readiness timed out. Inspect {log_path}')
                if 'Done (' in log_path.read_text(encoding='utf-8', errors='replace'):
                    try:
                        with socket.create_connection(('127.0.0.1', args.port), timeout=1):
                            pass
                        response = rcon(args.rcon_port, password, 'list')
                        break
                    except (OSError, ValueError):
                        pass
                time.sleep(0.5)
            result = {'version': args.version, 'pid': process.pid, 'java': java, 'port': args.port, 'rcon_port': args.rcon_port, 'run_dir': str(directory), 'rcon_list': response, 'command': command}
            (directory / 'ready.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
            print(f'READY {args.version} port={args.port} rcon={args.rcon_port}: {response}', flush=True)
            if not args.smoke:
                print('Send console commands on stdin. Send stop or press Ctrl+C to end this owned run.', flush=True)
                while process.poll() is None:
                    readable, _, _ = select.select([sys.stdin], [], [], 1)
                    if readable:
                        line = sys.stdin.readline()
                        if not line:
                            time.sleep(1)
                            continue
                        process.stdin.write(line)
                        process.stdin.flush()
            if process.poll() is not None and process.returncode != 0:
                raise RuntimeError(f'Server exited with {process.returncode}. Inspect {log_path}')
        finally:
            stop(process)
            (directory / 'stopped.json').write_text(json.dumps({'pid': process.pid, 'exit_code': process.returncode}, indent=2) + '\n', encoding='utf-8')
    print(f'STOPPED {args.version} exit_code={process.returncode}', flush=True)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('version', nargs='?')
    parser.add_argument('--root', type=Path, default=default_root())
    parser.add_argument('--run-dir', type=Path)
    parser.add_argument('--port', type=int, default=25565)
    parser.add_argument('--rcon-port', type=int, default=25575)
    parser.add_argument('--java')
    parser.add_argument('--accept-eula', action='store_true')
    parser.add_argument('--timeout', type=int, default=180)
    parser.add_argument('--smoke', action='store_true', help='prove server and RCON readiness, then stop')
    parser.add_argument('--rcon-command')
    parser.add_argument('--password-file', type=Path)
    args = parser.parse_args()
    if args.rcon_command:
        password = args.password_file.read_text(encoding='utf-8').strip() if args.password_file else os.environ.get('MINECRAFT_RCON_PASSWORD')
        if not password:
            parser.error('RCON requires --password-file or MINECRAFT_RCON_PASSWORD')
        print(rcon(args.rcon_port, password, args.rcon_command))
    else:
        if args.version is None:
            parser.error('A version is required to start a server')
        run(args)
    return 0


if __name__ == '__main__':
    def interrupt_owned_run(signum, frame):
        raise KeyboardInterrupt

    signal.signal(signal.SIGTERM, interrupt_owned_run)
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        raise SystemExit(130)
