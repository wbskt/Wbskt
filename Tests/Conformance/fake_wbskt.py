"""A fake Wbskt for the conformance suite: the two HTTPS endpoints and the client socket.

It records what the SDK did (registrations, logins, the token on each upgrade, every frame) so the
scenarios can check it against Docs/Client.Protocol.md, and it can misbehave on cue: refuse a token,
throttle a login, reject credentials, close the socket. Like the real gateway, it refuses a revoked
client's token with 403 CLIENT_REVOKED, refuses an upgrade offering only unknown subprotocols with 400
SOCKET_PROTOCOL_UNSUPPORTED, closes a client's older connection when it connects again (4001), and
enforces the per-client message budget (4003).
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
from websockets.exceptions import ConnectionClosed

REGISTER_PATH = "/api/v1/client-registrations/initiate"
LOGIN_PATH = "/api/v1/client-auth/login"
SOCKET_PATH = "/ws"
SUBPROTOCOL = "wbskt.v1"

# The gateway's default message budget (MessageBudgetOptions); the fake enforces it the same way.
BUDGET_PER_SECOND = 10.0
BUDGET_BURST = 100
BUDGET_CLOSE_AFTER_DROPPED = 500
CLOSE_SUPERSEDED = 4001
CLOSE_THROTTLED = 4003
# The one close code a client sends of its own: closing to install a firmware update.
CLOSE_UPDATING = 4000

Answer = Tuple[int, Dict[str, Any], Dict[str, str]]


class FakeApi:
    def __init__(self) -> None:
        self.registrations: List[Dict[str, Any]] = []
        self.logins: List[Tuple[float, Dict[str, Any]]] = []
        self.registered_status = 1
        # Answers for the next logins, before the normal 200.
        self.login_answers: List[Answer] = []
        self.known: Dict[str, str] = {}
        # Which client each issued token belongs to; the socket reads it to supersede connections.
        self.token_clients: Dict[str, str] = {}
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
                token = f"token-{len(self.logins)}"
                self.token_clients[token] = str(body.get("clientRefId"))
                return 200, {"accessToken": token, "expiresIn": 3600}, {}
            return 404, {}, {}

    def forget_all_clients(self) -> None:
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


class _Budget:
    """A token bucket plus a count of drops in a row, like Wbskt.Gateway.Core's MessageBudget."""

    def __init__(self) -> None:
        self._tokens = float(BUDGET_BURST)
        self._last = time.monotonic()
        self.consecutive_dropped = 0

    def try_take(self) -> bool:
        now = time.monotonic()
        self._tokens = min(BUDGET_BURST, self._tokens + (now - self._last) * BUDGET_PER_SECOND)
        self._last = now
        if self._tokens >= 1:
            self._tokens -= 1
            self.consecutive_dropped = 0
            return True
        self.consecutive_dropped += 1
        return False


class FakeSocket:
    def __init__(self) -> None:
        self.connections: List[ServerConnection] = []
        self.frames: "asyncio.Queue[Tuple[int, Dict[str, Any], float]]" = asyncio.Queue()
        self.upgrade_tokens: List[str] = []
        self.subprotocols: List[Optional[str]] = []
        self.rejected_tokens: set = set()
        self.revoked_tokens: set = set()
        # Shared with FakeApi.token_clients by the fixture.
        self.token_clients: Dict[str, str] = {}
        self.refused: List[Tuple[int, str]] = []
        self._live: Dict[str, ServerConnection] = {}
        # Messages dropped over the budget, and the close code the fake sent on each connection.
        self.dropped = 0
        self.close_codes: Dict[int, int] = {}
        # The close code each connection's client sent, when it closed first (1006: it sent none).
        self.client_close_codes: Dict[int, int] = {}
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
        if token in self.revoked_tokens:
            return self._problem(connection, HTTPStatus.FORBIDDEN, "CLIENT_REVOKED")
        offered = [p.strip() for p in request.headers.get("Sec-WebSocket-Protocol", "").split(",") if p.strip()]
        if offered and SUBPROTOCOL not in offered:
            return self._problem(connection, HTTPStatus.BAD_REQUEST, "SOCKET_PROTOCOL_UNSUPPORTED")
        return None

    def _problem(self, connection: ServerConnection, status: HTTPStatus, code: str) -> Any:
        self.refused.append((int(status), code))
        response = connection.respond(status, json.dumps({"status": int(status), "code": code}))
        response.headers["Content-Type"] = "application/problem+json"
        return response

    async def _handler(self, ws: ServerConnection) -> None:
        index = len(self.connections)
        self.connections.append(ws)
        self.subprotocols.append(ws.subprotocol)
        authorization = ws.request.headers.get("Authorization", "") if ws.request is not None else ""
        client = self.token_clients.get(authorization[7:] if authorization.startswith("Bearer ") else authorization)
        if client is not None:
            older = self._live.get(client)
            self._live[client] = ws
            if older is not None:
                self.close_codes[self.connections.index(older)] = CLOSE_SUPERSEDED
                await older.close(CLOSE_SUPERSEDED, "Superseded by a new connection.")
        budget = _Budget()
        try:
            async for raw in ws:
                if not isinstance(raw, str):
                    continue
                if not budget.try_take():
                    self.dropped += 1
                    if budget.consecutive_dropped >= BUDGET_CLOSE_AFTER_DROPPED:
                        self.close_codes[index] = CLOSE_THROTTLED
                        await ws.close(CLOSE_THROTTLED, "Too many messages.")
                        return
                    continue
                await self.frames.put((index, json.loads(raw), time.time()))
        except ConnectionClosed:
            pass
        finally:
            if index not in self.close_codes and ws.close_code is not None:
                self.client_close_codes[index] = ws.close_code

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

    async def wait_upgrades(self, count: int, timeout: float = 20.0) -> None:
        """Until this many upgrades were asked for, whether or not they were accepted."""

        async def until() -> None:
            while len(self.upgrade_tokens) < count:
                await asyncio.sleep(0.02)

        await asyncio.wait_for(until(), timeout)

    async def close(self, connection: int, code: int, reason: str = "") -> None:
        self.close_codes[connection] = code
        await self.connections[connection].close(code, reason)

    async def send(self, connection: int, message: Dict[str, Any]) -> None:
        await self.connections[connection].send(json.dumps(message))
