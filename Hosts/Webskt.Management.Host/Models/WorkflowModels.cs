using Webskt.Workflow.Abstraction.Enums;
using Webskt.Workflow.Abstraction.Models;
using Webskt.Workflow.Abstraction.Models.Nodes;

namespace Webskt.Management.Host.Models;

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
