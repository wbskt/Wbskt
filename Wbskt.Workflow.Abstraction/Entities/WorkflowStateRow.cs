using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Abstraction.Entities;

/// <summary>A published workflow's state, its live version when enabled, and its newest version.</summary>
public sealed record WorkflowStateRow(Guid RefId, int WorkspaceId, WorkflowState State, int? LiveDefinitionId, int? LiveVersion, int LatestVersion);

/// <summary>What <c>dbo.Workflow_Enable</c> did. Nothing changed when the version was already live.</summary>
public sealed record WorkflowEnableResult(bool Changed, int? PreviousLiveDefinitionId, int LiveDefinitionId, int DroppedQueuedEvents);

/// <summary>
/// What <c>dbo.Workflow_Disable</c> did, and the workflow's runs still going (not test runs), which the
/// caller may cancel. Nothing changed when the workflow was already in the state asked for.
/// </summary>
public sealed record WorkflowDisableResult(bool Changed, int? PreviousLiveDefinitionId, int DroppedQueuedEvents, IReadOnlyCollection<long> ActiveRunIds);
