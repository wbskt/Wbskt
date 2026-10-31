"""The device protocol checklist (Docs/Device.Protocol.md §6), run against one SDK's harness.

Delays here follow the protocol's own schedule: a reconnect after a drop can take up to 10 s and a
retry after a failure up to 2 s, 4 s, 8 s..., so waits are generous on purpose.
"""

import asyncio
from datetime import datetime, timedelta, timezone

import pytest

from conftest import RELAY

RECONNECT = 25.0


def lower_keys(value):
    """Capabilities are read ignoring case (protocol §4), so an SDK may send them in any case."""
    if isinstance(value, dict):
        return {k.lower(): lower_keys(v) for k, v in value.items()}
    if isinstance(value, list):
        return [lower_keys(v) for v in value]
    return value


def parse_time(value: str) -> datetime:
    text = value.replace("Z", "+00:00")
    if "." in text:
        head, rest = text.split(".", 1)
        digits = "".join(c for c in rest if c.isdigit())
        zone = rest[len(digits):]
        text = f"{head}.{digits[:6]}{zone}"
    parsed = datetime.fromisoformat(text)
    return parsed if parsed.tzinfo else parsed.replace(tzinfo=timezone.utc)


# Credentials


async def test_registers_once_and_signs_in_with_stored_credentials(api, socket_server, device):
    first = await device()
    await first.expect("connected")
    await first.stop()

    second = await device()
    await second.expect("connected")

    assert len(api.registrations) == 1
    assert api.registrations[0] == {"pin": "483920", "name": "conformance-device"}
    assert len(api.logins) == 2


async def test_keeps_the_credentials_while_approval_is_pending(api, socket_server, device):
    api.registered_status = 0
    api.login_answers = [(401, {"code": "CLIENT_NOT_REGISTERED"}, {})] * 2
    harness = await device()

    await harness.expect("connected", timeout=RECONNECT)

    assert len(api.registrations) == 1
    assert len(api.logins) == 3


async def test_registers_again_once_the_credentials_are_rejected(api, socket_server, device):
    first = await device()
    await first.expect("connected")
    await first.stop()
    api.forget_all_devices()

    second = await device()
    await second.expect("connected", timeout=RECONNECT)

    assert len(api.registrations) == 2


# Token


async def test_offers_the_subprotocol_and_a_bearer_token(api, socket_server, device):
    harness = await device()
    await harness.expect("connected")

    assert socket_server.subprotocols[0] == "wbskt.v1"
    assert socket_server.upgrade_tokens[0] == "token-1"


async def test_reuses_the_token_across_a_reconnect(api, socket_server, device):
    harness = await device()
    await harness.expect("connected")
    first = await socket_server.wait_connections(1)
    await first.close(1000)

    await socket_server.wait_connections(2, timeout=RECONNECT)

    assert len(api.logins) == 1
    assert socket_server.upgrade_tokens == ["token-1", "token-1"]


async def test_renews_the_token_after_the_upgrade_refuses_it(api, socket_server, device):
    socket_server.rejected_tokens.add("token-1")
    harness = await device()

    await harness.expect("connected", timeout=RECONNECT)

    assert len(api.logins) == 2
    assert socket_server.upgrade_tokens[-1] == "token-2"


async def test_waits_for_retry_after_when_throttled(api, socket_server, device):
    api.login_answers = [(429, {"code": "TOO_MANY_REQUESTS"}, {"Retry-After": "2"})]
    harness = await device()

    await harness.expect("connected", timeout=RECONNECT)

    assert api.logins[1][0] - api.logins[0][0] >= 2.0


# On the socket


async def test_sends_capabilities_first_on_every_connection(api, socket_server, device):
    harness = await device(commands=[RELAY])
    await harness.command("send", type="early", payload=1)
    await harness.expect("connected")
    index, frame, _ = await socket_server.next_frame()
    assert index == 0
    assert frame["type"] == "capabilities"
    capabilities = lower_keys(frame["payload"])
    assert capabilities["capabilities"][0]["command"] == "relay.set"
    assert capabilities["capabilities"][0]["parameters"][0]["datatype"] == "boolean"
    assert capabilities.get("agent")

    await socket_server.connections[0].close(1000)
    await socket_server.wait_connections(2, timeout=RECONNECT)
    while True:
        index, frame, _ = await socket_server.next_frame(RECONNECT)
        if index == 1:
            break
    assert frame["type"] == "capabilities"


async def test_answers_a_ping_with_a_pong(api, socket_server, device):
    harness = await device()
    await harness.expect("connected")
    await socket_server.next_of("capabilities")

    await socket_server.send(0, {"type": "sys.ping", "payload": {"timestamp": "2026-10-31T12:00:00.1234567Z", "id": "p1"}})
    pong = await socket_server.next_of("sys.pong")

    assert pong["payload"] == {"originalTimestamp": "2026-10-31T12:00:00.1234567Z", "id": "p1"}


async def test_acks_a_command_on_arrival_and_hands_it_over(api, socket_server, device):
    harness = await device()
    await harness.expect("connected")
    await socket_server.next_of("capabilities")
    expires = (datetime.now(timezone.utc) + timedelta(minutes=1)).isoformat().replace("+00:00", "Z")

    await socket_server.send(
        0, {"type": "relay.set", "payload": {"on": True}, "commandId": "c1", "expiresAt": expires}
    )
    ack = await socket_server.next_of("sys.ack")
    message = await harness.expect("message", type="relay.set")

    assert ack["payload"] == {"commandId": "c1"}
    assert message["payload"] == {"on": True}
    assert message["commandId"] == "c1"


async def test_refuses_an_expired_command_without_handing_it_over(api, socket_server, device):
    harness = await device()
    await harness.expect("connected")
    await socket_server.next_of("capabilities")

    await socket_server.send(
        0, {"type": "relay.set", "payload": {}, "commandId": "c2", "expiresAt": "2020-01-01T00:00:00Z"}
    )
    ack = await socket_server.next_of("sys.ack")
    await socket_server.send(0, {"type": "marker", "payload": None})
    await harness.expect("message", type="marker")

    assert ack["payload"] == {"commandId": "c2", "type": "relay.set", "refused": "expired"}
    assert not [e for e in harness.log if e["event"] == "message" and e.get("commandId") == "c2"]


async def test_hands_over_a_plain_message_without_an_ack(api, socket_server, device):
    harness = await device()
    await harness.expect("connected")
    await socket_server.next_of("capabilities")

    await socket_server.send(0, {"type": "hello", "payload": 5})
    message = await harness.expect("message", type="hello")
    await harness.command("send", type="after", payload=None)
    after = await socket_server.next_of("after")

    assert message["payload"] == 5
    assert message.get("commandId") is None
    assert after["type"] == "after"


# Sending


async def test_stamps_messages_and_sends_the_offline_backlog_in_order(api, socket_server, device):
    socket_server.down = True
    harness = await device()
    for n in range(3):
        await harness.command("send", type="reading", payload=n)
        await harness.expect("sent", type="reading")
    await asyncio.sleep(1.5)
    socket_server.down = False

    await harness.expect("connected", timeout=RECONNECT)
    capabilities = await socket_server.next_frame(RECONNECT)
    readings = [await socket_server.next_frame(RECONNECT) for _ in range(3)]

    assert capabilities[1]["type"] == "capabilities"
    assert [frame["payload"] for _, frame, _ in readings] == [0, 1, 2]
    for _, frame, arrived in readings:
        sent_at = parse_time(frame["sentAt"])
        assert sent_at.timestamp() <= arrived + 1
        assert arrived - sent_at.timestamp() >= 1.0


@pytest.mark.parametrize("message_type", ["sys.custom", "SYS.ping", "capabilities", "State.Report"])
async def test_refuses_to_send_a_reserved_type(api, socket_server, device, message_type):
    harness = await device()
    await harness.expect("connected")
    await socket_server.next_of("capabilities")

    await harness.command("send", type=message_type, payload={})
    await harness.expect("rejected", type=message_type)
    await harness.command("send", type="marker", payload=None)
    frame = await socket_server.next_of("marker")

    assert frame["type"] == "marker"
