namespace Wbskt.Workflow.Abstraction.Runtime;

public interface ISubWorkflowCompletionHook
{
    Task OnRunCompletedAsync(Guid runRefId, string terminalStatus, CancellationToken ct);

    /// <summary>
    /// As above, with the value the run's <c>control:end</c> node handed back (JSON), delivered to the
    /// parent beside the status. Defaulted to drop it, so the test doubles need no second method.
    /// </summary>
    Task OnRunCompletedAsync(Guid runRefId, string terminalStatus, string? outputJson, CancellationToken ct)
        => OnRunCompletedAsync(runRefId, terminalStatus, ct);
}
