// Conformance harness for the .NET SDK: drives WbsktClient from JSON lines on stdin and reports
// what it does as JSON lines on stdout. See Tests/Conformance/README.md for the contract.

using System.Text.Json;
using System.Text.Json.Nodes;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.Protocol;

var stdout = Console.Out;
var writeLock = new object();

void Emit(string name, Dictionary<string, object?>? fields = null)
{
    var line = new JsonObject { ["event"] = name };
    foreach (var (key, value) in fields ?? [])
    {
        line[key] = value is null ? null : JsonSerializer.SerializeToNode(value);
    }

    lock (writeLock)
    {
        stdout.WriteLine(line.ToJsonString());
        stdout.Flush();
    }
}

string Env(string name) => Environment.GetEnvironmentVariable(name) ?? "";

var config = new ClientConfig(
    Env("WBSKT_API_URL"),
    Env("WBSKT_SOCKET_URL"),
    Env("WBSKT_DEVICE_NAME"),
    Env("WBSKT_POLICY_PIN") is { Length: > 0 } pin ? pin : null);

var webJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var commands = JsonSerializer.Deserialize<List<CommandCapability>>(Env("WBSKT_COMMANDS") is { Length: > 0 } c ? c : "[]", webJson) ?? [];

await using var client = new WbsktClient(config, new FileClientStorage(Env("WBSKT_STORAGE")));
client.OnConnected += () => Emit("connected");
client.OnDisconnected += () => Emit("disconnected");
client.OnMessageReceived += (type, payload, commandId) =>
    Emit("message", new() { ["type"] = type, ["payload"] = payload, ["commandId"] = commandId });

await client.UpdateCapabilitiesAsync(new ClientCapabilities("", "", "", commands));
await client.StartAsync();
Emit("started");

while (await Console.In.ReadLineAsync() is { } line)
{
    var op = JsonNode.Parse(line)!.AsObject();
    var name = op["op"]?.GetValue<string>();
    if (name == "stop")
    {
        break;
    }

    var type = op["type"]?.GetValue<string>() ?? "";
    try
    {
        switch (name)
        {
            case "send":
                await client.SendAsync(type, op["payload"]?.DeepClone()!);
                Emit("sent", new() { ["type"] = type });
                break;
            case "state":
                var values = op["values"].Deserialize<Dictionary<string, object?>>() ?? [];
                await client.ReportStateAsync(values);
                Emit("sent", new() { ["type"] = MessageTypes.StateReport });
                break;
        }
    }
    catch (ArgumentException error)
    {
        Emit("rejected", new() { ["type"] = type, ["reason"] = error.Message });
    }
}
