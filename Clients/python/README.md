# wbskt

Connect a Raspberry Pi, a script or a service to [Wbskt](https://wbskt.com). The device registers
once with a policy PIN, holds a WebSocket open, sends readings and receives commands. It comes back
on its own after a drop.

```sh
pip install wbskt
```

Python 3.9 or later. The only dependency is `websockets`.

## Quick start

```python
from wbskt import WbsktClient

client = WbsktClient("greenhouse-sensor", policy_pin="483920")
client.on_message = lambda message: print("got", message.type, message.payload)
client.start()

client.send("temperature", {"celsius": 21.5})
```

Get the PIN from a registration policy in the console (**Policies**). The first run registers the
device and stores its credentials in your user data folder (`~/.local/share/wbskt/<name>.json` on
Linux), so later runs sign in without registering again. If the policy needs approval, the device
waits and connects once someone approves it in the console.

To keep a script running until Ctrl+C, call `client.run_forever()` instead of `start()`.

## asyncio

```python
import asyncio
from wbskt import AsyncWbsktClient

async def main():
    client = AsyncWbsktClient("greenhouse-sensor", policy_pin="483920")

    async def handle(message):
        print(message.type, message.payload)

    client.on_message = handle
    await client.start()
    while True:
        await client.send("temperature", {"celsius": read_sensor()})
        await asyncio.sleep(10)

asyncio.run(main())
```

Both clients take the same options.

## Commands

Tell the console which commands the device takes, and it offers them with a form:

```python
from wbskt import Command, DataType, Parameter, WbsktClient

relay = Command("relay.set", "Switch the relay", [Parameter("on", DataType.BOOLEAN, is_required=True)])
client = WbsktClient("greenhouse-relay", policy_pin="483920", capabilities=[relay])

def handle(message):
    if message.type == "relay.set":
        set_relay(message.payload["on"])
        client.send("relay.state", {"on": message.payload["on"]})

client.on_message = handle
```

The SDK confirms delivery of a command as soon as it arrives, before your handler runs, and refuses
one that arrives after its expiry without calling your handler. Report the outcome as a message of
your own if it matters. Call `set_capabilities(...)` when the set of commands changes.

## State

```python
client.report_state({"humidity": 41, "fan": "on"})
```

Send only what changed. Numbers are kept as readings and charted in the console.

## While offline

`send` never blocks on the network. While disconnected, messages wait in memory (the last 1,000 by
default, `offline_buffer_size=`) and go out in order, with the time they were made, once the
connection is back. Workflows can tell those late readings from live ones.

## Options

| Option | Default | |
|---|---|---|
| `api_url` | `https://api.dev.wbskt.com` | A self-hosted install's `https://api.<domain>`. |
| `socket_url` | `wss://ws.dev.wbskt.com` | `wss://ws.<domain>`. |
| `storage` | `FileStorage.for_device(name)` | `FileStorage(path)`, `MemoryStorage(ref, secret)`, or your own object with `load`, `save` and `clear`. |
| `capabilities` | none | The commands the device takes. |
| `offline_buffer_size` | 1000 | Messages kept while offline; the oldest go first. |

Handlers can be `on_message(message)`, `on_connected()` and `on_disconnected()`. On `WbsktClient`
they run on the client's background thread, so keep them short.

Messages are at most 64 KB, and types starting with `sys.`, `capabilities` and `state.report` are
reserved; `send` raises `ValueError` for either.

## Protocol

The SDK implements [the device protocol](https://github.com/wbskt/Wbskt/blob/master/Docs/Device.Protocol.md):
token reuse, jittered reconnects that honour `Retry-After`, ping answers and command
acknowledgements. Read it to write a client in another language.
