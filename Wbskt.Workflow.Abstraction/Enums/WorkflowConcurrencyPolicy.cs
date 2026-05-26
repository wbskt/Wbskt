namespace Wbskt.Workflow.Abstraction.Enums;

public enum WorkflowConcurrencyPolicy
{
    AllowParallel,
    Queue,
    CancelExisting,
    DropIfRunning
}
