# Device protocol (v1): WebSocket binding

How the [message model](Device.Protocol.md) travels over a WebSocket. Registration, login, the
messages themselves, commands and the reconnect schedule are all on that page; this one covers only
what is particular to the WebSocket: the upgrade, the frames, keepalive and close codes. The
`Wbskt.Gateway.WebSocket.Host` serves it.

The hosted endpoint is `wss://ws.dev.wbskt.com/ws` (`WbsktDefaults` in the .NET SDK). A self-hosted
install uses `wss://ws.<DOMAIN>/ws`.

## 1. Connect

```http
GET /ws HTTP/1.1
Host: ws.<domain>
Upgrade: websocket
Authorization: Bearer <accessToken>
Sec-WebSocket-Protocol: wbskt.v1
```

`accessToken` is the one login returned (model §2). Clients that cannot set headers on the upgrade
(browsers) put it in `?access_token=` instead.

Offer the `wbskt.v1` subprotocol (`SocketProtocol.V1`); a connection that offers none is treated as
v1, and one that offers only other versions is refused.

A 401 on the upgrade means the token is no longer accepted: drop it and log in again (model §2). Any
other refusal, such as a 503 while the gateway restarts, keeps the token and retries on the
reconnect schedule (model §5).

## 2. Frames

Every message (model §4), both ways, is one **text** message holding one JSON object. It may arrive
as several frames; the 64 KB limit applies to the whole message. Binary frames are ignored.

## 3. Keepalive

The gateway pings each connection to time its round trip. These are binding messages, reserved like
every `sys.*` type:

| Type | Direction | Payload | Do |
|---|---|---|---|
| `sys.ping` | server → device | `{ "timestamp": "…", "id": "…" }` | Answer at once with `sys.pong`. |
| `sys.pong` | device → server | `{ "originalTimestamp": <ping's timestamp>, "id": <ping's id> }` | The server times the round trip on its own clock. |

## 4. Close codes

How the gateway signals each reason the model gives for ending a connection (model §5):

| Code | Reason | Do |
|---|---|---|
| 1000 NormalClosure | Normal | Reconnect on the schedule. |
| 1001 EndpointUnavailable | Restarting | Reconnect, 0–30 s spread. |
| 1008 PolicyViolation | Superseded, revoked, throttled | Reconnect on the schedule; a revoked device's login will then fail with 401. |
| 1009 MessageTooBig | Too big | Fix the sender; reconnect on the schedule. |

The close description is text for logs. Branch on the code, not on the description.

## 5. Conformance checklist

An SDK conforms to the WebSocket binding when it:

- [ ] offers `wbskt.v1` and sends the token as a bearer header (or `access_token`);
- [ ] renews the token after a 401 on the upgrade, and keeps it after any other refusal;
- [ ] answers `sys.ping` with `sys.pong` echoing `id` and the timestamp.

Together with the [message model's checklist](Device.Protocol.md#6-conformance-checklist), this is
everything the conformance suite (`Tests/Conformance`) checks.
