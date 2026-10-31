"""A fake Wbskt for the conformance suite: the two HTTPS endpoints and the device socket.

It records what the SDK did (registrations, logins, the token on each upgrade, every frame) so the
scenarios can check it against Docs/Device.Protocol.md, and it can misbehave on cue: refuse a token,
throttle a login, reject credentials, close the socket.
"""

import asyncio
import json
import threading
import time
import uuid
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any, Dict, List, Optional, Tuple

from websockets.asyncio.server import ServerConnection, serve

REGISTER_PATH = "/api/v1/client-registrations/initiate"
LOGIN_PATH = "/api/v1/client-auth/login"
SOCKET_PATH = "/ws"
SUBPROTOCOL = "wbskt.v1"

Answer = Tuple[int, Dict[str, Any], Dict[str, str]]


class FakeApi:
    def __init__(self) -> None:
        self.registrations: List[Dict[str, Any]] = []
        self.logins: List[Tuple[float, Dict[str, Any]]] = []
        self.registered_status = 1
        # Answers for the next logins, before the normal 200.
        self.login_answers: List[Answer] = []
        self.known: Dict[str, str] = {}
        self._lock = threading.Lock()
        self._server: Optional[ThreadingHTTPServer] = None
        self.url = ""

    def handle(self, path: str, body: Dict[str, Any]) -> Answer:
        with self._lock:
            if path == REGISTER_PATH:
                self.registrations.append(body)
                ref, secret = str(uuid.uuid4()), f"secret-{len(self.registrations)}"
                self.known[ref] = secret
                return 200, {"clientRefId": ref, "secret": secret, "status": self.registered_status}, {}
            if path == LOGIN_PATH:
                self.logins.append((time.monotonic(), body))
                if self.login_answers:
                    return self.login_answers.pop(0)
                secret = body.get("secret")
                if not secret or self.known.get(str(body.get("clientRefId"))) != secret:
                    return 401, {"code": "CLIENT_UNAUTHORIZED"}, {}
                return 200, {"accessToken": f"token-{len(self.logins)}", "expiresIn": 3600}, {}
            return 404, {}, {}

    def forget_all_devices(self) -> None:
        with self._lock:
            self.known.clear()

    def start(self) -> None:
        api = self

        class Handler(BaseHTTPRequestHandler):
            def do_POST(self) -> None:
                try:
                    body = json.loads(self._read_body() or b"{}")
                except ValueError:
                    body = {}
                status, payload, headers = api.handle(self.path, body)
                raw = json.dumps(payload).encode()
                self.send_response(status)
                self.send_header("Content-Type", "application/problem+json" if status >= 400 else "application/json")
                for key, value in headers.items():
                    self.send_header(key, value)
                self.send_header("Content-Length", str(len(raw)))
                self.end_headers()
                self.wfile.write(raw)

            def _read_body(self) -> bytes:
                # HttpClient sends JSON chunked, with no Content-Length.
                if "chunked" not in self.headers.get("Transfer-Encoding", "").lower():
                    return self.rfile.read(int(self.headers.get("Content-Length") or 0))
                body = b""
                while True:
                    size = int(self.rfile.readline().split(b";")[0].strip(), 16)
                    if size == 0:
                        while self.rfile.readline() not in (b"\r\n", b"\n", b""):
                            pass
                        return body
                    body += self.rfile.read(size)
                    self.rfile.readline()

            def log_message(self, *args: Any) -> None:
                pass

        self._server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        threading.Thread(target=self._server.serve_forever, daemon=True).start()
        self.url = f"http://127.0.0.1:{self._server.server_address[1]}"

    def stop(self) -> None:
        if self._server is not None:
            self._server.shutdown()
            self._server.server_close()


class FakeSocket:
    def __init__(self) -> None:
        self.connections: List[ServerConnection] = []
        self.frames: "asyncio.Queue[Tuple[int, Dict[str, Any], float]]" = asyncio.Queue()
        self.upgrade_tokens: List[str] = []
        self.subprotocols: List[Optional[str]] = []
        self.rejected_tokens: set = set()
        # While set, every upgrade is answered 503, as by a host that is down.
        self.down = False
        self.url = ""
        self._server: Any = None

    def _process_request(self, connection: ServerConnection, request: Any) -> Any:
        if request.path.split("?")[0] != SOCKET_PATH:
            return connection.respond(HTTPStatus.NOT_FOUND, "not found\n")
        token = request.headers.get("Authorization", "")
        token = token[7:] if token.startswith("Bearer ") else token
        self.upgrade_tokens.append(token)
        if self.down:
            return connection.respond(HTTPStatus.SERVICE_UNAVAILABLE, "down\n")
        if token in self.rejected_tokens:
            return connection.respond(HTTPStatus.UNAUTHORIZED, "token rejected\n")
        return None

    async def _handler(self, ws: ServerConnection) -> None:
        index = len(self.connections)
        self.connections.append(ws)
        self.subprotocols.append(ws.subprotocol)
        async for raw in ws:
            if isinstance(raw, str):
                await self.frames.put((index, json.loads(raw), time.time()))

    async def start(self) -> None:
        self._server = await serve(
            self._handler, "127.0.0.1", 0, process_request=self._process_request, subprotocols=[SUBPROTOCOL]
        )
        port = next(iter(self._server.sockets)).getsockname()[1]
        self.url = f"ws://127.0.0.1:{port}"

    async def stop(self) -> None:
        if self._server is not None:
            self._server.close()
            await self._server.wait_closed()

    async def next_frame(self, timeout: float = 10.0) -> Tuple[int, Dict[str, Any], float]:
        return await asyncio.wait_for(self.frames.get(), timeout)

    async def next_of(self, message_type: str, timeout: float = 10.0) -> Dict[str, Any]:
        """The next frame of this type, skipping others."""
        deadline = asyncio.get_running_loop().time() + timeout
        while True:
            remaining = deadline - asyncio.get_running_loop().time()
            _, frame, _ = await self.next_frame(max(remaining, 0.01))
            if frame.get("type") == message_type:
                return frame

    async def wait_connections(self, count: int, timeout: float = 20.0) -> ServerConnection:
        async def until() -> None:
            while len(self.connections) < count:
                await asyncio.sleep(0.02)

        await asyncio.wait_for(until(), timeout)
        return self.connections[count - 1]

    async def send(self, connection: int, message: Dict[str, Any]) -> None:
        await self.connections[connection].send(json.dumps(message))
