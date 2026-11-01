# SDK conformance suite

Checks a device SDK against the device protocol: the
[message model](../../Docs/Device.Protocol.md) (checklist in §6) and the
[WebSocket binding](../../Docs/Device.Protocol.WebSocket.md) (checklist in §5). Every scenario checks
an item on one of those two checklists:

| Checklist | Scenarios |
|---|---|
| Message model | registering once, pending approval, registering again, reusing the token, `Retry-After`, `capabilities` first, command acks, expired commands, plain messages, `sentAt` and the offline backlog, reserved types |
| WebSocket binding | the subprotocol and bearer token, renewing after a 401 on the upgrade, keeping the token while the gateway is down, ping and pong |

A future binding (MQTT, HTTPS) adds its own checklist and its own scenarios.

The suite fakes the two HTTP endpoints and the WebSocket gateway (`fake_wbskt.py`), so nothing else
has to run, and each scenario takes a few seconds. The fake can misbehave on cue: refuse a token,
throttle a login, forget a device, close or refuse the socket.

The SDK runs as a separate process, a **harness**, so any language can take part. CI
(`.github/workflows/sdk-conformance.yml`) runs it for the Python and .NET SDKs; the ESP32 SDK runs it
in its own repository (below).

```sh
pip install -r requirements.txt
pip install -e ../../Clients/python
WBSKT_HARNESS="python harnesses/python/harness.py" python -m pytest -v

dotnet build harnesses/dotnet -c Release -o /tmp/harness
WBSKT_HARNESS="dotnet /tmp/harness/Wbskt.Conformance.Harness.dll" python -m pytest -v
```

The suite takes about four minutes, because it waits for the SDK's real reconnect delays (up to
10 s after a drop) rather than shortening them.

## Writing a harness

A harness is a program that starts one client and then does what the suite tells it. It reads its
settings from the environment:

| Variable | |
|---|---|
| `WBSKT_API_URL` | Base URL of the fake API (`http://127.0.0.1:<port>`). |
| `WBSKT_SOCKET_URL` | Base URL of the fake socket host (`ws://127.0.0.1:<port>`); the SDK adds `/ws`. |
| `WBSKT_DEVICE_NAME` | The device name to register with. |
| `WBSKT_POLICY_PIN` | The policy PIN, or empty. |
| `WBSKT_STORAGE` | A file for the credentials. The same file is used across restarts of one scenario. |
| `WBSKT_COMMANDS` | The commands to declare, as the JSON array of the `capabilities` message's `capabilities`. |

The harness starts the client at once. It then reads one JSON object per line on stdin:

| Line | Do |
|---|---|
| `{"op":"send","type":"…","payload":…}` | Send an application message. |
| `{"op":"state","values":{…}}` | Report state. |
| `{"op":"stop"}` | Stop the client and exit. |

It writes one JSON object per line on stdout, flushing each one. Other lines are ignored, so the SDK
may log there.

| Line | When |
|---|---|
| `{"event":"started"}` | The client was started. |
| `{"event":"connected"}` / `{"event":"disconnected"}` | The socket opened or closed. |
| `{"event":"message","type":"…","payload":…,"commandId":"…"\|null}` | The SDK handed a message to the app. |
| `{"event":"sent","type":"…"}` | `send` or `state` returned. |
| `{"event":"rejected","type":"…","reason":"…"}` | The SDK refused to send the message, for example for a reserved type. |

The ESP32 library can't run on a host as it is. [Wbskt.Arduino](https://github.com/wbskt/Wbskt.Arduino)
builds its client for Linux over shims of the ESP32 libraries (`test/conformance`), and its own CI
runs this suite against that build:

```sh
make -C <Wbskt.Arduino>/test/conformance   # after cloning ArduinoJson into test/host/ArduinoJson
WBSKT_HARNESS=<Wbskt.Arduino>/test/conformance/wbskt-harness python -m pytest -v
```
