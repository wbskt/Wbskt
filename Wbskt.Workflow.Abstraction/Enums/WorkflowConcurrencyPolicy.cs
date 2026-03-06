namespace Wbskt.Workflow.Abstraction.Enums;

public enum WorkflowConcurrencyPolicy
{
    AllowParallel = 0,
    Queue = 1,
    CancelExisting = 2
}
