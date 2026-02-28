using Webskt.Workflow.Abstraction.Enums;

namespace Webskt.Workflow.Abstraction.Models;

public sealed class WorkflowInstance
{
    public Guid InstanceId { get; set; } = Guid.NewGuid();
    public Guid WorkflowRefId { get; set; }
    public int WorkspaceId { get; set; }

    /// <summary>
    /// The data that started this specific run (e.g. JSON from a device).
    /// </summary>
    public object? TriggerData { get; set; }

    /// <summary>
    /// The live memory of this instance. Shared across all branches.
    /// </summary>
    public Dictionary<string, object?> State { get; set; } = new();

    /// <summary>
    /// All currently active or waiting execution paths.
    /// </summary>
    public List<ExecutionPointer> Pointers { get; set; } = new();

    public WorkflowStatus Status { get; set; } = WorkflowStatus.Running;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
}
