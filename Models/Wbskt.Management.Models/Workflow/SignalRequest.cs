using System.Text.Json;

namespace Wbskt.Management.Models.Workflow;

public record SignalRequest(string SignalName, JsonElement Payload);