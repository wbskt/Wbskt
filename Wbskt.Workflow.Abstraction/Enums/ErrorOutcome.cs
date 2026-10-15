namespace Wbskt.Workflow.Abstraction.Enums;

public enum ErrorOutcome
{
    ContinueOnError,
    FailBranch,
    FailRun,
    // Not available in this release: WorkflowValidator rejects it at publish. Kept so a definition saved
    // with it still deserialises; the branch then fails as FailBranch would.
    Compensate,
    ContinueAsSucceeded,
    JumpToNode
}
