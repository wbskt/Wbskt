namespace Wbskt.Workflow.Engine.Host.Enums;

public enum WorkflowStatus
{
    Pending = 0,
    Running = 1,
    Paused = 2,
    Completed = 3,
    Terminated = 4,
    Failed = 5
}

public enum ExecutionStatus
{
    Active = 0,
    Waiting = 1, // For Delay or external signals
    Completed = 2,
    Faulted = 3
}
