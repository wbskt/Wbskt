using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;

namespace Wbskt.Management.Host.Models;

public record WorkflowSummaryResponse(
    Guid WorkflowRefId,
    string Name,
    string Description,
    bool IsEnabled,
    DateTime CreatedAt
);

public record CreateWorkflowRequest(
    string Name,
    string? Description
);

public record UpdateWorkflowRequest(
    string Name,
    string? Description,
    bool IsEnabled,
    WorkflowConcurrencyPolicy Concurrency,
    List<BaseNode> Nodes,
    List<WorkflowEdge> Edges,
    Dictionary<string, object?> InitialState
);
