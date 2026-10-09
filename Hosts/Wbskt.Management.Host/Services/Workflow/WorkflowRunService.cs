using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Services.Workflow;

public sealed class WorkflowRunService : IWorkflowRunService
{
    public static readonly Error BrokerUnavailable = Error.Unavailable("EVENT_BUS_UNAVAILABLE", "The run could not be cancelled right now. Try again shortly.");

    private readonly IWorkflowQueryService _definitions;
    private readonly IWorkflowRunQueryService _runs;
    private readonly IWorkflowEngineGateway _engine;
    private readonly IEventBus _eventBus;
    private readonly ILogger<WorkflowRunService> _logger;

    public WorkflowRunService(
        IWorkflowQueryService definitions,
        IWorkflowRunQueryService runs,
        IWorkflowEngineGateway engine,
        IEventBus eventBus,
        ILogger<WorkflowRunService> logger)
    {
        _definitions = definitions;
        _runs = runs;
        _engine = engine;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<Result<StartRunResponse>> StartManualAsync(int workspaceId, Guid workflowRefId, StartRunRequest request, CancellationToken ct)
    {
        var workflowResult = await _definitions.GetCurrentAsync(workspaceId, workflowRefId, ct);
        if (workflowResult.IsFailure)
        {
            return Result<StartRunResponse>.Failure(workflowResult.Error);
        }

        // A deprecated workflow still resolves as "current" (the lookup returns the latest version
        // regardless of IsEnabled), but its triggers were deregistered - so the engine would find no
        // registration and the caller would get an opaque failure. Say so plainly instead.
        if (workflowResult.Value.Status != "Published")
        {
            _logger.LogInformation("Rejected manual run for deprecated workflow '{RefId}'", workflowRefId);
            return Result<StartRunResponse>.Failure(Error.Conflict("WORKFLOW_DEPRECATED", $"Workflow '{workflowRefId}' is deprecated and cannot be started."));
        }

        StartRunResponse response = await _engine.StartManualRunAsync(workflowRefId, request, ct);
        _logger.LogInformation("Manual run request for workflow '{RefId}' resulted in {Outcome}", workflowRefId, response.Outcome);

        switch (response.Outcome)
        {
            // A deduplicated retry is a success from the caller's point of view - it returns the run
            // the original call started rather than a second one. Queued is accepted but held behind
            // an active run by the concurrency policy.
            case StartRunOutcome.Started or StartRunOutcome.Queued or StartRunOutcome.Duplicate:
                await _eventBus.PublishAsync(new WorkflowRunRequestedEvent(workflowRefId, workspaceId, response.RunRefId, response.Outcome.ToString()), ct);
                return Result<StartRunResponse>.Success(response);

            case StartRunOutcome.Dropped:
                return Result<StartRunResponse>.Failure(Error.Conflict(
                    "RUN_DROPPED_BY_CONCURRENCY_POLICY",
                    $"Workflow '{workflowRefId}' already has an active run and its concurrency policy discarded this request."));

            case StartRunOutcome.NoManualTrigger:
                return Result<StartRunResponse>.Failure(Error.Conflict(
                    "WORKFLOW_HAS_NO_MANUAL_TRIGGER",
                    $"Workflow '{workflowRefId}' has no manual trigger, so it cannot be started this way."));

            default:
                throw new InvalidOperationException($"Unrecognised engine outcome '{response.Outcome}' for workflow '{workflowRefId}'.");
        }
    }

    public async Task<Result<SignalResponse>> SignalAsync(int workspaceId, Guid runRefId, string signalName, SignalRequest request, CancellationToken ct)
    {
        var runResult = await _runs.EnsureRunInWorkspaceAsync(workspaceId, runRefId, ct);
        if (runResult.IsFailure)
        {
            return Result<SignalResponse>.Failure(runResult.Error);
        }

        SignalResponse response = await _engine.SignalAsync(runRefId, signalName, request, ct);
        _logger.LogDebug("Sent signal '{SignalName}' to RunRefId: '{RunRefId}'", signalName, runRefId);
        await _eventBus.PublishAsync(new WorkflowRunSignalSentEvent(runRefId, workspaceId, signalName), ct);
        return Result<SignalResponse>.Success(response);
    }

    public async Task<Result> CancelAsync(int workspaceId, Guid runRefId, string reason, CancellationToken ct)
    {
        var runResult = await _runs.ResolveCancellableRunAsync(workspaceId, runRefId, ct);
        if (runResult.IsFailure)
        {
            return Result.Failure(runResult.Error);
        }

        try
        {
            await _engine.CancelRunAsync(runResult.Value, reason, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Sending the cancel for run '{RunRefId}' failed; the event bus is unavailable. {Message}", runRefId, ex.Message);
            return Result.Failure(BrokerUnavailable);
        }

        _logger.LogInformation("Cancel requested for RunRefId: '{RunRefId}' in WorkspaceId: {WorkspaceId} (Reason: '{Reason}')", runRefId, workspaceId, reason);
        await _eventBus.PublishAsync(new WorkflowRunCancelRequestedEvent(runRefId, workspaceId, reason), ct);
        return Result.Success();
    }
}
