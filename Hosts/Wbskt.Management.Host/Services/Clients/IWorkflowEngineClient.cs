using System.Text.Json;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Services.Clients;

public interface IWorkflowEngineClient
{
    Task<StartRunResponse> StartManualRunAsync(Guid workflowRefId, StartRunRequest request, CancellationToken ct);
    Task<SignalResponse> SignalAsync(Guid runRefId, string signalName, SignalRequest request, CancellationToken ct);
    Task<WakeResponse> WakeAsync(string token, JsonElement payload, CancellationToken ct);
    /// <summary>
    /// Relays a public webhook callback to the engine. <c>secret</c> is whatever the anonymous caller
    /// presented in <c>X-Wbskt-Secret</c>, passed through verbatim so the engine can compare it against
    /// the trigger's configured secret; null when the caller sent none.
    /// </summary>
    Task<WebhookResponse> WebhookAsync(Guid workspaceRef, string path, JsonElement payload, string? secret, string? idempotencyKey, CancellationToken ct);
}
