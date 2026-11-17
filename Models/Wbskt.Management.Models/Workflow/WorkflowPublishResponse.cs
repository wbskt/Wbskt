using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Management.Models.Workflow;

/// <summary>The version just published, and the workflow's state and live version after it.</summary>
public record WorkflowPublishResponse(Guid RefId, int Version, WorkflowState State, int? LiveVersion);
