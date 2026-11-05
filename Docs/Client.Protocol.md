# Client protocol (v1): message model

How a client talks to Wbskt, whatever carries the messages. Every SDK (.NET, Python, ESP32/Arduino)
implements this document, and a client written from scratch against it is as supported as one using
an SDK. The types named here live in `Clients/Wbskt.Protocol`; where this page and that code
disagree, the code is right and this page is behind.

This page is the part every transport shares: registration and login, the messages and their
names, commands and acks, limits, and when to reconnect. How the messages travel is a **binding**,
one page per transport:

| Binding | Page | Endpoint |
|---|---|---|
| WebSocket | [Client.Protocol.WebSocket.md](Client.Protocol.WebSocket.md) | `wss://ws.<domain>/ws` |

A client makes two HTTPS calls, then holds one connection open through a binding:

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

`pin` is a registration policy's enrollment PIN (console: Policies). `name` is how the client shows
in the console. The answer:

```json
{ "clientRefId": "3fa85f64-…", "secret": "…", "status": 1 }
```

| `status` | Meaning |
|---|---|
| `1` Registered | The policy auto-approves. The client can log in now. |
| `0` Pending | The policy needs approval. Login answers 401 `CLIENT_NOT_REGISTERED` until someone approves it in the console; keep retrying on the reconnect schedule (§5). |
| `2` Revoked, `3` Rejected | Not returned by registration; login reports them. |

**Store `clientRefId` and `secret` durably** (a file, NVS/EEPROM on a microcontroller) and never
register again while you have them: each registration is a new client in the console and counts
against the policy's limit. Discard them only when login answers 401 `CLIENT_UNAUTHORIZED` (the
client was deleted or its secret rotated), then register again.

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
it (a 401 on the WebSocket upgrade). A client that logs in on every reconnect turns a server restart
into a login storm.

| Answer | Do |
|---|---|
| 200 | Connect (§3). |
| 401 `CLIENT_NOT_REGISTERED` | Pending approval, revoked or rejected. Retry on the reconnect schedule. |
| 401 `CLIENT_UNAUTHORIZED` | Credentials no longer valid. Clear them and register again (§1). |
| 429 | Rate limited (300 logins per minute per address by default; clients behind one NAT share it). Wait `Retry-After` if sent, plus jitter (§5). |

Errors are RFC 7807 Problem Details; `code` carries the values above.

## 3. Connect

The binding opens the connection and presents the access token; its page says how. Whatever the
binding:

- A refused token means it is no longer accepted: drop it and log in again (§2).
- One client has one connection. Connecting again ends the older one (superseded, §5).
- Every connection starts with the client sending `capabilities` (§4.2), before anything else.

## 4. Messages

Every message, both ways, is a JSON object:

```json
{ "type": "temperature", "payload": { "celsius": 21.5 }, "sentAt": "2026-10-31T12:00:00Z" }
```

| Field | Direction | |
|---|---|---|
| `type` | both | 1–100 characters. Compared ignoring case. |
| `payload` | both | Any JSON value. |
| `sentAt` | client → server | When the client produced it (UTC, ISO 8601). Optional; missing or unreadable means "now". |
| `commandId` | server → client | Present on a command that wants delivery confirmed (§4.3). |
| `expiresAt` | server → client | On a command: don't act on it after this instant (§4.3). |

**Limits.** A message is at most 64 KB (`ClientProtocol.MaxMessageBytes`); a bigger one ends the
connection (too big, §5). The gateway lets a client send 10 messages a second sustained with a
burst of 100; past that the excess is dropped, and after 500 drops in a row the connection is ended
(throttled, §5).

**Pacing.** A client sends at most 8 messages a second after a burst of 50
(`ClientProtocol.SendPerSecond`, `ClientProtocol.SendBurst`), and that includes the offline backlog it
flushes on reconnect. A message the gateway drops has still left the client, so an SDK that flushes a
full buffer at once loses most of it. The SDKs pace with a token bucket and keep answering pings
while a backlog drains.

**Reserved types.** Anything starting with `sys.` (any case) belongs to the platform, as do
`capabilities` and `state.report`. A client must not send its own `sys.*` type.

### 4.1 Application messages (client → server)

Any other `type` is yours: workflows trigger on it and the console shows it. Set `sentAt`. A message
whose `sentAt` is more than 30 seconds before it arrives is marked **late** (the client was offline
and buffered it), and workflows can tell late readings from live ones.

**Offline buffer.** While disconnected, queue outgoing messages in memory (the .NET SDK keeps 1,000
and drops the oldest when full), and send them in order, with their original `sentAt`, as soon as
the connection is back and `capabilities` has gone out. Never let a new message overtake the backlog.

### 4.2 Platform messages

| Type | Direction | Payload | Do |
|---|---|---|---|
| `sys.ping` | server → client | `{ "timestamp": "…", "id": "…" }` | Answer at once with `sys.pong`. |
| `sys.pong` | client → server | `{ "originalTimestamp": <ping's timestamp>, "id": <ping's id> }` | The server times the round trip on its own clock, so this works over any transport, whatever keepalive the transport has of its own. |
| `sys.ack` | client → server | `{ "commandId": "…" }`, or with `"type"` and `"refused": "expired"` | See §4.3. |
| `capabilities` | client → server | below | Send first on every connection. |
| `state.report` | client → server | `{ "<name>": <value>, … }` | The state variables that changed. Numbers are kept as readings for charts. |
| `sys.ota.update` | server → client | `{ "url": "https://…", "sha256": "<64 hex>", "version"?: "…", "size"?: <bytes> }` | Optional (§4.4). A command: acked, and refused when expired, as in §4.3. |
| `sys.ota.status` | client → server | `{ "commandId": "…", "state": "…", "progress"?: 0–100, "version"?: "…", "error"?: "…" }` | Optional (§4.4). How an update is going. |

`capabilities` describes the client and the commands it takes. Property names are read ignoring case:

```json
{
  "agent": "wbskt-python", "version": "1.0.0", "os": "Linux armv7l",
  "capabilities": [
    { "command": "relay.set", "description": "Switch the relay",
      "parameters": [ { "name": "on", "label": "On", "dataType": "boolean", "description": "", "isRequired": true } ] }
  ],
  "system": [ "ota" ],
  "firmwareVersion": "2.4.1"
}
```

`dataType` is `string`, `number`, `boolean` or `object`. `system` lists the optional platform
features the client takes; today the only one is `ota` (§4.4). Leave it out when there are none.
`firmwareVersion` is the version of the code the client runs: its firmware on a microcontroller,
the application otherwise. `version` is the SDK's own. Leave it out when unknown; the server keeps at
most 50 characters. Send `capabilities` again whenever either set changes.

### 4.3 Commands (server → client)

A command is an ordinary message with a `type` that isn't reserved. If it carries `commandId`:

1. If `expiresAt` is present and in the past, **don't act on it**. Answer
   `sys.ack` `{ "commandId": "…", "type": "<the command's type>", "refused": "expired" }`.
2. Otherwise answer `sys.ack` `{ "commandId": "…" }` **as soon as it arrives**, before acting on
   it. This is delivery confirmation, not success; report the outcome as an application message if
   it matters.

A command sent while the client is offline waits on the server (at most 24 hours, or until its
`expiresAt`) and arrives, oldest first, once the client connects again. So a client can get a command
long after it was sent: that is what `expiresAt` is for.

`sys.ota.update` is the one reserved type that is a command: it carries `commandId` and `expiresAt`
and is acked and refused exactly as above. No other reserved type is ever sent as a command.

### 4.4 Firmware updates (optional)

A client that can replace its own firmware lists `"ota"` under `system` in `capabilities`. The server
sends `sys.ota.update` only to such a client, and only with an `https` URL and a SHA-256. The
message carries a link, not the image: an image is 1–2 MB and a message at most 64 KB.

1. Ack it as any command (§4.3). If it has expired, refuse it there and do nothing else.
2. Answer `sys.ota.status` with the command's `commandId` and a `state`:
   - `rejected`, with `error`, when it won't start: OTA is off, the URL isn't `https`, the digest
     isn't 64 hex digits, `size` is bigger than the space for the image, or another update is
     under way. Nothing changes on the client.
   - `accepted` when it will. Then `downloading`, with `progress` (percent) as it goes if it can.
     `progress` is optional and best effort: a client that closes its connection to free memory for
     the download (as the ESP32 library does) cannot send it, and sends `downloading` once, before it closes.
   - `failed`, with `error`, if it goes wrong after that: the download breaks, the digest doesn't
     match, or the new firmware never connects and the client goes back to the previous one. The
     client keeps running its previous firmware.
   - `installed`, with `version`, sent by the **new** firmware once it has connected. Keep the
     `commandId` across the restart for this.
3. A client that closes its connection to download closes with the binding's "updating" code (WebSocket:
   4000). The server then shows it as updating rather than offline, and holds presence triggers back,
   for up to 5 minutes or until it connects again. Give the new image the same 5 minutes to connect
   before rolling it back.
4. Check the SHA-256 before the new image can boot, and keep the credentials across the update so
   the client signs in as the same client.
5. Send the running version as `firmwareVersion` in `capabilities` on every connection, so the
   server sees which firmware a client runs whatever happened.

Every firmware shipped must keep an update path. A client whose new firmware can't take
`sys.ota.update` can only be updated by hand after that.

## 5. Reconnecting

Every client behind a gateway loses its connection at the same moment when that gateway restarts,
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
| Superseded | The same client connected again. | Reconnect on the schedule, and log it: a client superseded again and again is sharing its credentials with another, and the server raises a warning for it. |
| Revoked | The client lost access: revoked, deleted or its secret rotated. | Drop the access token, then reconnect on the schedule; login will then fail with 401. |
| Throttled | Over the message rate (§4). | Wait a random 30–60 s before reconnecting, and send less. |
| Too big | A message over 64 KB (§4). | Fix the sender; reconnect on the schedule. |
| Server error | The server failed while serving the connection. | Reconnect on the schedule. |

## 6. Conformance checklist

An SDK conforms to the message model when it:

- [ ] stores credentials durably and registers only when it has none, or after `CLIENT_UNAUTHORIZED`;
- [ ] reuses the access token until a minute before expiry, and logs in again only then or when the binding refuses it;
- [ ] sends `capabilities` first on every connection;
- [ ] answers `sys.ping` with `sys.pong` echoing `id` and the timestamp;
- [ ] acks commands with `commandId` on arrival and refuses expired ones with `refused: "expired"`;
- [ ] stamps `sentAt` on application messages and buffers them in order while offline;
- [ ] sends no more than 8 messages a second after a burst of 50, a flushed backlog included;
- [ ] never sends a reserved type of its own, or a message over 64 KB;
- [ ] reconnects on the jittered schedule above, honouring `Retry-After`.

And, if it takes firmware updates (§4.4), when it:

- [ ] lists `ota` under `system` only while updates are on, and answers `sys.ota.update` with `rejected` otherwise;
- [ ] sends the running version as `firmwareVersion` in `capabilities`;
- [ ] refuses an expired `sys.ota.update` in its ack and starts nothing;
- [ ] reports `accepted`, `downloading` and, from the new firmware, `installed` with the version, under the update's `commandId`;
- [ ] closes with the binding's "updating" code when it drops its connection to download.

It also conforms to each binding it speaks; see that binding's checklist
([WebSocket](Client.Protocol.WebSocket.md#5-conformance-checklist)).

The .NET SDK (`Clients/Wbskt.Client.Sdk`) is the reference implementation.
