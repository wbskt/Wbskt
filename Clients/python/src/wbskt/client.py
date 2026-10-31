"""The device client: register once, keep a token, hold the socket open, and come back after a drop.

Follows Docs/Device.Protocol.md; ``Clients/Wbskt.Client.Sdk`` is the reference implementation.
"""

import asyncio
import collections
import inspect
import json
import logging
import platform
import re
import threading
import time
from datetime import datetime, timezone
from typing import Any, Awaitable, Callable, Deque, Dict, Iterable, List, Optional, Union

from websockets.asyncio.client import ClientConnection, connect
from websockets.exceptions import ConnectionClosed, InvalidStatus

from . import _http, protocol
from .models import Command, Message, RegistrationRequired, WbsktError
from .reconnect import ReconnectPolicy
from .storage import FileStorage, Storage
from .version import __version__

log = logging.getLogger("wbskt")

DEFAULT_API_URL = "https://api.dev.wbskt.com"
DEFAULT_SOCKET_URL = "wss://ws.dev.wbskt.com"

TOKEN_RENEW_BEFORE = 60.0
_ISO_TIME = re.compile(r"(?P<base>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?P<fraction>\.\d+)?(?P<zone>Z|[+-]\d{2}:\d{2})?$", re.IGNORECASE)
OFFLINE_BUFFER_SIZE = 1000

Handler = Callable[..., Union[None, Awaitable[None]]]


class _Throttled(Exception):
    def __init__(self, retry_after: float) -> None:
        super().__init__(f"rate limited, retry after {retry_after:g}s")
        self.retry_after = retry_after


class _LoginRefused(Exception):
    """Login said no for now (pending approval, revoked): retry on the schedule."""


class AsyncWbsktClient:
    """A device connection for asyncio code.

    >>> client = AsyncWbsktClient("greenhouse-sensor", policy_pin="483920")
    >>> client.on_message = handle
    >>> await client.start()
    >>> await client.send("temperature", {"celsius": 21.5})

    ``on_message(message)``, ``on_connected()`` and ``on_disconnected()`` may be plain functions or
    coroutines. Messages are handed over one at a time, in the order they arrived.
    """

    def __init__(
        self,
        device_name: str,
        policy_pin: Optional[str] = None,
        *,
        api_url: str = DEFAULT_API_URL,
        socket_url: str = DEFAULT_SOCKET_URL,
        storage: Optional[Storage] = None,
        capabilities: Iterable[Command] = (),
        offline_buffer_size: int = OFFLINE_BUFFER_SIZE,
        reconnect_policy: Optional[ReconnectPolicy] = None,
    ) -> None:
        if not device_name:
            raise ValueError("device_name is required")
        self.device_name = device_name
        self.policy_pin = policy_pin
        self.api_url = api_url.rstrip("/")
        self.socket_url = socket_url.rstrip("/")
        self.storage: Storage = storage if storage is not None else FileStorage.for_device(device_name)
        self.on_message: Optional[Handler] = None
        self.on_connected: Optional[Handler] = None
        self.on_disconnected: Optional[Handler] = None

        self._capabilities: List[Command] = list(capabilities)
        self._buffer: Deque[str] = collections.deque(maxlen=offline_buffer_size)
        self._policy = reconnect_policy or ReconnectPolicy()
        self._token: Optional[str] = None
        self._token_expires = 0.0
        self._ws: Optional[ClientConnection] = None
        self._connected = False
        # Made in start(): on Python 3.9 they bind to the loop that exists when they're created.
        self._stopping: Optional[asyncio.Event] = None
        self._inbox: Optional["asyncio.Queue[Message]"] = None
        self._run_task: Optional["asyncio.Task[None]"] = None
        self._dispatch_task: Optional["asyncio.Task[None]"] = None
        self._error: Optional[BaseException] = None

    @property
    def connected(self) -> bool:
        return self._connected

    async def start(self) -> None:
        """Start connecting in the background. The first attempt is immediate."""
        if self._run_task is not None:
            return
        self._stopping = asyncio.Event()
        self._inbox = asyncio.Queue()
        self._error = None
        self._dispatch_task = asyncio.ensure_future(self._dispatch())
        self._run_task = asyncio.ensure_future(self._run())

    async def stop(self) -> None:
        if self._stopping is not None:
            self._stopping.set()
        ws = self._ws
        if ws is not None:
            await ws.close()
        for task in (self._run_task, self._dispatch_task):
            if task is not None:
                task.cancel()
                try:
                    await task
                except (asyncio.CancelledError, Exception):
                    pass
        self._run_task = self._dispatch_task = None

    async def wait_closed(self) -> None:
        """Wait until the client stops, raising what stopped it if it wasn't ``stop()``."""
        if self._run_task is not None:
            try:
                await asyncio.shield(self._run_task)
            except asyncio.CancelledError:
                if self._stopping is None or not self._stopping.is_set():
                    raise
        if self._error is not None:
            raise self._error

    async def send(self, message_type: str, payload: Any = None) -> None:
        """Send an application message, stamped now. Queued in order while offline."""
        _check_type(message_type)
        await self._send_or_buffer(_encode(message_type, payload, sent_at=_now()))

    async def report_state(self, values: Dict[str, Any]) -> None:
        """Report the state variables that changed. Numbers are kept as readings for charts."""
        await self._send_or_buffer(_encode(protocol.STATE_REPORT, dict(values), sent_at=_now()))

    async def set_capabilities(self, capabilities: Iterable[Command]) -> None:
        """Replace the commands the device accepts, and tell the server if connected."""
        self._capabilities = list(capabilities)
        ws = self._ws
        if self._connected and ws is not None:
            try:
                await ws.send(self._capabilities_frame())
            except ConnectionClosed:
                pass

    async def _send_or_buffer(self, frame: str) -> None:
        if len(frame.encode("utf-8")) > protocol.MAX_MESSAGE_BYTES:
            raise ValueError(f"message is over {protocol.MAX_MESSAGE_BYTES} bytes")
        ws = self._ws
        if self._connected and ws is not None:
            try:
                await ws.send(frame)
                return
            except ConnectionClosed:
                pass
        self._buffer.append(frame)

    async def _run(self) -> None:
        assert self._stopping is not None
        failures = 0
        delay = 0.0
        try:
            while not self._stopping.is_set():
                if delay > 0 and await self._sleep(delay):
                    break
                try:
                    server_restarting = await self._connect_once()
                    failures = 0
                    delay = self._policy.first_delay(server_restarting)
                except _Throttled as throttled:
                    log.warning("wbskt: %s", throttled)
                    delay = self._policy.after_throttle(throttled.retry_after)
                except WbsktError as error:
                    self._error = error
                    log.error("wbskt: %s", error)
                    return
                except asyncio.CancelledError:
                    raise
                except Exception as error:  # network, refused login, bad upgrade: all retried
                    delay = self._policy.retry_delay(failures)
                    failures += 1
                    log.info("wbskt: connect failed (%s); retrying in %.1fs", error, delay)
        finally:
            self._connected = False
            self._ws = None

    async def _sleep(self, seconds: float) -> bool:
        """Wait, waking early on stop(). True if stopping."""
        assert self._stopping is not None
        try:
            await asyncio.wait_for(self._stopping.wait(), timeout=seconds)
            return True
        except asyncio.TimeoutError:
            return False

    async def _connect_once(self) -> bool:
        """One connection from login to close. Returns whether the server said it was restarting."""
        token = await self._access_token()
        try:
            ws = await connect(
                f"{self.socket_url}{protocol.SOCKET_PATH}",
                additional_headers={"Authorization": f"Bearer {token}"},
                subprotocols=[protocol.SUBPROTOCOL],
                max_size=protocol.MAX_MESSAGE_BYTES,
            )
        except InvalidStatus as error:
            if error.response.status_code == 401:
                self._token = None
            raise

        self._ws = ws
        try:
            await ws.send(self._capabilities_frame())
            while self._buffer:
                await ws.send(self._buffer[0])
                self._buffer.popleft()
            # Nothing awaits between the empty check and this, so a send can't overtake the backlog.
            self._connected = True
            log.info("wbskt: connected as %s", self.device_name)
            await _call(self.on_connected)
            async for raw in ws:
                if isinstance(raw, str):
                    await self._receive(ws, raw)
        except ConnectionClosed:
            pass
        finally:
            was_connected = self._connected
            self._connected = False
            self._ws = None
            await ws.close()
            if was_connected:
                await _call(self.on_disconnected)

        return ws.close_code == protocol.CLOSE_SERVER_RESTARTING

    async def _receive(self, ws: ClientConnection, raw: str) -> None:
        try:
            data = json.loads(raw)
            message_type = str(data["type"])
        except (ValueError, KeyError, TypeError):
            log.debug("wbskt: ignored a frame that isn't a message")
            return
        payload = data.get("payload")

        if message_type.lower() == protocol.PING:
            ping = payload if isinstance(payload, dict) else {}
            pong = {"originalTimestamp": ping.get("timestamp"), "id": ping.get("id")}
            await ws.send(_encode(protocol.PONG, pong))
            return
        if protocol.is_reserved(message_type):
            return

        command_id = data.get("commandId")
        expires_at = _parse_time(data.get("expiresAt"))
        if command_id:
            if expires_at is not None and expires_at <= datetime.now(timezone.utc):
                refusal = {"commandId": command_id, "type": message_type, "refused": protocol.REFUSED_EXPIRED}
                await ws.send(_encode(protocol.ACK, refusal))
                return
            await ws.send(_encode(protocol.ACK, {"commandId": command_id}))

        assert self._inbox is not None
        self._inbox.put_nowait(Message(message_type, payload, command_id, expires_at))

    async def _dispatch(self) -> None:
        assert self._inbox is not None
        while True:
            message = await self._inbox.get()
            try:
                await _call(self.on_message, message)
            except Exception:
                log.exception("wbskt: on_message raised")

    async def _access_token(self) -> str:
        if self._token is not None and time.monotonic() < self._token_expires - TOKEN_RENEW_BEFORE:
            return self._token

        client_ref_id, secret = self.storage.load()
        if not client_ref_id or not secret:
            client_ref_id, secret = await self._register()

        status, body, headers = await _post(f"{self.api_url}{protocol.LOGIN_PATH}", {"clientRefId": client_ref_id, "secret": secret})
        if status == 200 and isinstance(body, dict) and body.get("accessToken"):
            self._token = str(body["accessToken"])
            self._token_expires = time.monotonic() + float(body.get("expiresIn") or 3600)
            return self._token
        if status == 429:
            raise _Throttled(_retry_after(headers))
        code = body.get("code") if isinstance(body, dict) else None
        if status == 401 and code == "CLIENT_UNAUTHORIZED":
            # Deleted, or its secret rotated: these credentials will never work again.
            self.storage.clear()
            raise _LoginRefused("credentials no longer valid; registering again")
        raise _LoginRefused(f"login answered {status} {code or ''}".strip())

    async def _register(self):
        if not self.policy_pin:
            raise RegistrationRequired("no stored credentials and no policy PIN to register with")
        status, body, headers = await _post(f"{self.api_url}{protocol.REGISTER_PATH}", {"pin": self.policy_pin, "name": self.device_name})
        if status == 429:
            raise _Throttled(_retry_after(headers))
        if status not in (200, 201) or not isinstance(body, dict) or not body.get("clientRefId"):
            code = body.get("code") if isinstance(body, dict) else None
            raise _LoginRefused(f"registration answered {status} {code or ''}".strip())

        client_ref_id, secret = str(body["clientRefId"]), str(body["secret"])
        # Saved even when pending approval: registering again would make a second device.
        self.storage.save(client_ref_id, secret)
        if body.get("status") == protocol.PENDING:
            log.warning("wbskt: registered %s; waiting for approval in the console", self.device_name)
        return client_ref_id, secret

    def _capabilities_frame(self) -> str:
        return _encode(
            protocol.CAPABILITIES,
            {
                "agent": "wbskt-python",
                "version": __version__,
                "os": f"{platform.system()} {platform.machine()}".strip(),
                "capabilities": [c.to_json() for c in self._capabilities],
            },
        )


class WbsktClient:
    """The same client for code that isn't async: it runs on its own background thread.

    >>> client = WbsktClient("greenhouse-sensor", policy_pin="483920")
    >>> client.on_message = lambda message: print(message.type, message.payload)
    >>> client.start()
    >>> client.send("temperature", {"celsius": 21.5})

    Handlers run on the client's thread, so keep them short or hand work off.
    """

    def __init__(self, device_name: str, policy_pin: Optional[str] = None, **options: Any) -> None:
        self._loop = asyncio.new_event_loop()
        self._thread: Optional[threading.Thread] = None
        self._options = (device_name, policy_pin, options)
        self._client: Optional[AsyncWbsktClient] = None
        self.on_message: Optional[Callable[[Message], None]] = None
        self.on_connected: Optional[Callable[[], None]] = None
        self.on_disconnected: Optional[Callable[[], None]] = None

    @property
    def connected(self) -> bool:
        return self._client is not None and self._client.connected

    def start(self) -> None:
        if self._thread is not None:
            return
        self._thread = threading.Thread(target=self._loop.run_forever, name="wbskt", daemon=True)
        self._thread.start()
        self._client = self._call(self._create())
        self._call(self._client.start())

    def stop(self) -> None:
        if self._thread is None or self._client is None:
            return
        self._call(self._client.stop())
        self._loop.call_soon_threadsafe(self._loop.stop)
        self._thread.join()
        self._thread = None

    def run_forever(self) -> None:
        """Start, then block until Ctrl+C or a fatal error such as a missing PIN."""
        self.start()
        try:
            self._call(self._require_client().wait_closed())
        except KeyboardInterrupt:
            pass
        finally:
            self.stop()

    def send(self, message_type: str, payload: Any = None) -> None:
        self._call(self._require_client().send(message_type, payload))

    def report_state(self, values: Dict[str, Any]) -> None:
        self._call(self._require_client().report_state(values))

    def set_capabilities(self, capabilities: Iterable[Command]) -> None:
        self._call(self._require_client().set_capabilities(capabilities))

    async def _create(self) -> AsyncWbsktClient:
        device_name, policy_pin, options = self._options
        client = AsyncWbsktClient(device_name, policy_pin, **options)
        # Looked up on each call, so handlers can be set before or after start().
        client.on_message = lambda message: _call(self.on_message, message)
        client.on_connected = lambda: _call(self.on_connected)
        client.on_disconnected = lambda: _call(self.on_disconnected)
        return client

    def _require_client(self) -> AsyncWbsktClient:
        if self._client is None:
            raise RuntimeError("call start() first")
        return self._client

    def _call(self, coroutine: Awaitable[Any]) -> Any:
        return asyncio.run_coroutine_threadsafe(coroutine, self._loop).result()  # type: ignore[arg-type]


async def _call(handler: Optional[Handler], *args: Any) -> None:
    if handler is None:
        return
    result = handler(*args)
    if inspect.isawaitable(result):
        await result


async def _post(url: str, body: Dict[str, Any]):
    return await asyncio.get_running_loop().run_in_executor(None, _http.post_json, url, body)


def _check_type(message_type: str) -> None:
    if not message_type or len(message_type) > protocol.MAX_TYPE_LENGTH:
        raise ValueError(f"type must be 1-{protocol.MAX_TYPE_LENGTH} characters")
    if protocol.is_reserved(message_type):
        raise ValueError(f"'{message_type}' is reserved for the platform")


def _encode(message_type: str, payload: Any, sent_at: Optional[str] = None) -> str:
    message: Dict[str, Any] = {"type": message_type, "payload": payload}
    if sent_at is not None:
        message["sentAt"] = sent_at
    return json.dumps(message, separators=(",", ":"), default=str)


def _now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")


def _parse_time(value: Any) -> Optional[datetime]:
    if not isinstance(value, str):
        return None
    # .NET writes seven fractional digits; fromisoformat before 3.11 takes at most six.
    match = _ISO_TIME.match(value.strip())
    if match is None:
        return None
    try:
        parsed = datetime.fromisoformat(match["base"] + (match["fraction"] or "")[:7] + (match["zone"] or "").upper().replace("Z", "+00:00"))
    except ValueError:
        return None
    return parsed if parsed.tzinfo else parsed.replace(tzinfo=timezone.utc)


def _retry_after(headers) -> float:
    for key, value in headers.items():
        if key.lower() == "retry-after":
            try:
                return max(0.0, float(value))
            except ValueError:
                return 0.0
    return 0.0
