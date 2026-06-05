using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Services.Clients;

public interface IWorkflowEngineClient
{
    Task<StartRunResponse> StartManualRunAsync(Guid workflowRefId, StartRunRequest request, CancellationToken ct);
    Task<SignalResponse> SignalAsync(Guid runRefId, string signalName, SignalRequest request, CancellationToken ct);
}
