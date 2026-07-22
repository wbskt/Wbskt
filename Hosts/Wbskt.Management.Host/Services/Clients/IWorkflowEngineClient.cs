using System.Text.Json;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Services.Clients;

public interface IWorkflowEngineClient
{
    Task<StartRunResponse> StartManualRunAsync(Guid workflowRefId, StartRunRequest request, CancellationToken ct);
    Task<SignalResponse> SignalAsync(Guid runRefId, string signalName, SignalRequest request, CancellationToken ct);
    Task<WakeResponse> WakeAsync(string token, JsonElement payload, CancellationToken ct);
    Task<WebhookResponse> WebhookAsync(string path, JsonElement payload, CancellationToken ct);
}
