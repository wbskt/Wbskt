"""The checklist scenarios that need no misbehaviour on cue, run against the real hosts.

Skipped unless WBSKT_LIVE_* is set; nightly-conformance.yml sets it after starting the hosts. The
fake records frames; here the platform's own records (client detail, message types) are the evidence,
read through the management API with a user token.

  WBSKT_LIVE_API_URL     the management host the SDK registers and signs in with
  WBSKT_LIVE_SOCKET_URL  the WebSocket gateway
  WBSKT_LIVE_TOKEN       a user access token with the clients.* permissions in the workspace
  WBSKT_LIVE_WORKSPACE   that workspace's reference
  WBSKT_LIVE_PIN         an auto-approving registration policy's PIN in it
"""

import asyncio
import json
import os
import urllib.request
import uuid
from pathlib import Path
from typing import Any, Callable, Dict, List, Optional

import pytest

from conftest import RELAY, Harness, harness_command

LIVE = {name: os.environ.get(f"WBSKT_LIVE_{name}", "") for name in ("API_URL", "SOCKET_URL", "TOKEN", "WORKSPACE", "PIN")}

pytestmark = pytest.mark.skipif(not all(LIVE.values()), reason="set WBSKT_LIVE_* to run against real hosts")

CONNECT = 30.0


def _call(method: str, path: str, body: Optional[Dict[str, Any]] = None) -> Any:
    url = f"{LIVE['API_URL'].rstrip('/')}/api/v1/workspaces/{LIVE['WORKSPACE']}{path}"
    data = json.dumps(body).encode() if body is not None else None
    request = urllib.request.Request(url, data=data, method=method)
    request.add_header("Authorization", f"Bearer {LIVE['TOKEN']}")
    if data is not None:
        request.add_header("Content-Type", "application/json")
    with urllib.request.urlopen(request, timeout=10) as response:
        text = response.read()
    return json.loads(text) if text else None


async def api(method: str, path: str, body: Optional[Dict[str, Any]] = None) -> Any:
    return await asyncio.get_running_loop().run_in_executor(None, _call, method, path, body)


async def eventually(read: Callable[[], Any], check: Callable[[Any], bool], timeout: float = 30.0) -> Any:
    """Reads until check passes: the platform records some things in batches."""
    deadline = asyncio.get_running_loop().time() + timeout
    while True:
        value = await read()
        if check(value):
            return value
        if asyncio.get_running_loop().time() > deadline:
            raise AssertionError(f"still {value!r} after {timeout}s")
        await asyncio.sleep(1.0)


@pytest.fixture
async def live_client(tmp_path: Path):
    """Starts harnesses for one fresh client on the real hosts; each start shares its storage."""
    started: List[Harness] = []
    command = harness_command()
    name = f"conformance-{uuid.uuid4().hex[:12]}"

    async def start() -> Harness:
        env = {
            "WBSKT_API_URL": LIVE["API_URL"],
            "WBSKT_SOCKET_URL": LIVE["SOCKET_URL"],
            "WBSKT_CLIENT_NAME": name,
            "WBSKT_POLICY_PIN": LIVE["PIN"],
            "WBSKT_STORAGE": str(tmp_path / "credentials.json"),
            "WBSKT_COMMANDS": json.dumps([RELAY]),
        }
        harness = Harness(command, env)
        await harness.start()
        started.append(harness)
        return harness

    start.name = name  # type: ignore[attr-defined]
    yield start

    for harness in started:
        await harness.stop()


async def client_ref(name: str) -> str:
    page = await eventually(lambda: api("GET", f"/clients?name={name}"), lambda p: len(p["items"]) == 1)
    return page["items"][0]["clientRefId"]


async def test_live_registers_connects_and_announces_capabilities(live_client):
    harness = await live_client()
    await harness.expect("connected", timeout=CONNECT)
    ref = await client_ref(live_client.name)

    detail = await eventually(lambda: api("GET", f"/clients/{ref}"), lambda d: d["isConnected"] and d["agentName"])

    assert [c["command"] for c in detail["capabilities"]] == ["relay.set"]


async def test_live_a_backlog_past_the_burst_arrives_whole(live_client):
    harness = await live_client()
    await harness.expect("connected", timeout=CONNECT)
    ref = await client_ref(live_client.name)

    await harness.command("send-many", type="live.reading", count=150)
    await harness.expect("sent", type="live.reading", count=150)

    def counted(page: Any) -> int:
        return next((t["count"] for t in page["items"] if t["type"] == "live.reading"), 0)

    page = await eventually(lambda: api("GET", f"/clients/{ref}/message-types"), lambda p: counted(p) >= 150, 60.0)
    assert counted(page) == 150
    assert not [e for e in harness.log if e["event"] == "disconnected"]


async def test_live_a_command_is_acked_and_survives_a_throwing_handler(live_client):
    harness = await live_client()
    await harness.expect("connected", timeout=CONNECT)
    ref = await client_ref(live_client.name)

    await harness.command("throw-next")
    await harness.expect("armed")
    first = await api("POST", f"/clients/{ref}/commands", {"type": "relay.set", "payload": json.dumps({"on": True})})
    await harness.expect("message", type="relay.set", commandId=first["commandId"])
    second = await api("POST", f"/clients/{ref}/commands", {"type": "relay.set", "payload": json.dumps({"on": False})})
    await harness.expect("message", type="relay.set", commandId=second["commandId"])

    assert first["status"] == second["status"] == "sent"
    assert not [e for e in harness.log if e["event"] == "disconnected"]


async def test_live_answers_a_ping(live_client):
    harness = await live_client()
    await harness.expect("connected", timeout=CONNECT)
    ref = await client_ref(live_client.name)

    await api("POST", f"/clients/{ref}/ping")

    await eventually(lambda: api("GET", f"/clients/{ref}"), lambda d: d["lastRttMs"] is not None)


async def test_live_a_second_connection_supersedes_the_first(live_client):
    first = await live_client()
    await first.expect("connected", timeout=CONNECT)

    second = await live_client()
    await second.expect("connected", timeout=CONNECT)
    await first.expect("disconnected")
    await second.stop()

    await first.expect("connected", timeout=CONNECT)
