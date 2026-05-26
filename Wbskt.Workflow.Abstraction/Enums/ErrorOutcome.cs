namespace Wbskt.Workflow.Abstraction.Enums;

public enum ErrorOutcome
{
    ContinueOnError,
    FailBranch,
    FailRun,
    Compensate,
    ContinueAsSucceeded,
    JumpToNode
}
