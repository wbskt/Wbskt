using Webskt.Workflow.Engine.Host.Enums;

namespace Webskt.Workflow.Engine.Host.Models;

public sealed class WorkflowInstance
{
    public Guid InstanceId { get; set; } = Guid.NewGuid();
    public Guid WorkflowRefId { get; set; }
    public int WorkspaceId { get; set; }

    public object? TriggerData { get; set; }

    public Dictionary<string, object?> State { get; set; } = new();

    public List<ExecutionPointer> Pointers { get; set; } = new();

    public WorkflowStatus Status { get; set; } = WorkflowStatus.Running;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
}
