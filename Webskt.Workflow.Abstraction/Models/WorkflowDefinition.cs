using Webskt.Workflow.Abstraction.Enums;
using Webskt.Workflow.Abstraction.Models.Nodes;

namespace Webskt.Workflow.Abstraction.Models;

public sealed class WorkflowDefinition
{
    public Guid WorkflowRefId { get; set; }
    public int WorkspaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public int Version { get; set; } = 1;

    public List<BaseNode> Nodes { get; set; } = new();

    public List<WorkflowEdge> Edges { get; set; } = new();

    public WorkflowConcurrencyPolicy Concurrency { get; set; } = WorkflowConcurrencyPolicy.AllowParallel;

    public Dictionary<string, object> InitialState { get; set; } = new();

    public DateTime CreatedAt { get; set; }
}
