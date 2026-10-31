import asyncio
from datetime import datetime, timedelta, timezone

import pytest

from conftest import eventually, make_client
from wbskt import Command, MemoryStorage, Parameter, RegistrationRequired, protocol
from wbskt.models import DataType


async def test_registers_once_and_stores_the_credentials(api, socket_server, no_jitter):
    storage = MemoryStorage()
    client = make_client(api, socket_server, no_jitter, storage=storage)
    await client.start()
    await socket_server.wait_connections(1)
    await client.stop()

    assert api.registrations == [{"pin": "483920", "name": "test-device"}]
    assert storage.load()[0] is not None

    second = make_client(api, socket_server, no_jitter, storage=storage)
    await second.start()
    await socket_server.wait_connections(2)
    await second.stop()

    assert len(api.registrations) == 1


async def test_keeps_the_credentials_while_approval_is_pending(api, socket_server, no_jitter):
    api.status = protocol.PENDING
    api.login_answers = [(401, {"code": "CLIENT_NOT_REGISTERED"}, {})]
    storage = MemoryStorage()
    client = make_client(api, socket_server, no_jitter, storage=storage)
    await client.start()
    await socket_server.wait_connections(1)
    await client.stop()

    assert len(api.registrations) == 1
    assert len(api.logins) == 2


async def test_reuses_the_token_across_reconnects(api, socket_server, no_jitter):
    client = make_client(api, socket_server, no_jitter)
    await client.start()
    first = await socket_server.wait_connections(1)
    await first.close()
    await socket_server.wait_connections(2)
    await client.stop()

    assert len(api.logins) == 1
    assert socket_server.tokens == ["token-1", "token-1"]


async def test_logs_in_again_when_the_socket_refuses_the_token(api, socket_server, no_jitter):
    socket_server.rejected_tokens.add("token-1")
    client = make_client(api, socket_server, no_jitter)
    await client.start()
    await socket_server.wait_connections(1)
    await client.stop()

    assert socket_server.tokens == ["token-1", "token-2"]


async def test_registers_again_when_the_credentials_are_no_longer_valid(api, socket_server, no_jitter):
    storage = MemoryStorage("old-ref", "old-secret")
    api.login_answers = [(401, {"code": "CLIENT_UNAUTHORIZED"}, {})]
    client = make_client(api, socket_server, no_jitter, storage=storage)
    await client.start()
    await socket_server.wait_connections(1)
    await client.stop()

    assert len(api.registrations) == 1
    assert storage.load()[0] != "old-ref"


async def test_waits_for_retry_after_when_throttled(api, socket_server, no_jitter):
    api.login_answers = [(429, {}, {"Retry-After": "0.3"})]
    client = make_client(api, socket_server, no_jitter)
    started = asyncio.get_running_loop().time()
    await client.start()
    await socket_server.wait_connections(1)
    await client.stop()

    assert asyncio.get_running_loop().time() - started >= 0.3


async def test_stops_without_a_pin_or_credentials(api, socket_server, no_jitter):
    client = make_client(api, socket_server, no_jitter)
    client.policy_pin = None
    await client.start()

    with pytest.raises(RegistrationRequired):
        await asyncio.wait_for(client.wait_closed(), 5)
    await client.stop()


async def test_sends_capabilities_first_then_the_backlog_in_order(api, socket_server, no_jitter):
    relay = Command("relay.set", "Switch the relay", [Parameter("on", DataType.BOOLEAN, is_required=True)])
    client = make_client(api, socket_server, no_jitter, capabilities=[relay])
    await client.send("temperature", {"celsius": 20})
    await client.send("temperature", {"celsius": 21})
    await client.start()

    capabilities = await socket_server.next()
    first = await socket_server.next()
    second = await socket_server.next()
    await client.stop()

    assert capabilities["type"] == protocol.CAPABILITIES
    assert capabilities["payload"]["agent"] == "wbskt-python"
    assert capabilities["payload"]["capabilities"] == [
        {
            "command": "relay.set",
            "description": "Switch the relay",
            "parameters": [{"name": "on", "label": "on", "dataType": "boolean", "description": "", "isRequired": True}],
        }
    ]
    assert [first["payload"], second["payload"]] == [{"celsius": 20}, {"celsius": 21}]
    assert first["sentAt"] <= second["sentAt"]


async def test_buffer_drops_the_oldest_when_full(api, socket_server, no_jitter):
    client = make_client(api, socket_server, no_jitter, offline_buffer_size=2)
    for n in range(3):
        await client.send("count", n)
    await client.start()

    await socket_server.next()
    sent = [(await socket_server.next())["payload"], (await socket_server.next())["payload"]]
    await client.stop()

    assert sent == [1, 2]


async def test_answers_a_ping_with_a_pong(api, socket_server, no_jitter):
    client = make_client(api, socket_server, no_jitter)
    await client.start()
    ws = await socket_server.wait_connections(1)
    await socket_server.next()
    await ws.send('{"type":"sys.ping","payload":{"timestamp":"2026-10-31T12:00:00.1234567Z","id":"p1"}}')

    pong = await socket_server.next()
    await client.stop()

    assert pong == {"type": "sys.pong", "payload": {"originalTimestamp": "2026-10-31T12:00:00.1234567Z", "id": "p1"}}


async def test_acks_a_command_before_handing_it_over(api, socket_server, no_jitter):
    received = []
    client = make_client(api, socket_server, no_jitter)
    client.on_message = received.append
    await client.start()
    ws = await socket_server.wait_connections(1)
    await socket_server.next()
    later = (datetime.now(timezone.utc) + timedelta(minutes=1)).isoformat()
    await ws.send(f'{{"type":"relay.set","payload":{{"on":true}},"commandId":"c1","expiresAt":"{later}"}}')

    ack = await socket_server.next()
    await eventually(lambda: received)
    await client.stop()

    assert ack == {"type": "sys.ack", "payload": {"commandId": "c1"}}
    assert received[0].type == "relay.set"
    assert received[0].payload == {"on": True}
    assert received[0].command_id == "c1"


async def test_refuses_an_expired_command(api, socket_server, no_jitter):
    received = []
    client = make_client(api, socket_server, no_jitter)
    client.on_message = received.append
    await client.start()
    ws = await socket_server.wait_connections(1)
    await socket_server.next()
    await ws.send('{"type":"relay.set","payload":{},"commandId":"c2","expiresAt":"2020-01-01T00:00:00Z"}')

    ack = await socket_server.next()
    await client.stop()

    assert ack == {"type": "sys.ack", "payload": {"commandId": "c2", "type": "relay.set", "refused": "expired"}}
    assert received == []


async def test_async_handlers_get_messages_in_order(api, socket_server, no_jitter):
    received = []

    async def handle(message):
        await asyncio.sleep(0.01)
        received.append(message.payload)

    client = make_client(api, socket_server, no_jitter)
    client.on_message = handle
    await client.start()
    ws = await socket_server.wait_connections(1)
    for n in range(5):
        await ws.send(f'{{"type":"tick","payload":{n}}}')
    await eventually(lambda: len(received) == 5)
    await client.stop()

    assert received == [0, 1, 2, 3, 4]


@pytest.mark.parametrize("message_type", ["sys.custom", "SYS.ping", "capabilities", "State.Report", "", "x" * 101])
async def test_rejects_a_type_it_may_not_send(api, socket_server, no_jitter, message_type):
    client = make_client(api, socket_server, no_jitter)

    with pytest.raises(ValueError):
        await client.send(message_type, {})


async def test_rejects_a_message_over_the_limit(api, socket_server, no_jitter):
    client = make_client(api, socket_server, no_jitter)

    with pytest.raises(ValueError):
        await client.send("blob", "x" * protocol.MAX_MESSAGE_BYTES)


async def test_reports_state(api, socket_server, no_jitter):
    client = make_client(api, socket_server, no_jitter)
    await client.start()
    await socket_server.wait_connections(1)
    await socket_server.next()
    await eventually(lambda: client.connected)
    await client.report_state({"humidity": 40})

    report = await socket_server.next()
    await client.stop()

    assert report["type"] == "state.report"
    assert report["payload"] == {"humidity": 40}


async def test_reconnects_quickly_after_a_restart_close(api, socket_server, no_jitter):
    client = make_client(api, socket_server, no_jitter)
    await client.start()
    first = await socket_server.wait_connections(1)
    await first.close(code=protocol.CLOSE_SERVER_RESTARTING, reason="restarting")

    await socket_server.wait_connections(2)
    await client.stop()
