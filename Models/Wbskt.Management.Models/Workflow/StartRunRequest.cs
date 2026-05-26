using System.Text.Json;

namespace Wbskt.Management.Models.Workflow;

public record StartRunRequest(string TriggerNodeId, Dictionary<string, JsonElement>? Payload);