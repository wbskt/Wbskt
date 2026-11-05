# Wbskt.Client

Connect a board, script or service to [Wbskt](https://wbskt.com). It registers with a policy PIN
once, keeps the credentials it gets, holds a WebSocket open, buffers messages while offline and
reconnects on its own.

```bash
dotnet add package Wbskt.Client --prerelease
```

This is a 0.9 beta (`--prerelease` finds it): the client protocol it speaks may still change before 1.0.

```csharp
using Wbskt.Client.Sdk;

await using var wbskt = new WbsktClient("greenhouse-sensor", "your-policy-pin");

wbskt.OnMessageReceived += (type, payload, commandId) =>
    Console.WriteLine($"{type}: {payload}");

await wbskt.StartAsync();

await wbskt.SendAsync("temperature", new { celsius = 21.5 });
await wbskt.ReportStateAsync(new Dictionary<string, object?> { ["celsius"] = 21.5 });
```

The PIN comes from a registration policy in the console (Policies). The first start registers the
client and saves its credentials in `wbskt/greenhouse-sensor.json` under your local application data
folder (`~/.local/share` on Linux); later starts sign in with them.

**Your own server or storage.** Pass a `ClientConfig` with your API and socket URLs, and optionally
an `IClientStorage` of your own (a database row, a secrets store):

```csharp
var config = new ClientConfig("https://api.example.com", "wss://ws.example.com", "greenhouse-sensor", "your-policy-pin");
await using var wbskt = new WbsktClient(config, new FileClientStorage("/etc/wbskt/client.json"));
```

**Commands.** Declare the commands the client takes with `UpdateCapabilitiesAsync` and they show up
in the console's send panel. Delivery acks and expired-command refusals are sent for you.

**Offline.** `SendAsync` while disconnected queues the message (1,000 by default,
`ClientConfig.OfflineBufferSize`) and sends it in order, with its original time, on reconnect. Sends
are paced at 8 a second after a burst of 50, under the gateway's limit, so a full buffer takes about
two minutes to drain.

**Handlers.** Messages reach `OnMessageReceived` one at a time, in order, on the SDK's own task. A
handler that throws doesn't drop the connection; the exception is raised on `OnHandlerError`.

The wire protocol every Wbskt SDK implements is documented in
[Docs/Client.Protocol.md](https://github.com/wbskt/Wbskt/blob/master/Docs/Client.Protocol.md), with the
WebSocket specifics in [Docs/Client.Protocol.WebSocket.md](https://github.com/wbskt/Wbskt/blob/master/Docs/Client.Protocol.WebSocket.md).
