namespace Wbskt.Management.Models.Workflow;

/// <param name="Trigger">What started the run. On the list endpoints only.</param>
/// <param name="CreditsUsed">Credits charged so far across the run's steps. On the list endpoints only.</param>
/// <param name="FailedAt">For a Failed, PartiallyFailed or Failing run, the step that failed first and its error code.</param>
/// <param name="EndedAt">For a Succeeded run, the last step it completed: its End step, or the last of its last branch.</param>
/// <param name="CurrentStep">For a live run, the step it is on, with the attempt when that step is being retried.</param>
/// <param name="WorkflowName">The workflow's name at the run's version. On the list endpoints only.</param>
public record RunSummaryDto(
    Guid RefId,
    Guid WorkflowDefinitionRefId,
    int Version,
    string Status,
    string? CorrelationKey,
    DateTime StartedAt,
    DateTime? CompletedAt,
    RunTriggerDto? Trigger = null,
    decimal? CreditsUsed = null,
    RunStepDto? FailedAt = null,
    RunStepDto? EndedAt = null,
    RunStepDto? CurrentStep = null,
    string? WorkflowName = null);

/// <param name="Kind">The trigger node's kind, such as <c>trigger:client</c>; null when the version no longer has the node.</param>
/// <param name="Summary">A short description from the trigger's settings, such as <c>Device greenhouse-1 sent temperature</c>.</param>
public record RunTriggerDto(Guid NodeId, string? Kind, string? Name, string? Summary);

/// <param name="Name">The step's name in the run's version; null when the version no longer has the node.</param>
/// <param name="ErrorCode">On <c>failedAt</c>: the failure's code.</param>
/// <param name="Attempt">On <c>currentStep</c>, while it is being retried: the attempt under way.</param>
/// <param name="MaxAttempts">With <paramref name="Attempt"/>: how many attempts the step may make.</param>
public record RunStepDto(Guid NodeId, string? Name, string? Kind = null, string? ErrorCode = null, int? Attempt = null, int? MaxAttempts = null);
