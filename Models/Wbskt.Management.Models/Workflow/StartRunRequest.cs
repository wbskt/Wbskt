using System.Text.Json;

namespace Wbskt.Management.Models.Workflow;

/// <param name="TriggerNodeId">The manual trigger to start the run from.</param>
/// <param name="Payload">What the run reads as <c>trigger.body</c>.</param>
/// <param name="IdempotencyKey">Names the request, so a retry of it starts no second run.</param>
public record StartRunRequest(Guid TriggerNodeId, Dictionary<string, JsonElement>? Payload, string? IdempotencyKey = null);