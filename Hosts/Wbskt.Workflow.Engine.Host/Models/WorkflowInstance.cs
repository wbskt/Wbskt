using Wbskt.Workflow.Engine.Host.Enums;
using Wbskt.Workflow.Engine.Host.Models.TriggerContexts;

namespace Wbskt.Workflow.Engine.Host.Models;

public sealed class WorkflowInstance
{
    public Guid InstanceId { get; set; } = Guid.NewGuid();
    public Guid WorkflowRefId { get; set; }
    public int WorkflowId { get; set; }
    public int WorkspaceId { get; set; }

    public BaseTriggerContext? TriggerContext { get; set; }

    public Dictionary<string, object?> State { get; set; } = new();

    public List<ExecutionPointer> Pointers { get; set; } = new();

    public WorkflowStatus Status { get; set; } = WorkflowStatus.Running;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
}
