"""A fake Wbskt: the two HTTPS endpoints on http.server and the socket on websockets."""

import asyncio
import json
import threading
import uuid
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any, Dict, List, Optional

import pytest
from websockets.asyncio.server import ServerConnection, serve

from wbskt import protocol


class FakeApi:
    def __init__(self) -> None:
        self.registrations: List[Dict[str, Any]] = []
        self.logins: List[Dict[str, Any]] = []
        self.status = protocol.REGISTERED
        # Answers to give the next logins, before the normal 200: (status, body, headers).
        self.login_answers: List[tuple] = []
        self.issued: List[str] = []
        self._lock = threading.Lock()

    def handle(self, path: str, body: Dict[str, Any]):
        with self._lock:
            if path == protocol.REGISTER_PATH:
                self.registrations.append(body)
                return 200, {"clientRefId": str(uuid.uuid4()), "secret": f"secret-{len(self.registrations)}", "status": self.status}, {}
            if path == protocol.LOGIN_PATH:
                self.logins.append(body)
                if self.login_answers:
                    return self.login_answers.pop(0)
                token = f"token-{len(self.logins)}"
                self.issued.append(token)
                return 200, {"accessToken": token, "expiresIn": 3600}, {}
            return 404, {}, {}


class FakeSocket:
    def __init__(self) -> None:
        self.connections: List[ServerConnection] = []
        self.received: "asyncio.Queue[Dict[str, Any]]" = asyncio.Queue()
        self.connected = asyncio.Event()
        self.rejected_tokens: set = set()
        self.tokens: List[str] = []

    def process_request(self, connection, request):
        token = request.headers.get("Authorization", "").removeprefix("Bearer ")
        self.tokens.append(token)
        if token in self.rejected_tokens:
            return connection.respond(HTTPStatus.UNAUTHORIZED, "token rejected\n")
        return None

    async def handler(self, ws: ServerConnection) -> None:
        self.connections.append(ws)
        self.connected.set()
        async for raw in ws:
            await self.received.put(json.loads(raw))

    async def next(self, timeout: float = 5.0) -> Dict[str, Any]:
        return await asyncio.wait_for(self.received.get(), timeout)

    async def wait_connections(self, count: int, timeout: float = 5.0) -> ServerConnection:
        async def until() -> None:
            while len(self.connections) < count:
                await asyncio.sleep(0.01)

        await asyncio.wait_for(until(), timeout)
        return self.connections[count - 1]


@pytest.fixture
def api():
    fake = FakeApi()

    class Handler(BaseHTTPRequestHandler):
        def do_POST(self) -> None:
            length = int(self.headers.get("Content-Length") or 0)
            status, body, headers = fake.handle(self.path, json.loads(self.rfile.read(length) or b"{}"))
            raw = json.dumps(body).encode()
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            for key, value in headers.items():
                self.send_header(key, value)
            self.send_header("Content-Length", str(len(raw)))
            self.end_headers()
            self.wfile.write(raw)

        def log_message(self, *args: Any) -> None:
            pass

    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    fake.url = f"http://127.0.0.1:{server.server_address[1]}"
    yield fake
    server.shutdown()
    server.server_close()


@pytest.fixture
async def socket_server():
    fake = FakeSocket()
    async with serve(fake.handler, "127.0.0.1", 0, process_request=fake.process_request, subprotocols=[protocol.SUBPROTOCOL]) as server:
        port = next(iter(server.sockets)).getsockname()[1]
        fake.url = f"ws://127.0.0.1:{port}"
        yield fake


class NoJitter:
    def random(self) -> float:
        return 0.0


@pytest.fixture
def no_jitter():
    from wbskt import ReconnectPolicy

    return ReconnectPolicy(NoJitter())  # type: ignore[arg-type]


def make_client(api, socket_server, no_jitter, storage=None, **options: Any):
    from wbskt import AsyncWbsktClient, MemoryStorage

    return AsyncWbsktClient(
        "test-device",
        "483920",
        api_url=api.url,
        socket_url=socket_server.url,
        storage=storage if storage is not None else MemoryStorage(),
        reconnect_policy=no_jitter,
        **options,
    )


async def eventually(predicate, timeout: float = 5.0) -> None:
    async def until() -> None:
        while not predicate():
            await asyncio.sleep(0.01)

    await asyncio.wait_for(until(), timeout)
