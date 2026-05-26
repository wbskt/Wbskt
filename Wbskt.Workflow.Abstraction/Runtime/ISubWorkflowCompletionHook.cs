namespace Wbskt.Workflow.Abstraction.Runtime;

public interface ISubWorkflowCompletionHook
{
    Task OnRunCompletedAsync(Guid runRefId, string terminalStatus, CancellationToken ct);
}
