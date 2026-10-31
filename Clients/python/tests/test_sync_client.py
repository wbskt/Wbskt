import asyncio
import threading

from websockets.asyncio.server import serve

from conftest import FakeSocket, NoJitter
from wbskt import MemoryStorage, ReconnectPolicy, WbsktClient, protocol


class ThreadedSocket:
    """The fake socket on its own loop, since a sync test leaves pytest's loop idle."""

    def __enter__(self) -> FakeSocket:
        self.loop = asyncio.new_event_loop()
        self.thread = threading.Thread(target=self.loop.run_forever, daemon=True)
        self.thread.start()
        self.fake = asyncio.run_coroutine_threadsafe(self._start(), self.loop).result(5)
        return self.fake

    async def _start(self) -> FakeSocket:
        fake = FakeSocket()
        self.server = await serve(fake.handler, "127.0.0.1", 0, process_request=fake.process_request, subprotocols=[protocol.SUBPROTOCOL])
        fake.url = f"ws://127.0.0.1:{next(iter(self.server.sockets)).getsockname()[1]}"
        return fake

    def next(self):
        return asyncio.run_coroutine_threadsafe(self.fake.next(), self.loop).result(5)

    def send(self, raw: str) -> None:
        async def go() -> None:
            await self.fake.wait_connections(1)
            await self.fake.connections[0].send(raw)

        asyncio.run_coroutine_threadsafe(go(), self.loop).result(5)

    def __exit__(self, *exc) -> None:
        async def stop() -> None:
            self.server.close()
            await self.server.wait_closed()

        asyncio.run_coroutine_threadsafe(stop(), self.loop).result(5)
        self.loop.call_soon_threadsafe(self.loop.stop)
        self.thread.join(5)


def test_the_sync_client_round_trip(api):
    received = threading.Event()
    socket = ThreadedSocket()
    fake = socket.__enter__()
    client = WbsktClient(
        "sync-device",
        "483920",
        api_url=api.url,
        socket_url=fake.url,
        storage=MemoryStorage(),
        reconnect_policy=ReconnectPolicy(NoJitter()),
    )
    client.on_message = lambda message: received.set() if message.type == "hello" else None
    try:
        client.start()
        assert socket.next()["type"] == protocol.CAPABILITIES
        client.send("temperature", {"celsius": 21.5})
        assert socket.next()["payload"] == {"celsius": 21.5}
        socket.send('{"type":"hello","payload":null}')
        assert received.wait(5)
    finally:
        client.stop()
        socket.__exit__()
