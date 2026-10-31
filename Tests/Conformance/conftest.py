"""Runs one SDK's harness (WBSKT_HARNESS, a command line) against the fake Wbskt."""

import asyncio
import json
import os
import shlex
from pathlib import Path
from typing import Any, Dict, List, Optional

import pytest

from fake_wbskt import FakeApi, FakeSocket

RELAY = {
    "command": "relay.set",
    "description": "Switch the relay",
    "parameters": [{"name": "on", "label": "On", "dataType": "boolean", "description": "", "isRequired": True}],
}


class Harness:
    """One SDK process. Commands go in on stdin and events come out on stdout, one JSON object a line."""

    def __init__(self, command: List[str], env: Dict[str, str]) -> None:
        self._command = command
        self._env = env
        self.process: Optional[asyncio.subprocess.Process] = None
        self.events: "asyncio.Queue[Dict[str, Any]]" = asyncio.Queue()
        self.log: List[Dict[str, Any]] = []
        self._reader: Optional["asyncio.Task[None]"] = None

    async def start(self) -> None:
        self.process = await asyncio.create_subprocess_exec(
            *self._command,
            stdin=asyncio.subprocess.PIPE,
            stdout=asyncio.subprocess.PIPE,
            env={**os.environ, **self._env},
        )
        self._reader = asyncio.ensure_future(self._read())

    async def _read(self) -> None:
        assert self.process is not None and self.process.stdout is not None
        while True:
            line = await self.process.stdout.readline()
            if not line:
                return
            try:
                event = json.loads(line)
            except ValueError:
                continue  # an SDK's own logging on stdout
            if isinstance(event, dict) and "event" in event:
                self.log.append(event)
                await self.events.put(event)

    async def command(self, op: str, **fields: Any) -> None:
        assert self.process is not None and self.process.stdin is not None
        self.process.stdin.write((json.dumps({"op": op, **fields}) + "\n").encode())
        await self.process.stdin.drain()

    async def expect(self, name: str, timeout: float = 20.0, **match: Any) -> Dict[str, Any]:
        deadline = asyncio.get_running_loop().time() + timeout
        while True:
            remaining = deadline - asyncio.get_running_loop().time()
            if remaining <= 0:
                raise AssertionError(f"no {name} event {match or ''} within {timeout}s; saw {self.log}")
            try:
                event = await asyncio.wait_for(self.events.get(), remaining)
            except asyncio.TimeoutError:
                continue
            if event["event"] == name and all(event.get(k) == v for k, v in match.items()):
                return event

    async def stop(self) -> None:
        if self.process is None or self.process.returncode is not None:
            return
        try:
            await self.command("stop")
            await asyncio.wait_for(self.process.wait(), 10)
        except (asyncio.TimeoutError, BrokenPipeError, ConnectionResetError):
            self.process.kill()
            await self.process.wait()
        if self._reader is not None:
            self._reader.cancel()


def harness_command() -> List[str]:
    command = os.environ.get("WBSKT_HARNESS")
    if not command:
        pytest.skip("set WBSKT_HARNESS to the SDK harness command line (see README.md)")
    return shlex.split(command)


@pytest.fixture
def api():
    fake = FakeApi()
    fake.start()
    yield fake
    fake.stop()


@pytest.fixture
async def socket_server():
    fake = FakeSocket()
    await fake.start()
    yield fake
    await fake.stop()


@pytest.fixture
async def device(api, socket_server, tmp_path: Path):
    """Starts harnesses for one device; each start shares the device's storage, like a reboot."""
    started: List[Harness] = []
    command = harness_command()

    async def start(pin: Optional[str] = "483920", commands: Optional[List[Dict[str, Any]]] = None) -> Harness:
        env = {
            "WBSKT_API_URL": api.url,
            "WBSKT_SOCKET_URL": socket_server.url,
            "WBSKT_DEVICE_NAME": "conformance-device",
            "WBSKT_POLICY_PIN": pin or "",
            "WBSKT_STORAGE": str(tmp_path / "credentials.json"),
            "WBSKT_COMMANDS": json.dumps(commands if commands is not None else [RELAY]),
        }
        harness = Harness(command, env)
        await harness.start()
        started.append(harness)
        return harness

    yield start

    for harness in started:
        await harness.stop()
