using Wbskt.Infrastructure;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Services.Workflow;

/// <summary>
/// The engine operations a workspace member starts: a manual run, a signal and a cancel. Each checks
/// the workflow or run is this workspace's, asks the engine through <c>IWorkflowEngineGateway</c>, and
/// announces what happened. Reads stay in <see cref="IWorkflowRunQueryService"/>.
/// </summary>
public interface IWorkflowRunService
{
    /// <summary>
    /// Starts a run from the workflow's manual trigger. Success carries the engine's answer: Started or
    /// Duplicate (a deduplicated retry), or Queued behind an active run. A deprecated workflow, one with
    /// no manual trigger, and a run its concurrency policy dropped are 409s.
    /// </summary>
    Task<Result<StartRunResponse>> StartManualAsync(int workspaceId, Guid workflowRefId, StartRunRequest request, CancellationToken ct);

    Task<Result<SignalResponse>> SignalAsync(int workspaceId, Guid runRefId, string signalName, SignalRequest request, CancellationToken ct);

    /// <summary>
    /// Sends the engine the cancel command. Success means the broker has it; the run reads Cancelling,
    /// then Cancelled, shortly after. 404 for a run that is unknown or another workspace's, 409 for one
    /// already finished, <c>EVENT_BUS_UNAVAILABLE</c> (503) when nothing could be sent.
    /// </summary>
    Task<Result> CancelAsync(int workspaceId, Guid runRefId, string reason, CancellationToken ct);
}
