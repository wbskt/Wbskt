# Device protocol (v1): WebSocket binding

How the [message model](Device.Protocol.md) travels over a WebSocket. Registration, login, the
messages themselves, commands and the reconnect schedule are all on that page; this one covers only
what is particular to the WebSocket: the upgrade and how it is refused, the frames, and close codes. The
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

The gateway refuses before the upgrade, with an RFC 7807 Problem Details body whose `code` names the
reason:

| Answer | Means | Do |
|---|---|---|
| 401, no `code` | The token did not validate: expired, signed by another key, or missing. | Drop the token and log in again (model §2). |
| 401 `CLIENT_TOKEN_INVALID` | The token validated but is not a device token with every claim this gateway needs. | Drop the token and log in again. |
| 403, no `code` | The token validated but does not mark itself as a device's (its `typ` claim). | A client bug: only the token from device login (model §2) is accepted here. |
| 403 `CLIENT_REVOKED` | The device lost access after this token was issued: revoked, deleted, or its secret rotated. | Keep the token and retry on the reconnect schedule (model §5); once it expires, login reports what happened. |
| 400 `SOCKET_PROTOCOL_UNSUPPORTED` | The upgrade offered only subprotocol versions this gateway does not speak. | Offer `wbskt.v1`. A client bug, not something to retry. |
| 400 `SOCKET_UPGRADE_REQUIRED` | The request to `/ws` was not a WebSocket upgrade. | A client bug, not something to retry. |
| 503, or no answer | The gateway is restarting or unreachable. | Keep the token and retry on the reconnect schedule. |

## 2. Frames

Every message (model §4), both ways, is one **text** message holding one JSON object. It may arrive
as several frames; the 64 KB limit applies to the whole message. Binary frames are ignored.

## 3. Keepalive

The round-trip ping is the model's `sys.ping` and `sys.pong` (model §4.2), sent as text messages
like any other.

Separately, the gateway sends a WebSocket Ping control frame every 30 seconds and drops a connection
that has not answered within another 30, so a device that lost power does not stay "online" for
minutes. WebSocket libraries answer these Pings on their own; a device does nothing for them.

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
- [ ] renews the token after a 401 on the upgrade, and keeps it after any other refusal.

Together with the [message model's checklist](Device.Protocol.md#6-conformance-checklist), this is
everything the conformance suite (`Tests/Conformance`) checks.
