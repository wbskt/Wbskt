# Device protocol (v1): message model

How a device talks to Wbskt, whatever carries the messages. Every SDK (.NET, Python, ESP32/Arduino)
implements this document, and a device written from scratch against it is as supported as one using
an SDK. The types named here live in `Clients/Wbskt.Protocol`; where this page and that code
disagree, the code is right and this page is behind.

This page is the part every transport shares: registration and login, the messages and their
names, commands and acks, limits, and when to reconnect. How the messages travel is a **binding**,
one page per transport:

| Binding | Page | Endpoint |
|---|---|---|
| WebSocket | [Device.Protocol.WebSocket.md](Device.Protocol.WebSocket.md) | `wss://ws.<domain>/ws` |

A device makes two HTTPS calls, then holds one connection open through a binding:

```
POST https://api.<domain>/api/v1/client-registrations/initiate   once, with the policy PIN
POST https://api.<domain>/api/v1/client-auth/login               whenever it needs a token
<the binding's connection>                                       e.g. GET wss://ws.<domain>/ws
```

The hosted service is `api.dev.wbskt.com` (`WbsktDefaults` in the .NET SDK). A self-hosted install
uses its own `DOMAIN`.

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
reconnect until a minute before it expires, and log in again only then or when the binding refuses
it (a 401 on the WebSocket upgrade). A device that logs in on every reconnect turns a server restart
into a login storm.

| Answer | Do |
|---|---|
| 200 | Connect (§3). |
| 401 `CLIENT_NOT_REGISTERED` | Pending approval, revoked or rejected. Retry on the reconnect schedule. |
| 401 `CLIENT_UNAUTHORIZED` | Credentials no longer valid. Clear them and register again (§1). |
| 429 | Rate limited (300 logins per minute per address by default; devices behind one NAT share it). Wait `Retry-After` if sent, plus jitter (§5). |

Errors are RFC 7807 Problem Details; `code` carries the values above.

## 3. Connect

The binding opens the connection and presents the access token; its page says how. Whatever the
binding:

- A refused token means it is no longer accepted: drop it and log in again (§2).
- One device has one connection. Connecting again ends the older one (superseded, §5).
- Every connection starts with the device sending `capabilities` (§4.2), before anything else.

## 4. Messages

Every message, both ways, is a JSON object:

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
connection (too big, §5). A device may send 10 messages a second sustained with a burst of 100
(enough to flush an offline buffer); past that the excess is dropped, and after 500 drops in a row
the connection is ended (throttled, §5).

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
| `sys.pong` | device → server | `{ "originalTimestamp": <ping's timestamp>, "id": <ping's id> }` | The server times the round trip on its own clock, so this works over any transport, whatever keepalive the transport has of its own. |
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

Every device behind a gateway loses its connection at the same moment when that gateway restarts,
so reconnects must be spread out. Delays are always drawn at random between zero and a ceiling
("full jitter"):

| Situation | Wait before the next attempt |
|---|---|
| Connection just dropped | random 0–10 s |
| Server ended it because it is restarting | random 0–30 s |
| *n*-th failed attempt in a row (n = 0, 1, …) | random 0–min(60 s, 2 s × 2ⁿ) |
| Login or registration answered 429 | `Retry-After` + random 0–2 s |

The first connection at startup needs no delay. Reset the failure count once connected. Reuse the
token (§2), so most reconnects are a handshake and not a login.

Why the server ends a connection. Each binding says how it signals these (the WebSocket binding
uses close codes):

| Reason | When | Do |
|---|---|---|
| Normal | The server closed it with nothing wrong. | Reconnect on the schedule. |
| Restarting | The gateway is shutting down. | Reconnect, 0–30 s spread. |
| Superseded | The same device connected again. | Reconnect on the schedule. |
| Revoked | The device lost access: revoked, deleted or its secret rotated. | Reconnect on the schedule; login will then fail with 401. |
| Throttled | Over the message rate (§4). | Reconnect on the schedule, and send less. |
| Too big | A message over 64 KB (§4). | Fix the sender; reconnect on the schedule. |

## 6. Conformance checklist

An SDK conforms to the message model when it:

- [ ] stores credentials durably and registers only when it has none, or after `CLIENT_UNAUTHORIZED`;
- [ ] reuses the access token until a minute before expiry, and logs in again only then or when the binding refuses it;
- [ ] sends `capabilities` first on every connection;
- [ ] answers `sys.ping` with `sys.pong` echoing `id` and the timestamp;
- [ ] acks commands with `commandId` on arrival and refuses expired ones with `refused: "expired"`;
- [ ] stamps `sentAt` on application messages and buffers them in order while offline;
- [ ] never sends a reserved type of its own, or a message over 64 KB;
- [ ] reconnects on the jittered schedule above, honouring `Retry-After`.

It also conforms to each binding it speaks; see that binding's checklist
([WebSocket](Device.Protocol.WebSocket.md#5-conformance-checklist)).

The .NET SDK (`Clients/Wbskt.Client.Sdk`) is the reference implementation.
