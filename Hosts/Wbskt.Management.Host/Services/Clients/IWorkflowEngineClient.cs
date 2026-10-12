using System.Text.Json;
using Wbskt.Infrastructure;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Engine.Contracts;

namespace Wbskt.Management.Host.Services.Clients;

public interface IWorkflowEngineClient
{
    Task<StartRunResponse> StartManualRunAsync(Guid workflowRefId, StartRunRequest request, CancellationToken ct);
    Task<SignalResponse> SignalAsync(Guid runRefId, string signalName, SignalRequest request, CancellationToken ct);

    /// <summary>
    /// Has the engine validate an integration and seal its secret. The engine's refusal comes back as
    /// its error (a validation code); an engine that cannot be reached, or has no master key, as
    /// <see cref="ErrorType.Unavailable"/>.
    /// </summary>
    Task<Result<IntegrationSealResponse>> SealIntegrationAsync(IntegrationSealRequest request, CancellationToken ct);

    /// <summary>Has the engine try an integration against its service, with errors as <see cref="SealIntegrationAsync"/>.</summary>
    Task<Result<IntegrationProbeResponse>> ProbeIntegrationAsync(IntegrationProbeRequest request, CancellationToken ct);
}
