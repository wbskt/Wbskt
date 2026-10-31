# Device protocol (v1)

How a device talks to Wbskt. Every SDK (.NET, Python, ESP32/Arduino) implements this document, and a
device written from scratch against it is as supported as one using an SDK. The types named here
live in `Clients/Wbskt.Protocol`; where this page and that code disagree, the code is right and this
page is behind.

A device makes two HTTPS calls, then holds one WebSocket open:

```
POST https://api.<domain>/api/v1/client-registrations/initiate   once, with the policy PIN
POST https://api.<domain>/api/v1/client-auth/login               whenever it needs a token
GET  wss://ws.<domain>/ws  (Upgrade)                             the connection
```

The hosted service is `api.dev.wbskt.com` and `ws.dev.wbskt.com` (`WbsktDefaults` in the .NET SDK).
A self-hosted install uses its own `DOMAIN`.

## 1. Register (once)

```http
POST /api/v1/client-registrations/initiate
Content-Type: application/json

{ "pin": "483920", "name": "greenhouse-sensor" }
```

`pin` is a registration policy's enrollment PIN (console: Policies). `name` is how the device shows
in the console. The answer:

```json
{ "clientRefId": "3fa85f64-…", "secret": "…", "status": 1 }
```

| `status` | Meaning |
|---|---|
| `1` Registered | The policy auto-approves. The device can log in now. |
| `0` Pending | The policy needs approval. Login answers 401 `CLIENT_NOT_REGISTERED` until someone approves it in the console; keep retrying on the reconnect schedule (§5). |
| `2` Revoked, `3` Rejected | Not returned by registration; login reports them. |

**Store `clientRefId` and `secret` durably** (a file, NVS/EEPROM on a microcontroller) and never
register again while you have them: each registration is a new device in the console and counts
against the policy's limit. Discard them only when login answers 401 `CLIENT_UNAUTHORIZED` (the
device was deleted or its secret rotated), then register again.

Registration is rate limited per address; a 429 is retried like a failed login (§5).

## 2. Log in

```http
POST /api/v1/client-auth/login
Content-Type: application/json

{ "clientRefId": "3fa85f64-…", "secret": "…" }
```

```json
{ "accessToken": "eyJ…", "expiresIn": 3600 }
```

`expiresIn` is in seconds (one hour today; read it, don't assume it). **Reuse the token** for every
reconnect until a minute before it expires, and log in again only then or when the socket refuses
it with 401. A device that logs in on every reconnect turns a server restart into a login storm.

| Answer | Do |
|---|---|
| 200 | Connect (§3). |
| 401 `CLIENT_NOT_REGISTERED` | Pending approval, revoked or rejected. Retry on the reconnect schedule. |
| 401 `CLIENT_UNAUTHORIZED` | Credentials no longer valid. Clear them and register again (§1). |
| 429 | Rate limited (300 logins per minute per address by default; devices behind one NAT share it). Wait `Retry-After` if sent, plus jitter (§5). |

Errors are RFC 7807 Problem Details; `code` carries the values above.

## 3. Connect

```http
GET /ws HTTP/1.1
Host: ws.<domain>
Upgrade: websocket
Authorization: Bearer <accessToken>
Sec-WebSocket-Protocol: wbskt.v1
```

Clients that cannot set headers on the upgrade (browsers) put the token in `?access_token=` instead.
Offer the `wbskt.v1` subprotocol (`SocketProtocol.V1`); a connection that offers none is treated as
v1. A 401 on the upgrade means the token is no longer accepted: drop it and log in again.

One device has one connection. Connecting again closes the older one (`PolicyViolation`, "superseded").

## 4. Messages

Every message, both ways, is one **text** frame (or several, up to one message) holding a JSON object:

```json
{ "type": "temperature", "payload": { "celsius": 21.5 }, "sentAt": "2026-10-31T12:00:00Z" }
```

| Field | Direction | |
|---|---|---|
| `type` | both | 1–100 characters. Compared ignoring case. |
| `payload` | both | Any JSON value. |
| `sentAt` | device → server | When the device produced it (UTC, ISO 8601). Optional; missing or unreadable means "now". |
| `commandId` | server → device | Present on a command that wants delivery confirmed (§4.3). |
| `expiresAt` | server → device | On a command: don't act on it after this instant (§4.3). |

**Limits.** A message is at most 64 KB (`SocketProtocol.MaxMessageBytes`); a bigger one ends the
connection. Binary frames are ignored. A device may send 10 messages a second sustained with a burst
of 100 (enough to flush an offline buffer); past that the excess is dropped, and after 500 drops in a
row the connection is closed with `PolicyViolation` ("throttled").

**Reserved types.** Anything starting with `sys.` (any case) belongs to the platform, as do
`capabilities` and `state.report`. A device must not send its own `sys.*` type.

### 4.1 Application messages (device → server)

Any other `type` is yours: workflows trigger on it and the console shows it. Set `sentAt`. A message
whose `sentAt` is more than 30 seconds before it arrives is marked **late** (the device was offline
and buffered it), and workflows can tell late readings from live ones.

**Offline buffer.** While disconnected, queue outgoing messages in memory (the .NET SDK keeps 1,000
and drops the oldest when full), and send them in order, with their original `sentAt`, as soon as
the connection is back and `capabilities` has gone out. Never let a new message overtake the backlog.

### 4.2 Platform messages

| Type | Direction | Payload | Do |
|---|---|---|---|
| `sys.ping` | server → device | `{ "timestamp": "…", "id": "…" }` | Answer at once with `sys.pong`. |
| `sys.pong` | device → server | `{ "originalTimestamp": <ping's timestamp>, "id": <ping's id> }` | The server times the round trip on its own clock. |
| `sys.ack` | device → server | `{ "commandId": "…" }`, or with `"type"` and `"refused": "expired"` | See §4.3. |
| `capabilities` | device → server | below | Send first on every connection. |
| `state.report` | device → server | `{ "<name>": <value>, … }` | The state variables that changed. Numbers are kept as readings for charts. |

`capabilities` describes the device and the commands it takes. Property names are read ignoring case:

```json
{
  "agent": "wbskt-python", "version": "1.0.0", "os": "Linux armv7l",
  "capabilities": [
    { "command": "relay.set", "description": "Switch the relay",
      "parameters": [ { "name": "on", "label": "On", "dataType": "boolean", "description": "", "isRequired": true } ] }
  ]
}
```

`dataType` is `string`, `number`, `boolean` or `object`. Send it again whenever the set changes.

### 4.3 Commands (server → device)

A command is an ordinary message with a `type` that isn't reserved. If it carries `commandId`:

1. If `expiresAt` is present and in the past, **don't act on it**. Answer
   `sys.ack` `{ "commandId": "…", "type": "<the command's type>", "refused": "expired" }`.
2. Otherwise answer `sys.ack` `{ "commandId": "…" }` **as soon as it arrives**, before acting on
   it. This is delivery confirmation, not success; report the outcome as an application message if
   it matters.

Commands sent while the device is offline are not queued today; the sender is told it is offline.

## 5. Reconnecting

Every device behind a socket host loses its connection at the same moment when that host restarts,
so reconnects must be spread out. Delays are always drawn at random between zero and a ceiling
("full jitter"):

| Situation | Wait before the next attempt |
|---|---|
| Connection just dropped | random 0–10 s |
| Server closed with `EndpointUnavailable` (1001, restarting) | random 0–30 s |
| *n*-th failed attempt in a row (n = 0, 1, …) | random 0–min(60 s, 2 s × 2ⁿ) |
| Login or registration answered 429 | `Retry-After` + random 0–2 s |

The first connection at startup needs no delay. Reset the failure count once connected. Reuse the
token (§2), so most reconnects are a socket handshake and not a login.

Close codes the server uses:

| Code | Reason | Do |
|---|---|---|
| 1000 NormalClosure | | Reconnect on the schedule. |
| 1001 EndpointUnavailable | Server restarting | Reconnect, 0–30 s spread. |
| 1008 PolicyViolation | Superseded, revoked, throttled | Reconnect on the schedule; a revoked device's login will then fail with 401. |
| 1009 MessageTooBig | Over 64 KB | Fix the sender; reconnect on the schedule. |

## 6. Conformance checklist

An SDK conforms when it:

- [ ] stores credentials durably and registers only when it has none, or after `CLIENT_UNAUTHORIZED`;
- [ ] reuses the access token until a minute before expiry, and renews it after a 401 on the upgrade;
- [ ] offers `wbskt.v1` and sends the token as a bearer header (or `access_token`);
- [ ] sends `capabilities` first on every connection;
- [ ] answers `sys.ping` with `sys.pong` echoing `id` and the timestamp;
- [ ] acks commands with `commandId` on arrival and refuses expired ones with `refused: "expired"`;
- [ ] stamps `sentAt` on application messages and buffers them in order while offline;
- [ ] never sends a reserved type of its own, or a message over 64 KB;
- [ ] reconnects on the jittered schedule in §5, honouring `Retry-After`.

The .NET SDK (`Clients/Wbskt.Client.Sdk`) is the reference implementation.
