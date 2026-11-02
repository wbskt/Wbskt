"""Firmware updates (Docs/Device.Protocol.md §4.4), for an SDK that takes them. A harness that does
turns them on when WBSKT_OTA is set; every scenario here is skipped for one whose device does not then
list "ota" under "system" in its capabilities.
"""

import asyncio
import os
from typing import Any, Dict, List

import pytest

from fake_wbskt import CLOSE_UPDATING

RECONNECT = 25.0

# Settled by the first scenario to start a device, so an SDK without updates pays for one start.
_takes_ota: Dict[str, bool] = {}

DIGEST = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08"


def ota_update(command_id: str, url: str = "https://firmware.example/fw.bin", **extra: Any) -> Dict[str, Any]:
    return {
        "type": "sys.ota.update",
        "payload": {"url": url, "sha256": DIGEST, "version": "1.1.0", "size": 1200000},
        "commandId": command_id,
        **extra,
    }


def system_of(capabilities: Dict[str, Any]) -> List[str]:
    payload = {k.lower(): v for k, v in capabilities["payload"].items()}
    return payload.get("system") or []


def firmware_version_of(capabilities: Dict[str, Any]) -> Any:
    payload = {k.lower(): v for k, v in capabilities["payload"].items()}
    return payload.get("firmwareversion")


async def next_ota_status(socket_server, timeout: float = RECONNECT) -> Dict[str, Any]:
    return (await socket_server.next_of("sys.ota.status", timeout=timeout))["payload"]


async def start_with_ota(device, socket_server, monkeypatch, version: str = "1.0.0"):
    harness, _ = await start_with_ota_capabilities(device, socket_server, monkeypatch, version)
    return harness


async def start_with_ota_capabilities(device, socket_server, monkeypatch, version: str = "1.0.0"):
    harness_command = os.environ.get("WBSKT_HARNESS", "")
    if _takes_ota.get(harness_command) is False:
        pytest.skip("this SDK does not take firmware updates")
    monkeypatch.setenv("WBSKT_OTA", version)
    harness = await device()
    await harness.expect("connected")
    capabilities = await socket_server.next_of("capabilities")
    _takes_ota[harness_command] = "ota" in system_of(capabilities)
    if not _takes_ota[harness_command]:
        pytest.skip("this SDK does not take firmware updates")
    return harness, capabilities


async def test_lists_ota_and_reports_the_firmware_version(api, socket_server, device, monkeypatch):
    _, capabilities = await start_with_ota_capabilities(device, socket_server, monkeypatch)

    assert firmware_version_of(capabilities) == "1.0.0"


async def test_rejects_an_update_while_ota_is_off(api, socket_server, device, monkeypatch):
    probe = await start_with_ota(device, socket_server, monkeypatch)
    await probe.stop()
    monkeypatch.delenv("WBSKT_OTA")

    harness = await device()
    await harness.expect("connected")
    capabilities = await socket_server.next_of("capabilities")
    assert "ota" not in system_of(capabilities)

    await socket_server.send(len(socket_server.connections) - 1, ota_update("o1"))
    ack = await socket_server.next_of("sys.ack")
    status = await next_ota_status(socket_server)

    assert ack["payload"] == {"commandId": "o1"}
    assert status["commandId"] == "o1"
    assert status["state"] == "rejected"
    assert status.get("error")


async def test_refuses_an_expired_update_and_starts_nothing(api, socket_server, device, monkeypatch):
    await start_with_ota(device, socket_server, monkeypatch)

    await socket_server.send(0, ota_update("o2", expiresAt="2020-01-01T00:00:00Z"))
    ack = await socket_server.next_of("sys.ack")
    await socket_server.send(0, {"type": "sys.ping", "payload": {"timestamp": "2026-10-31T12:00:00Z", "id": "after"}})
    seen = []
    while True:
        _, frame, _ = await socket_server.next_frame()
        if frame["type"] == "sys.pong":
            break
        seen.append(frame["type"])

    assert ack["payload"] == {"commandId": "o2", "type": "sys.ota.update", "refused": "expired"}
    assert "sys.ota.status" not in seen


async def test_rejects_an_update_over_plain_http(api, socket_server, device, monkeypatch):
    await start_with_ota(device, socket_server, monkeypatch)

    await socket_server.send(0, ota_update("o3", url="http://firmware.example/fw.bin"))
    status = await next_ota_status(socket_server)

    assert status["commandId"] == "o3"
    assert status["state"] == "rejected"
    assert status.get("error")


async def test_reports_accepted_downloading_and_installed_from_the_new_firmware(api, socket_server, device, monkeypatch):
    # WBSKT_OTA_INSTALL=ok has the harness install whatever it is sent and exit as if restarting.
    monkeypatch.setenv("WBSKT_OTA_INSTALL", "ok")
    old = await start_with_ota(device, socket_server, monkeypatch)

    await socket_server.send(0, ota_update("o4"))
    accepted = await next_ota_status(socket_server)
    await old.expect("restart")
    # A device that closed its connection to download said why, so the server shows it as updating.
    # One that kept it open until it restarted just drops it (1006).
    for _ in range(100):
        if 0 in socket_server.device_close_codes:
            break
        await asyncio.sleep(0.05)
    assert socket_server.device_close_codes.get(0) in (CLOSE_UPDATING, 1006)

    # A device may close the socket to download, so progress can arrive late or not at all.
    monkeypatch.setenv("WBSKT_OTA", "1.1.0")
    new = await device()
    await new.expect("connected", timeout=RECONNECT)
    # The new firmware's capabilities come first on its connection, before its statuses.
    capabilities = await socket_server.next_of("capabilities", timeout=RECONNECT)
    after = [await next_ota_status(socket_server)]
    while after[-1]["state"] != "installed":
        after.append(await next_ota_status(socket_server))

    assert (accepted["commandId"], accepted["state"]) == ("o4", "accepted")
    assert all(s["commandId"] == "o4" and s["state"] in ("downloading", "installed") for s in after)
    assert after[-1]["version"] == "1.1.0"
    assert firmware_version_of(capabilities) == "1.1.0"
    # Kept credentials: the new firmware signs in again, as the same client, without registering.
    assert len(api.registrations) == 1
    assert len({login["clientRefId"] for _, login in api.logins}) == 1
