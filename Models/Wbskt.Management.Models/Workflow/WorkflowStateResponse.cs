using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Management.Models.Workflow;

/// <summary>
/// A workflow's state after a change to it. <c>LiveVersion</c> is set only when it is enabled. A disable
/// also says how many runs it cancelled and how many queued trigger events it dropped; both are 0 otherwise.
/// </summary>
public record WorkflowStateResponse(Guid RefId, WorkflowState State, int? LiveVersion, int CancelledRuns = 0, int DroppedQueuedEvents = 0);
