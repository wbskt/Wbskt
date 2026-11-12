# Client protocol (v1): MQTT binding

How the [message model](Client.Protocol.md) travels over MQTT. Registration, login, the messages
themselves, commands and the reconnect schedule are all on that page; this one covers only what is
particular to MQTT: CONNECT and how it is refused, the topics, and the reason codes. The
`Wbskt.Gateway.MQTT.Host` serves it.

The endpoint is `mqtts://mqtt.<DOMAIN>:8883`, MQTT over TLS. Use it whenever the board can do TLS.

`mqtt://mqtt.<DOMAIN>:1883`, plain MQTT, is open too, for boards too small for TLS. Everything on it
travels in clear text, the access token included, so anyone on the network path can read the token and
connect as the client until it expires. It behaves exactly like 8883 otherwise. An SDK that can do TLS
uses 8883 and offers 1883 only as an explicit opt-in.

The gateway is not a general broker. A client
talks only to the gateway, on its own two topics, and never to another client.

MQTT 3.1.1 and MQTT 5 are both accepted. MQTT 3.1.1 carries no reason code on DISCONNECT, so when the
gateway ends a 3.1.1 client's connection (§4) the client sees only the connection drop.

## 1. Connect

| CONNECT field | Value |
|---|---|
| Client id | The client's `clientRefId`, in its usual form (`xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`, lower case, no braces); any other spelling is refused with 0x85. |
| User name | The client's `clientRefId` again. |
| Password | The `accessToken` login returned (model §2). |
| Clean session / clean start | Set. The gateway keeps no session between connections, whatever is asked. |
| Keep alive | Any; 30 to 60 seconds is a good default. The gateway also probes the TCP connection on its own. |
| Will | Ignored: a will message goes nowhere. The gateway announces a client that drops off on its own. |

The gateway answers CONNACK with:

| Code | Means | Do |
|---|---|---|
| 0x00 Success | Connected. | Send `capabilities` (model §4.2). |
| 0x86 Bad user name or password | The token did not validate (expired, signed by another key, not a client token), or the user name is not the token's client. | Drop the token and log in again (model §2). |
| 0x87 Not authorized | The client lost access after this token was issued: revoked, deleted, or its secret rotated. | Keep the token and retry on the reconnect schedule (model §5); once it expires, login reports what happened. |
| 0x85 Client identifier not valid | The client id is not the token's client, or is not in the usual lower-case form. | A client bug, not something to retry. |
| 0x84 Unsupported protocol version | MQTT 3.1 or another version this gateway does not speak. | Use 3.1.1 or 5. |
| 0x88 Server unavailable, or no answer | The gateway cannot check tokens right now, or is restarting. | Keep the token and retry on the reconnect schedule. |

On MQTT 3.1.1 the same refusals arrive as the 3.1.1 return codes (4 for 0x86, 5 for 0x87, 2 for 0x85).

## 2. Topics

| Topic | Direction | Who |
|---|---|---|
| `wbskt/v1/{clientRefId}/up` | client → gateway | The client publishes every message (model §4) here, and may publish nowhere else. |
| `wbskt/v1/{clientRefId}/down` | gateway → client | The gateway sends every message here. It subscribes the client itself as it connects, so a client need not subscribe; one that does may subscribe to this topic and no other. |

Topics are case-sensitive and use the same lower-case `clientRefId`; a PUBLISH to any other spelling is dropped. `MqttBinding` in `Wbskt.Protocol` builds both. Each PUBLISH carries one message: one JSON object in
UTF-8, at most 64 KB. A packet over 64 KB plus 1 KB of MQTT framing ends the connection at once,
before the gateway reads it.

Messages are delivered at most once (QoS 0) towards the client. A client may publish at QoS 0 or 1;
QoS 1 gets a PUBACK, but delivery is still confirmed by the model's own `sys.ack` for commands, not by
MQTT. Retained messages are ignored and never stored. A publish to any other topic is dropped
(PUBACK 0x87 at QoS 1), and a subscription to any other topic is refused (SUBACK 0x87).

## 3. Keepalive

The round-trip ping is the model's `sys.ping` and `sys.pong` (model §4.2), sent on the two topics like
any other message.

MQTT's own PINGREQ and PINGRESP keep the connection alive at the transport level, as WebSocket ping
frames do. MQTT libraries send them on their own.

## 4. Reason codes

How the gateway signals each reason the model gives for ending a connection (model §5), on an MQTT 5
DISCONNECT:

| Code | Reason | Do |
|---|---|---|
| 0x00 Normal disconnection | Normal | Reconnect on the schedule. |
| 0x8B Server shutting down | Restarting | Reconnect, 0–30 s spread. |
| 0x95 Packet too large | Too big | Fix the sender; reconnect on the schedule. |
| 0x83 Implementation specific error | Server error | Reconnect on the schedule. |
| 0x8E Session taken over | Superseded | Reconnect on the schedule. Log it: repeated, it means another client uses these credentials. |
| 0x87 Not authorized | Revoked | Drop the access token, then reconnect on the schedule; the login decides what happens next. |
| 0x96 Message rate too high | Throttled | Wait a random 30–60 s, then reconnect; send less. |

The reason string is text for logs. Branch on the code, not on the string.

A client closing to install a firmware update (model §4.4) sends an MQTT 5 DISCONNECT with the user
property `reason` set to `updating` (`MqttBinding.CloseReasonProperty`, `MqttBinding.UpdatingReason`).
The server shows the client as updating, not offline, and holds presence triggers back until it
connects again or 5 minutes pass. MQTT 3.1.1 has no properties on DISCONNECT, so there a firmware
update reads as an ordinary close.

## 5. Conformance checklist

An SDK conforms to the MQTT binding when it:

- [ ] connects over TLS by default (plain 1883 only when the user opts in), with its `clientRefId` as client id and user name, and the access token as password;
- [ ] renews the token after CONNACK 0x86, and keeps it after any other refusal;
- [ ] publishes only to its up topic, and reads its messages from its down topic;
- [ ] on MQTT 5: drops the token after DISCONNECT 0x87, waits 30–60 s after 0x96, and logs 0x8E;
- [ ] on MQTT 5: closes for a firmware update with the `reason: updating` user property.

Together with the [message model's checklist](Client.Protocol.md#6-conformance-checklist), this is
everything a conforming MQTT SDK does.
