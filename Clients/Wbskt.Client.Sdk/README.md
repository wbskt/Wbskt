# Wbskt.Client

Connect a device, script or service to [Wbskt](https://wbskt.com). It registers with a policy PIN
once, keeps the credentials it gets, holds a WebSocket open, buffers messages while offline and
reconnects on its own.

```bash
dotnet add package Wbskt.Client --prerelease
```

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
device and saves its credentials in `wbskt/greenhouse-sensor.json` under your local application data
folder (`~/.local/share` on Linux); later starts sign in with them.

**Your own server or storage.** Pass a `ClientConfig` with your API and socket URLs, and optionally
an `IClientStorage` of your own (a database row, a secrets store):

```csharp
var config = new ClientConfig("https://api.example.com", "wss://ws.example.com", "greenhouse-sensor", "your-policy-pin");
await using var wbskt = new WbsktClient(config, new FileClientStorage("/etc/wbskt/device.json"));
```

**Commands.** Declare the commands the device takes with `UpdateCapabilitiesAsync` and they show up
in the console's send panel. Delivery acks and expired-command refusals are sent for you.

**Offline.** `SendAsync` while disconnected queues the message (1,000 by default,
`ClientConfig.OfflineBufferSize`) and sends it in order, with its original time, on reconnect.

The wire protocol every Wbskt SDK implements is documented in
[Docs/Device.Protocol.md](https://github.com/wbskt/Wbskt/blob/master/Docs/Device.Protocol.md).
